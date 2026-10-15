using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LiteNetLib;
using Underthrone.Shared.Protocol;
using Underthrone.Shared.Sim;

namespace Underthrone.Server
{
    /// UDP front end for one World: accepts players, feeds their inputs in, and sends snapshots
    /// and combat events out every tick.
    public sealed class GameServer : INetEventListener, IDisposable
    {
        public const int MAX_PLAYERS = 200;
        /// Remote entities are drawn about this far in the past on clients (interpolation buffer).
        private const int CLIENT_INTERP_TICKS = 3;

        private readonly NetManager _net;
        private readonly World _world;
        private readonly Action<string> _log;
        private readonly Dictionary<int, ServerPlayer> _playersByPeer = new Dictionary<int, ServerPlayer>();
        private readonly Dictionary<ushort, NetPeer> _peersByPlayer = new Dictionary<ushort, NetPeer>();
        private readonly PacketWriter _writer = new PacketWriter(1024);
        private readonly List<EntitySnapshot> _entities = new List<EntitySnapshot>();
        private readonly PlayerInput[] _inputScratch = new PlayerInput[Messages.MAX_INPUTS_PER_PACKET];

        public GameServer(World world, Action<string> log)
        {
            _world = world;
            _log = log ?? (_ => { });
            _net = new NetManager(this)
            {
                AutoRecycle = true,
                UpdateTime = 5,
                DisconnectTimeout = 10000,
            };
        }

        public World World => _world;
        public int Port => _net.LocalPort;

        public bool Start(int port)
        {
            bool ok = _net.Start(port);
            if (ok)
            {
                _log($"Underthrone server listening on UDP {_net.LocalPort} at {SimConstants.TICK_RATE} Hz");
            }
            return ok;
        }

        /// Runs the fixed-step loop until the token is cancelled.
        public void Run(CancellationToken token)
        {
            var clock = Stopwatch.StartNew();
            double tickSeconds = SimConstants.TICK_DELTA;
            double nextTick = clock.Elapsed.TotalSeconds;
            while (!token.IsCancellationRequested)
            {
                double now = clock.Elapsed.TotalSeconds;
                if (now < nextTick)
                {
                    _net.PollEvents();
                    Thread.Sleep(1);
                    continue;
                }
                TickOnce();
                nextTick += tickSeconds;
                if (now - nextTick > 1.0)
                {
                    _log("Server fell more than 1 s behind; skipping ahead");
                    nextTick = now;
                }
            }
        }

        /// One full server tick: receive, simulate, send. Public so tests can drive it.
        public void TickOnce()
        {
            _net.PollEvents();
            _world.Step();
            BroadcastEvents();
            SendSnapshots();
        }

        public void Dispose() => _net.Stop();

        private void BroadcastEvents()
        {
            IReadOnlyList<CombatEventData> events = _world.Events;
            for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
            {
                CombatEventData e = events[eventIndex];
                _writer.Reset();
                Messages.WriteCombatEvent(_writer, e.Tick, e.AttackerId, e.TargetId, e.Outcome, e.Damage);
                _net.SendToAll(_writer.Buffer, 0, _writer.Length, DeliveryMethod.ReliableOrdered);
            }
        }

        private void SendSnapshots()
        {
            _world.BuildEntitySnapshots(_entities);
            foreach (KeyValuePair<ushort, NetPeer> pair in _peersByPlayer)
            {
                ServerPlayer player = _playersByPeer[pair.Value.Id];
                _writer.Reset();
                Messages.WriteSnapshot(_writer, _world.Tick, player.LastProcessedInputTick, player.State, _entities);
                pair.Value.Send(_writer.Buffer, 0, _writer.Length, DeliveryMethod.Sequenced);
            }
        }

        // --- INetEventListener ---

        public void OnConnectionRequest(ConnectionRequest request)
        {
            if (_net.ConnectedPeersCount >= MAX_PLAYERS)
            {
                request.Reject();
                return;
            }
            request.AcceptIfKey(Messages.CONNECTION_KEY);
        }

        public void OnPeerConnected(NetPeer peer)
        {
            _log($"Peer {peer.Id} connected from {peer}");
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (!_playersByPeer.TryGetValue(peer.Id, out ServerPlayer player))
            {
                return;
            }
            _playersByPeer.Remove(peer.Id);
            _peersByPlayer.Remove(player.Id);
            _world.RemovePlayer(player);
            _writer.Reset();
            Messages.WritePlayerLeft(_writer, player.Id);
            _net.SendToAll(_writer.Buffer, 0, _writer.Length, DeliveryMethod.ReliableOrdered);
            _log($"{player.Name} (#{player.Id}) left: {disconnectInfo.Reason}");
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
        {
            var r = new PacketReader(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
            try
            {
                var type = (MessageType)r.ReadByte();
                switch (type)
                {
                    case MessageType.Hello:
                        HandleHello(peer, ref r);
                        break;
                    case MessageType.Inputs:
                        HandleInputs(peer, ref r);
                        break;
                    default:
                        peer.Disconnect();
                        break;
                }
            }
            catch (ProtocolException ex)
            {
                _log($"Bad packet from peer {peer.Id}: {ex.Message}; disconnecting");
                peer.Disconnect();
            }
        }

        public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            if (!_playersByPeer.TryGetValue(peer.Id, out ServerPlayer player))
            {
                return;
            }
            // What the player saw lags the server by one-way latency plus the remote-entity
            // interpolation buffer. Capped so a high-ping player can't rewind time too far.
            int oneWayTicks = (int)MathF.Round(latency / 1000f * SimConstants.TICK_RATE);
            player.LatencyTicks = Math.Min(SimConstants.MAX_LAG_COMPENSATION_TICKS, oneWayTicks + CLIENT_INTERP_TICKS);
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            _log($"Network error {socketError} from {endPoint}");
        }

        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }

        private void HandleHello(NetPeer peer, ref PacketReader r)
        {
            Messages.ReadHello(ref r, out ushort version, out string name);
            if (version != Messages.PROTOCOL_VERSION || _playersByPeer.ContainsKey(peer.Id))
            {
                peer.Disconnect();
                return;
            }
            name = string.IsNullOrWhiteSpace(name) ? "Wanderer" : name.Trim();
            if (name.Length > 24)
            {
                name = name.Substring(0, 24);
            }
            ServerPlayer player = _world.AddPlayer(name);
            _playersByPeer[peer.Id] = player;
            _peersByPlayer[player.Id] = peer;
            _writer.Reset();
            Messages.WriteWelcome(_writer, player.Id, _world.Tick);
            peer.Send(_writer.Buffer, 0, _writer.Length, DeliveryMethod.ReliableOrdered);
            _log($"{name} joined as #{player.Id}");
        }

        private void HandleInputs(NetPeer peer, ref PacketReader r)
        {
            if (!_playersByPeer.TryGetValue(peer.Id, out ServerPlayer player))
            {
                return;
            }
            int count = Messages.ReadInputs(ref r, _inputScratch);
            for (int inputIndex = 0; inputIndex < count; inputIndex++)
            {
                player.Enqueue(_inputScratch[inputIndex]);
            }
        }
    }
}

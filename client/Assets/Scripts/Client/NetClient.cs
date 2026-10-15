using System.Collections.Generic;
using LiteNetLib;
using Underthrone.Shared.Protocol;
using Underthrone.Shared.Sim;
using UnityEngine;

namespace Underthrone.Client
{
    /// Connects to the Underthrone server, routes snapshots to the local player and remote
    /// entities, and sends the local player's inputs.
    [DisallowMultipleComponent]
    public sealed class NetClient : MonoBehaviour
    {
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 7777;
        [SerializeField] private string _playerName = "Wanderer";
        [SerializeField] private LocalPlayer _localPlayer;
        [SerializeField] private RemoteEntities _remoteEntities;

        private readonly PacketWriter _writer = new PacketWriter(512);
        private readonly List<EntitySnapshot> _entities = new List<EntitySnapshot>();
        private readonly List<string> _combatLog = new List<string>();
        private EventBasedNetListener _listener;
        private NetManager _net;
        private NetPeer _server;
        private double _serverClockOffset;
        private bool _hasClock;

        public ushort PlayerId { get; private set; }
        public bool IsConnected => _server != null && _server.ConnectionState == ConnectionState.Connected;
        public int RoundTripMs => _server != null ? _server.RoundTripTime : 0;
        public uint LastServerTick { get; private set; }
        public IReadOnlyList<string> CombatLog => _combatLog;

        /// Best estimate of the server's simulation time right now (seconds).
        public double ServerTimeNow => Time.unscaledTimeAsDouble + _serverClockOffset;

        private void Awake()
        {
            _listener = new EventBasedNetListener();
            _listener.PeerConnectedEvent += OnConnected;
            _listener.PeerDisconnectedEvent += OnDisconnected;
            _listener.NetworkReceiveEvent += OnReceive;
            _net = new NetManager(_listener) { DisconnectTimeout = 10000 };
        }

        private void Start()
        {
            _net.Start();
            _server = _net.Connect(_host, _port, Messages.CONNECTION_KEY);
            Debug.Log($"[Underthrone] Connecting to {_host}:{_port}");
        }

        private void Update()
        {
            _net.PollEvents();
        }

        private void OnDestroy()
        {
            _net?.Stop();
        }

        public void SendInputs(IReadOnlyList<PlayerInput> recent, int start, int count)
        {
            if (!IsConnected || PlayerId == 0)
            {
                return;
            }
            _writer.Reset();
            Messages.WriteInputs(_writer, recent, start, count);
            _server.Send(_writer.Buffer, 0, _writer.Length, DeliveryMethod.Unreliable);
        }

        private void OnConnected(NetPeer peer)
        {
            _writer.Reset();
            Messages.WriteHello(_writer, _playerName);
            peer.Send(_writer.Buffer, 0, _writer.Length, DeliveryMethod.ReliableOrdered);
        }

        private void OnDisconnected(NetPeer peer, DisconnectInfo info)
        {
            Debug.LogWarning($"[Underthrone] Disconnected: {info.Reason}");
            PlayerId = 0;
        }

        private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            var r = new PacketReader(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
            try
            {
                switch ((MessageType)r.ReadByte())
                {
                    case MessageType.Welcome:
                        Messages.ReadWelcome(ref r, out ushort id, out _);
                        PlayerId = id;
                        Debug.Log($"[Underthrone] Joined as #{id}");
                        break;
                    case MessageType.Snapshot:
                        HandleSnapshot(ref r);
                        break;
                    case MessageType.CombatEvent:
                        HandleCombatEvent(ref r);
                        break;
                    case MessageType.PlayerLeft:
                        _remoteEntities.Remove(r.ReadUShort());
                        break;
                }
            }
            catch (ProtocolException ex)
            {
                Debug.LogError($"[Underthrone] Bad packet: {ex.Message}");
            }
            reader.Recycle();
        }

        private void HandleSnapshot(ref PacketReader r)
        {
            Messages.ReadSnapshot(ref r, out uint serverTick, out uint ack, out PlayerState you, _entities);
            if (serverTick <= LastServerTick)
            {
                return;
            }
            LastServerTick = serverTick;

            double serverTime = serverTick * (double)SimConstants.TICK_DELTA;
            double offset = serverTime - Time.unscaledTimeAsDouble;
            // Smooth the clock so jitter doesn't make remote entities stutter.
            _serverClockOffset = _hasClock ? _serverClockOffset + (offset - _serverClockOffset) * 0.05 : offset;
            _hasClock = true;

            _localPlayer.OnServerState(ack, you);
            _remoteEntities.OnSnapshot(serverTime, _entities, PlayerId);
        }

        private void HandleCombatEvent(ref PacketReader r)
        {
            Messages.ReadCombatEvent(ref r, out _, out ushort attacker, out ushort target, out HitOutcome outcome, out ushort damage);
            if (target == PlayerId)
            {
                _localPlayer.OnStruck(outcome, damage);
            }
            else if (attacker == PlayerId)
            {
                _localPlayer.OnLandedHit();
            }
            _remoteEntities.OnCombatEvent(target, outcome);
            _combatLog.Add($"#{attacker} -> #{target}: {outcome}{(damage > 0 ? $" ({damage})" : string.Empty)}");
            if (_combatLog.Count > 6)
            {
                _combatLog.RemoveAt(0);
            }
        }
    }
}

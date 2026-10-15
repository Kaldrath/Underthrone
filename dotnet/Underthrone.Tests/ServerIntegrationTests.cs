using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using LiteNetLib;
using Underthrone.Server;
using Underthrone.Shared.Protocol;
using Underthrone.Shared.Sim;
using Xunit;

namespace Underthrone.Tests
{
    public class ServerIntegrationTests
    {
        [Fact]
        public void ClientJoins_WalksForward_AndSeesItselfMoveInSnapshots()
        {
            using var server = new GameServer(new World(), null);
            Assert.True(server.Start(0));

            var listener = new EventBasedNetListener();
            var client = new NetManager(listener) { DisconnectTimeout = 10000 };
            bool connectedEventFired = false;
            // Same event signatures the Unity NetClient uses.
            listener.PeerConnectedEvent += peer => connectedEventFired = true;
            listener.PeerDisconnectedEvent += (peer, info) => { DisconnectReason reason = info.Reason; };
            ushort myId = 0;
            uint lastAck = 0;
            PlayerState me = default;
            int snapshots = 0;
            var entities = new List<EntitySnapshot>();
            listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                var r = new PacketReader(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
                var type = (MessageType)r.ReadByte();
                if (type == MessageType.Welcome)
                {
                    Messages.ReadWelcome(ref r, out myId, out _);
                }
                else if (type == MessageType.Snapshot)
                {
                    Messages.ReadSnapshot(ref r, out _, out lastAck, out me, entities);
                    snapshots++;
                }
                reader.Recycle();
            };
            Assert.True(client.Start());
            NetPeer serverPeer = client.Connect("127.0.0.1", server.Port, Messages.CONNECTION_KEY);

            var writer = new PacketWriter();
            bool helloSent = false;
            var inputs = new List<PlayerInput>();
            uint inputTick = 0;
            var clock = Stopwatch.StartNew();
            float startZ = float.NaN;

            while (clock.ElapsedMilliseconds < 5000)
            {
                client.PollEvents();
                if (!helloSent && serverPeer.ConnectionState == ConnectionState.Connected)
                {
                    writer.Reset();
                    Messages.WriteHello(writer, "Integration");
                    serverPeer.Send(writer.Buffer, 0, writer.Length, DeliveryMethod.ReliableOrdered);
                    helloSent = true;
                }
                if (myId != 0 && snapshots > 0)
                {
                    if (float.IsNaN(startZ))
                    {
                        startZ = me.Position.Z;
                    }
                    inputs.Add(PlayerInput.Create(++inputTick, 0f, 1f, 0f, InputButtons.None));
                    writer.Reset();
                    int count = System.Math.Min(4, inputs.Count);
                    Messages.WriteInputs(writer, inputs, inputs.Count - count, count);
                    serverPeer.Send(writer.Buffer, 0, writer.Length, DeliveryMethod.Unreliable);
                }
                server.TickOnce();
                if (lastAck >= 30)
                {
                    break;
                }
                Thread.Sleep(5);
            }
            client.Stop();

            Assert.True(connectedEventFired);
            Assert.True(serverPeer.RoundTripTime >= 0);
            Assert.NotEqual(0, myId);
            Assert.True(snapshots > 10, $"only {snapshots} snapshots");
            Assert.True(lastAck >= 30, $"server acked only up to input {lastAck}");
            Assert.True(me.Position.Z - startZ > 3f, $"moved only {me.Position.Z - startZ:F2} m");
            Assert.Contains(entities, e => e.Id == myId && e.Kind == EntityKind.Player);
        }
    }
}

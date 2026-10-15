using System.Collections.Generic;
using System.Numerics;
using Underthrone.Shared.Client;
using Underthrone.Shared.Protocol;
using Underthrone.Shared.Sim;
using Xunit;

namespace Underthrone.Tests
{
    public class ProtocolAndPredictionTests
    {
        [Fact]
        public void Snapshot_RoundTripsPlayerStateAndEntities()
        {
            PlayerState you = PlayerState.Spawn(new Vector3(1.5f, 0, -2.25f), 1.2f);
            you.Action = PlayerAction.Dodging;
            you.ActionTicks = 7;
            you.IFrameTicks = 3;
            you.Stamina = 61.5f;
            you.DodgeDirection = new Vector3(0.6f, 0, -0.8f);
            you.SwingId = 42;
            you.PrevButtons = InputButtons.Dodge | InputButtons.Block;
            var entities = new List<EntitySnapshot>
            {
                new EntitySnapshot { Id = 1, Kind = EntityKind.Player, X = 1.5f, Z = -2.25f, YawQ = 1234, Action = 5, Health = 77 },
                new EntitySnapshot { Id = 1000, Kind = EntityKind.Ghoul, X = 9f, Z = 3f, YawQ = 60000, Action = 2, Health = 120 },
            };

            var w = new PacketWriter(8);
            Messages.WriteSnapshot(w, 99, 57, you, entities);
            var r = new PacketReader(w.Buffer, 0, w.Length);
            Assert.Equal(MessageType.Snapshot, (MessageType)r.ReadByte());
            var read = new List<EntitySnapshot>();
            Messages.ReadSnapshot(ref r, out uint tick, out uint ack, out PlayerState back, read);

            Assert.Equal(99u, tick);
            Assert.Equal(57u, ack);
            Assert.Equal(you.Position, back.Position);
            Assert.Equal(you.Stamina, back.Stamina);
            Assert.Equal(you.Action, back.Action);
            Assert.Equal(you.IFrameTicks, back.IFrameTicks);
            Assert.Equal(you.DodgeDirection, back.DodgeDirection);
            Assert.Equal(you.SwingId, back.SwingId);
            Assert.Equal(you.PrevButtons, back.PrevButtons);
            Assert.Equal(entities, read);
            Assert.Equal(0, r.Remaining);
        }

        [Fact]
        public void Inputs_RoundTripAndKeepOnlyTheNewestWhenTooMany()
        {
            var inputs = new List<PlayerInput>();
            for (uint tick = 1; tick <= 12; tick++)
            {
                inputs.Add(PlayerInput.Create(tick, 0.5f, 1f, 0.3f, InputButtons.Attack));
            }
            var w = new PacketWriter();
            Messages.WriteInputs(w, inputs, 0, inputs.Count);
            var r = new PacketReader(w.Buffer, 0, w.Length);
            r.ReadByte();
            var into = new PlayerInput[Messages.MAX_INPUTS_PER_PACKET];
            int count = Messages.ReadInputs(ref r, into);
            Assert.Equal(Messages.MAX_INPUTS_PER_PACKET, count);
            Assert.Equal(5u, into[0].Tick);
            Assert.Equal(12u, into[count - 1].Tick);
            Assert.Equal(inputs[11].YawQ, into[count - 1].YawQ);
        }

        [Fact]
        public void TruncatedPacket_ThrowsProtocolException()
        {
            var w = new PacketWriter();
            Messages.WriteWelcome(w, 3, 500);
            Assert.Throws<ProtocolException>(() =>
            {
                var r = new PacketReader(w.Buffer, 0, w.Length - 1);
                r.ReadByte();
                Messages.ReadWelcome(ref r, out _, out _);
            });
        }

        [Fact]
        public void Prediction_MatchesServerExactly_WhenNothingInterferes()
        {
            PlayerState start = PlayerState.Spawn(Vector3.Zero, 0f);
            var client = new ClientPrediction(start);
            PlayerState server = start;
            for (uint tick = 1; tick <= 20; tick++)
            {
                PlayerInput input = PlayerInput.Create(tick, 0.3f, 1f, tick * 0.05f,
                    tick == 5 ? InputButtons.Attack : tick == 15 ? InputButtons.Dodge : InputButtons.None);
                client.Predict(input);
                PlayerSim.Step(ref server, input);
            }
            // Server has processed up to tick 12 when its snapshot arrives; the client replays 13-20.
            PlayerState serverAt12 = start;
            for (uint tick = 1; tick <= 12; tick++)
            {
                PlayerSim.Step(ref serverAt12, client_InputAt(tick));
            }
            client.Reconcile(12, serverAt12);
            Assert.Equal(0f, client.LastCorrection, 4);
            Assert.Equal(server.Position, client.State.Position);
            Assert.Equal(8, client.PendingInputs.Count);
        }

        [Fact]
        public void Prediction_SnapsToServerWhenTheServerDisagrees()
        {
            var client = new ClientPrediction(PlayerState.Spawn(Vector3.Zero, 0f));
            for (uint tick = 1; tick <= 10; tick++)
            {
                client.Predict(PlayerInput.Create(tick, 0, 1, 0, InputButtons.None));
            }
            // The server says we were hit and staggered at tick 6.
            PlayerState serverAt6 = PlayerState.Spawn(Vector3.Zero, 0f);
            for (uint tick = 1; tick <= 6; tick++)
            {
                PlayerSim.Step(ref serverAt6, PlayerInput.Create(tick, 0, 1, 0, InputButtons.None));
            }
            PlayerSim.ApplyHit(ref serverAt6, 25);
            client.Reconcile(6, serverAt6);
            Assert.Equal(CombatTuning.PLAYER_MAX_HEALTH - 25, client.State.Health);
            Assert.True(client.LastCorrection > 0.1f);
        }

        [Fact]
        public void Interpolator_BlendsBetweenBracketingSnapshots()
        {
            var interp = new SnapshotInterpolator();
            interp.Add(1.0, new EntitySnapshot { Id = 1, X = 0, Z = 0, Action = 1 });
            interp.Add(1.1, new EntitySnapshot { Id = 1, X = 10, Z = 2, Action = 2 });
            EntitySnapshot mid = interp.Sample(1.05, out _);
            Assert.Equal(5f, mid.X, 3);
            Assert.Equal(1f, mid.Z, 3);
            Assert.Equal(1, mid.Action); // discrete state never shown early
            EntitySnapshot late = interp.Sample(2.0, out _);
            Assert.Equal(10f, late.X, 3);
            EntitySnapshot early = interp.Sample(0.5, out _);
            Assert.Equal(0f, early.X, 3);
        }

        private static PlayerInput client_InputAt(uint tick) => PlayerInput.Create(tick, 0.3f, 1f, tick * 0.05f,
            tick == 5 ? InputButtons.Attack : tick == 15 ? InputButtons.Dodge : InputButtons.None);
    }
}

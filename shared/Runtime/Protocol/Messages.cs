using System.Collections.Generic;
using System.Numerics;
using Underthrone.Shared.Sim;

namespace Underthrone.Shared.Protocol
{
    public enum MessageType : byte
    {
        Hello = 1,        // C->S reliable
        Welcome = 2,      // S->C reliable
        Inputs = 3,       // C->S unreliable, carries recent inputs redundantly to survive loss
        Snapshot = 4,     // S->C unreliable sequenced
        CombatEvent = 5,  // S->C reliable
        PlayerLeft = 6,   // S->C reliable
    }

    public enum EntityKind : byte
    {
        Player = 1,
        Ghoul = 2,
    }

    /// Everything a client needs to draw another entity (it never simulates them).
    public struct EntitySnapshot
    {
        public ushort Id;
        public EntityKind Kind;
        public float X;
        public float Z;
        public ushort YawQ;
        public byte Action;     // PlayerAction or EnemyAction depending on Kind
        public ushort Health;
    }

    public static class Messages
    {
        public const ushort PROTOCOL_VERSION = 1;
        public const string CONNECTION_KEY = "underthrone-dev";
        public const int MAX_INPUTS_PER_PACKET = 8;

        // --- Hello / Welcome ---

        public static void WriteHello(PacketWriter w, string playerName)
        {
            w.WriteByte((byte)MessageType.Hello);
            w.WriteUShort(PROTOCOL_VERSION);
            w.WriteString(playerName);
        }

        public static void ReadHello(ref PacketReader r, out ushort protocolVersion, out string playerName)
        {
            protocolVersion = r.ReadUShort();
            playerName = r.ReadString();
        }

        public static void WriteWelcome(PacketWriter w, ushort playerId, uint serverTick)
        {
            w.WriteByte((byte)MessageType.Welcome);
            w.WriteUShort(playerId);
            w.WriteUInt(serverTick);
        }

        public static void ReadWelcome(ref PacketReader r, out ushort playerId, out uint serverTick)
        {
            playerId = r.ReadUShort();
            serverTick = r.ReadUInt();
        }

        // --- Inputs ---

        /// Writes up to MAX_INPUTS_PER_PACKET inputs, oldest first.
        public static void WriteInputs(PacketWriter w, IReadOnlyList<PlayerInput> inputs, int start, int count)
        {
            if (count > MAX_INPUTS_PER_PACKET)
            {
                start += count - MAX_INPUTS_PER_PACKET;
                count = MAX_INPUTS_PER_PACKET;
            }
            w.WriteByte((byte)MessageType.Inputs);
            w.WriteByte((byte)count);
            for (int inputIndex = start; inputIndex < start + count; inputIndex++)
            {
                PlayerInput input = inputs[inputIndex];
                w.WriteUInt(input.Tick);
                w.WriteSByte(input.MoveX);
                w.WriteSByte(input.MoveY);
                w.WriteUShort(input.YawQ);
                w.WriteByte((byte)input.Buttons);
            }
        }

        /// Reads into <paramref name="into"/> (length >= MAX_INPUTS_PER_PACKET); returns the count.
        public static int ReadInputs(ref PacketReader r, PlayerInput[] into)
        {
            int count = r.ReadByte();
            if (count > MAX_INPUTS_PER_PACKET || count > into.Length)
            {
                throw new ProtocolException("Too many inputs in one packet");
            }
            for (int inputIndex = 0; inputIndex < count; inputIndex++)
            {
                uint tick = r.ReadUInt();
                sbyte moveX = r.ReadSByte();
                sbyte moveY = r.ReadSByte();
                ushort yaw = r.ReadUShort();
                var buttons = (InputButtons)(r.ReadByte() & 0x07);
                into[inputIndex] = new PlayerInput(tick, moveX, moveY, yaw, buttons);
            }
            return count;
        }

        // --- Snapshot ---

        /// The recipient's own full body state (for reconciliation) plus every visible entity.
        public static void WriteSnapshot(PacketWriter w, uint serverTick, uint ackInputTick, in PlayerState you,
            IReadOnlyList<EntitySnapshot> entities)
        {
            w.WriteByte((byte)MessageType.Snapshot);
            w.WriteUInt(serverTick);
            w.WriteUInt(ackInputTick);
            WritePlayerState(w, you);
            w.WriteUShort((ushort)entities.Count);
            for (int entityIndex = 0; entityIndex < entities.Count; entityIndex++)
            {
                EntitySnapshot e = entities[entityIndex];
                w.WriteUShort(e.Id);
                w.WriteByte((byte)e.Kind);
                w.WriteFloat(e.X);
                w.WriteFloat(e.Z);
                w.WriteUShort(e.YawQ);
                w.WriteByte(e.Action);
                w.WriteUShort(e.Health);
            }
        }

        public static void ReadSnapshot(ref PacketReader r, out uint serverTick, out uint ackInputTick,
            out PlayerState you, List<EntitySnapshot> entitiesOut)
        {
            serverTick = r.ReadUInt();
            ackInputTick = r.ReadUInt();
            you = ReadPlayerState(ref r);
            int count = r.ReadUShort();
            entitiesOut.Clear();
            for (int entityIndex = 0; entityIndex < count; entityIndex++)
            {
                entitiesOut.Add(new EntitySnapshot
                {
                    Id = r.ReadUShort(),
                    Kind = (EntityKind)r.ReadByte(),
                    X = r.ReadFloat(),
                    Z = r.ReadFloat(),
                    YawQ = r.ReadUShort(),
                    Action = r.ReadByte(),
                    Health = r.ReadUShort(),
                });
            }
        }

        // --- Combat events ---

        public static void WriteCombatEvent(PacketWriter w, uint tick, ushort attackerId, ushort targetId,
            HitOutcome outcome, ushort damage)
        {
            w.WriteByte((byte)MessageType.CombatEvent);
            w.WriteUInt(tick);
            w.WriteUShort(attackerId);
            w.WriteUShort(targetId);
            w.WriteByte((byte)outcome);
            w.WriteUShort(damage);
        }

        public static void ReadCombatEvent(ref PacketReader r, out uint tick, out ushort attackerId,
            out ushort targetId, out HitOutcome outcome, out ushort damage)
        {
            tick = r.ReadUInt();
            attackerId = r.ReadUShort();
            targetId = r.ReadUShort();
            outcome = (HitOutcome)r.ReadByte();
            damage = r.ReadUShort();
        }

        public static void WritePlayerLeft(PacketWriter w, ushort playerId)
        {
            w.WriteByte((byte)MessageType.PlayerLeft);
            w.WriteUShort(playerId);
        }

        // --- PlayerState (full precision, so client replay matches the server exactly) ---

        public static void WritePlayerState(PacketWriter w, in PlayerState s)
        {
            w.WriteFloat(s.Position.X);
            w.WriteFloat(s.Position.Y);
            w.WriteFloat(s.Position.Z);
            w.WriteFloat(s.Yaw);
            w.WriteInt(s.Health);
            w.WriteFloat(s.Stamina);
            w.WriteByte((byte)s.Action);
            w.WriteInt(s.ActionTicks);
            w.WriteInt(s.IFrameTicks);
            w.WriteInt(s.ParryTicks);
            w.WriteInt(s.RegenDelayTicks);
            w.WriteFloat(s.DodgeDirection.X);
            w.WriteFloat(s.DodgeDirection.Z);
            w.WriteUShort(s.SwingId);
            w.WriteByte((byte)s.PrevButtons);
        }

        public static PlayerState ReadPlayerState(ref PacketReader r)
        {
            var s = new PlayerState();
            s.Position = new Vector3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat());
            s.Yaw = r.ReadFloat();
            s.Health = r.ReadInt();
            s.Stamina = r.ReadFloat();
            s.Action = (PlayerAction)r.ReadByte();
            s.ActionTicks = r.ReadInt();
            s.IFrameTicks = r.ReadInt();
            s.ParryTicks = r.ReadInt();
            s.RegenDelayTicks = r.ReadInt();
            float dodgeX = r.ReadFloat();
            float dodgeZ = r.ReadFloat();
            s.DodgeDirection = new Vector3(dodgeX, 0f, dodgeZ);
            s.SwingId = r.ReadUShort();
            s.PrevButtons = (InputButtons)r.ReadByte();
            return s;
        }
    }
}

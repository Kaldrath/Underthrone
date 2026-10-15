using System;
using System.Buffers.Binary;
using System.Text;

namespace Underthrone.Shared.Protocol
{
    /// Little-endian binary reader. Throws <see cref="ProtocolException"/> on truncated data so
    /// a malformed packet can be rejected without crashing the server.
    public struct PacketReader
    {
        private readonly byte[] _buffer;
        private readonly int _end;
        private int _position;

        public PacketReader(byte[] buffer, int offset, int length)
        {
            _buffer = buffer;
            _position = offset;
            _end = offset + length;
        }

        public int Remaining => _end - _position;

        public byte ReadByte()
        {
            Require(1);
            return _buffer[_position++];
        }

        public sbyte ReadSByte() => (sbyte)ReadByte();

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUShort()
        {
            Require(2);
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(_position));
            _position += 2;
            return value;
        }

        public uint ReadUInt()
        {
            Require(4);
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_buffer.AsSpan(_position));
            _position += 4;
            return value;
        }

        public int ReadInt()
        {
            Require(4);
            int value = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(_position));
            _position += 4;
            return value;
        }

        public float ReadFloat() => BitConverter.Int32BitsToSingle(ReadInt());

        public string ReadString()
        {
            int count = ReadByte();
            Require(count);
            string value = Encoding.UTF8.GetString(_buffer, _position, count);
            _position += count;
            return value;
        }

        private void Require(int count)
        {
            if (_position + count > _end)
            {
                throw new ProtocolException("Packet truncated");
            }
        }
    }

    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }
}

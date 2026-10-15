using System;
using System.Buffers.Binary;
using System.Text;

namespace Underthrone.Shared.Protocol
{
    /// Little-endian binary writer over a reusable, growable buffer. Call Reset() and reuse one
    /// instance per sender so steady-state sending does not allocate.
    public sealed class PacketWriter
    {
        private byte[] _buffer;
        private int _length;

        public PacketWriter(int capacity = 256)
        {
            _buffer = new byte[capacity];
        }

        public byte[] Buffer => _buffer;
        public int Length => _length;

        public void Reset() => _length = 0;

        public byte[] ToArray()
        {
            var copy = new byte[_length];
            Array.Copy(_buffer, copy, _length);
            return copy;
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void WriteSByte(sbyte value) => WriteByte((byte)value);

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUShort(ushort value)
        {
            Ensure(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length), value);
            _length += 2;
        }

        public void WriteUInt(uint value)
        {
            Ensure(4);
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_length), value);
            _length += 4;
        }

        public void WriteInt(int value)
        {
            Ensure(4);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_length), value);
            _length += 4;
        }

        public void WriteFloat(float value) => WriteInt(BitConverter.SingleToInt32Bits(value));

        public void WriteString(string value)
        {
            value ??= string.Empty;
            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount > byte.MaxValue)
            {
                throw new ArgumentException("String too long for a u8 length prefix", nameof(value));
            }
            WriteByte((byte)byteCount);
            Ensure(byteCount);
            Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _length);
            _length += byteCount;
        }

        private void Ensure(int extra)
        {
            if (_length + extra <= _buffer.Length)
            {
                return;
            }
            int size = _buffer.Length * 2;
            while (size < _length + extra)
            {
                size *= 2;
            }
            Array.Resize(ref _buffer, size);
        }
    }
}

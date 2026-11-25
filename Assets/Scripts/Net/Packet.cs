using System;
using System.Text;

namespace BOW.Net
{
    /// <summary>Little-endian binary writer for packets (TDD 8.6). Reused per send to avoid garbage.</summary>
    public sealed class PacketWriter
    {
        private byte[] _buffer;

        public int Length { get; private set; }
        public byte[] Buffer => _buffer;

        public PacketWriter(int capacity = 512) { _buffer = new byte[capacity]; }

        public PacketWriter Reset() { Length = 0; return this; }

        /// <summary>Starts a packet: protocol version then message type.</summary>
        public PacketWriter Begin(MessageType type)
        {
            Reset();
            U16(NetProtocol.Version);
            U8((byte)type);
            return this;
        }

        public byte[] ToArray()
        {
            var copy = new byte[Length];
            Array.Copy(_buffer, copy, Length);
            return copy;
        }

        private void Ensure(int extra)
        {
            if (Length + extra <= _buffer.Length) return;
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + extra));
        }

        public void U8(byte v) { Ensure(1); _buffer[Length++] = v; }
        public void Bool(bool v) => U8(v ? (byte)1 : (byte)0);
        public void I8(sbyte v) => U8((byte)v);

        public void U16(ushort v)
        {
            Ensure(2);
            _buffer[Length++] = (byte)v;
            _buffer[Length++] = (byte)(v >> 8);
        }

        public void U32(uint v)
        {
            Ensure(4);
            _buffer[Length++] = (byte)v;
            _buffer[Length++] = (byte)(v >> 8);
            _buffer[Length++] = (byte)(v >> 16);
            _buffer[Length++] = (byte)(v >> 24);
        }

        public void I32(int v) => U32((uint)v);

        public void F32(float v)
        {
            // Bit-exact and endian-independent: BitConverter gives native order, so write via the int bits.
            U32((uint)BitConverter.SingleToInt32Bits(v));
        }

        public void Str(string s, int maxBytes = 64)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(bytes.Length, maxBytes);
            U8((byte)n);
            Ensure(n);
            Array.Copy(bytes, 0, _buffer, Length, n);
            Length += n;
        }
    }

    /// <summary>Reads packets written by <see cref="PacketWriter"/>. Throws <see cref="PacketException"/> on short or bad data.</summary>
    public sealed class PacketReader
    {
        private byte[] _data;
        private int _end;

        public int Position { get; private set; }
        public int Remaining => _end - Position;

        public PacketReader() { }
        public PacketReader(byte[] data, int length) { Set(data, length); }

        public PacketReader Set(byte[] data, int length)
        {
            _data = data;
            _end = Math.Min(length, data.Length);
            Position = 0;
            return this;
        }

        /// <summary>Reads the header. Returns false (and leaves the type as None) for a different protocol version.</summary>
        public bool Header(out ushort version, out MessageType type)
        {
            version = U16();
            type = (MessageType)U8();
            return version == NetProtocol.Version;
        }

        private void Need(int n)
        {
            if (Position + n > _end) throw new PacketException("Packet too short.");
        }

        public byte U8() { Need(1); return _data[Position++]; }
        public bool Bool() => U8() != 0;
        public sbyte I8() => (sbyte)U8();

        public ushort U16()
        {
            Need(2);
            int v = _data[Position] | (_data[Position + 1] << 8);
            Position += 2;
            return (ushort)v;
        }

        public uint U32()
        {
            Need(4);
            uint v = (uint)(_data[Position] | (_data[Position + 1] << 8) | (_data[Position + 2] << 16) | (_data[Position + 3] << 24));
            Position += 4;
            return v;
        }

        public int I32() => (int)U32();

        public float F32() => BitConverter.Int32BitsToSingle((int)U32());

        /// <summary>A float that must be finite (guards the host against bad guest data, TDD 12.3).</summary>
        public float FiniteF32()
        {
            float v = F32();
            if (float.IsNaN(v) || float.IsInfinity(v)) throw new PacketException("Non-finite value.");
            return v;
        }

        public string Str()
        {
            int n = U8();
            Need(n);
            string s = Encoding.UTF8.GetString(_data, Position, n);
            Position += n;
            return s;
        }
    }

    public sealed class PacketException : Exception
    {
        public PacketException(string message) : base(message) { }
    }
}

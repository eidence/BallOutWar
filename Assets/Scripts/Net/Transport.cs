using System;

namespace BOW.Net
{
    public enum Channel : byte
    {
        /// <summary>Delivered once, in order (lobby, start, events).</summary>
        Reliable,
        /// <summary>May be lost or reordered (inputs, snapshots).</summary>
        Unreliable,
    }

    public enum TransportEventType : byte
    {
        Connected,
        Data,
        Disconnected,
    }

    public struct TransportEvent
    {
        public TransportEventType Type;
        /// <summary>The peer: on a host each guest has its own id; on a guest the host is always <see cref="NetProtocol.HostPeer"/>.</summary>
        public int Peer;
        public byte[] Data;
        public int Length;
    }

    /// <summary>
    /// Moves bytes between devices (TDD 8.2). LAN uses Unity Transport, online play will use EOS P2P,
    /// and tests use <see cref="LoopbackNetwork"/>; the protocol above is the same for all of them.
    /// </summary>
    public interface ITransport : IDisposable
    {
        bool IsHost { get; }

        /// <summary>Pumps the network. Call once per frame before <see cref="Poll"/>.</summary>
        void Update();

        /// <summary>Returns the next event, if any. The event's data is only valid until the next call.</summary>
        bool Poll(out TransportEvent e);

        void Send(int peer, Channel channel, byte[] data, int length);

        /// <summary>Closes one connection (host: a guest; guest: the host).</summary>
        void Disconnect(int peer);
    }

    public static class TransportExtensions
    {
        public static void Send(this ITransport transport, int peer, Channel channel, PacketWriter packet)
            => transport.Send(peer, channel, packet.Buffer, packet.Length);
    }
}

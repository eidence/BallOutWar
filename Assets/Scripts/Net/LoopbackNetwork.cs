using System;
using System.Collections.Generic;

namespace BOW.Net
{
    /// <summary>
    /// An in-memory network for tests and offline checks: one host and any number of guests, with optional
    /// latency, jitter and loss on the unreliable channel (TDD 8.7 test conditions). Time only moves when
    /// <see cref="Advance"/> is called, so tests are deterministic.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        private readonly List<Packet> _inFlight = new List<Packet>();
        private readonly Random _random;
        private LoopbackTransport _host;
        private readonly List<LoopbackTransport> _guests = new List<LoopbackTransport>();

        /// <summary>One-way delay in seconds.</summary>
        public float Latency { get; set; }
        /// <summary>Random extra delay (0..Jitter seconds) on unreliable packets.</summary>
        public float Jitter { get; set; }
        /// <summary>Chance (0..1) an unreliable packet is lost.</summary>
        public float Loss { get; set; }
        public double Time { get; private set; }

        public LoopbackNetwork(int seed = 1) { _random = new Random(seed); }

        public LoopbackTransport CreateHost()
        {
            if (_host != null) throw new InvalidOperationException("The loopback network already has a host.");
            _host = new LoopbackTransport(this, isHost: true, id: 0);
            return _host;
        }

        /// <summary>Creates a guest and connects it to the host after the network latency.</summary>
        public LoopbackTransport CreateGuest()
        {
            if (_host == null) throw new InvalidOperationException("Create the host first.");
            var guest = new LoopbackTransport(this, isHost: false, id: _guests.Count + 1);
            _guests.Add(guest);
            Schedule(guest, _host, Channel.Reliable, null, 0, TransportEventType.Connected);
            Schedule(_host, guest, Channel.Reliable, null, 0, TransportEventType.Connected);
            return guest;
        }

        public void Advance(double seconds)
        {
            Time += seconds;
            for (int i = 0; i < _inFlight.Count; i++)
            {
                var p = _inFlight[i];
                if (p.DeliverAt > Time) continue;
                p.To.Enqueue(p.Event);
                _inFlight.RemoveAt(i--);
            }
        }

        internal void Send(LoopbackTransport from, int peer, Channel channel, byte[] data, int length)
        {
            LoopbackTransport to = from.IsHost ? FindGuest(peer) : _host;
            if (to == null || from.IsClosed || to.IsClosed || !from.IsLinked(to)) return;
            if (channel == Channel.Unreliable && Loss > 0f && _random.NextDouble() < Loss) return;
            var copy = new byte[length];
            Array.Copy(data, copy, length);
            Schedule(to, from, channel, copy, length, TransportEventType.Data);
        }

        internal void Disconnect(LoopbackTransport from, int peer)
        {
            LoopbackTransport to = from.IsHost ? FindGuest(peer) : _host;
            if (to == null) return;
            from.Unlink(to);
            to.Unlink(from);
            Schedule(to, from, Channel.Reliable, null, 0, TransportEventType.Disconnected);
        }

        /// <summary>Drops a guest without a goodbye, as if its Wi-Fi went away. The host sees a disconnect.</summary>
        public void Cut(LoopbackTransport guest)
        {
            guest.Unlink(_host);
            _host.Unlink(guest);
            Schedule(_host, guest, Channel.Reliable, null, 0, TransportEventType.Disconnected);
            Schedule(guest, _host, Channel.Reliable, null, 0, TransportEventType.Disconnected);
        }

        private LoopbackTransport FindGuest(int id)
        {
            foreach (var g in _guests) if (g.Id == id) return g;
            return null;
        }

        private void Schedule(LoopbackTransport to, LoopbackTransport from, Channel channel, byte[] data, int length, TransportEventType type)
        {
            double delay = Latency;
            if (channel == Channel.Unreliable && Jitter > 0f) delay += _random.NextDouble() * Jitter;
            // Reliable packets keep their order: never deliver before the last reliable packet to the same peer.
            double at = Time + delay;
            if (channel == Channel.Reliable)
            {
                for (int i = _inFlight.Count - 1; i >= 0; i--)
                {
                    if (_inFlight[i].To == to && _inFlight[i].Reliable) { at = Math.Max(at, _inFlight[i].DeliverAt); break; }
                }
            }
            if (type == TransportEventType.Connected) to.Link(from);
            _inFlight.Add(new Packet
            {
                To = to,
                Reliable = channel == Channel.Reliable,
                DeliverAt = at,
                Event = new TransportEvent { Type = type, Peer = to.IsHost ? from.Id : NetProtocol.HostPeer, Data = data, Length = length },
            });
        }

        private struct Packet
        {
            public LoopbackTransport To;
            public bool Reliable;
            public double DeliverAt;
            public TransportEvent Event;
        }
    }

    public sealed class LoopbackTransport : ITransport
    {
        private readonly LoopbackNetwork _network;
        private readonly Queue<TransportEvent> _events = new Queue<TransportEvent>();
        private readonly HashSet<LoopbackTransport> _links = new HashSet<LoopbackTransport>();

        public bool IsHost { get; }
        internal int Id { get; }
        internal bool IsClosed { get; private set; }

        internal LoopbackTransport(LoopbackNetwork network, bool isHost, int id)
        {
            _network = network;
            IsHost = isHost;
            Id = id;
        }

        internal void Enqueue(TransportEvent e) { if (!IsClosed) _events.Enqueue(e); }
        internal void Link(LoopbackTransport other) => _links.Add(other);
        internal void Unlink(LoopbackTransport other) => _links.Remove(other);
        internal bool IsLinked(LoopbackTransport other) => _links.Contains(other);

        public void Update() { }

        public bool Poll(out TransportEvent e)
        {
            if (_events.Count > 0) { e = _events.Dequeue(); return true; }
            e = default;
            return false;
        }

        public void Send(int peer, Channel channel, byte[] data, int length) => _network.Send(this, peer, channel, data, length);

        public void Disconnect(int peer) => _network.Disconnect(this, peer);

        public void Dispose()
        {
            IsClosed = true;
            _events.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using BOW.Net;
using Unity.Networking.Transport;

namespace BOW.Game
{
    /// <summary>
    /// <see cref="ITransport"/> over Unity Transport UDP (TDD 8.2): a reliable pipeline with fragmentation for
    /// lobby and events, and a plain unreliable one for inputs and snapshots. 5 s heartbeat timeout (TDD 8.9).
    /// </summary>
    public sealed class LanTransport : ITransport
    {
        private const int DisconnectTimeoutMs = 5000;

        private NetworkDriver _driver;
        private readonly NetworkPipeline _reliable;
        private readonly NetworkPipeline _unreliable;
        private readonly Dictionary<NetworkConnection, int> _peerIds = new Dictionary<NetworkConnection, int>();
        private readonly Dictionary<int, NetworkConnection> _connections = new Dictionary<int, NetworkConnection>();
        private readonly Queue<TransportEvent> _events = new Queue<TransportEvent>();
        private NetworkConnection _server;
        private int _nextPeer = 1;
        private byte[] _receiveBuffer = new byte[2048];
        private bool _disposed;

        public bool IsHost { get; }

        private LanTransport(bool host)
        {
            IsHost = host;
            var settings = new NetworkSettings();
            settings.WithNetworkConfigParameters(disconnectTimeoutMS: DisconnectTimeoutMs, heartbeatTimeoutMS: 500, connectTimeoutMS: 1000, maxConnectAttempts: 8);
            _driver = NetworkDriver.Create(settings);
            _reliable = _driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
            _unreliable = NetworkPipeline.Null;
        }

        /// <summary>Starts hosting on the game port. Throws if the port is in use.</summary>
        public static LanTransport Host(ushort port = NetProtocol.GamePort)
        {
            var t = new LanTransport(true);
            if (t._driver.Bind(NetworkEndpoint.AnyIpv4.WithPort(port)) != 0 || t._driver.Listen() != 0)
            {
                t.Dispose();
                throw new InvalidOperationException($"Couldn't open port {port}. Is another game already hosting on this device?");
            }
            return t;
        }

        /// <summary>Connects to a host. Connection success or failure arrives as an event.</summary>
        public static LanTransport Join(string address, ushort port = NetProtocol.GamePort)
        {
            if (!NetworkEndpoint.TryParse(address, port, out var endpoint))
                throw new ArgumentException($"\"{address}\" isn't a valid IP address.");
            var t = new LanTransport(false);
            t._server = t._driver.Connect(endpoint);
            return t;
        }

        public void Update()
        {
            if (_disposed) return;
            _driver.ScheduleUpdate().Complete();

            if (IsHost)
            {
                NetworkConnection incoming;
                while ((incoming = _driver.Accept()) != default)
                {
                    int id = _nextPeer++;
                    _peerIds[incoming] = id;
                    _connections[id] = incoming;
                    _events.Enqueue(new TransportEvent { Type = TransportEventType.Connected, Peer = id });
                }
            }

            NetworkEvent.Type type;
            while ((type = _driver.PopEvent(out var connection, out var reader, out _)) != NetworkEvent.Type.Empty)
            {
                int peer = IsHost ? (_peerIds.TryGetValue(connection, out int id) ? id : -1) : NetProtocol.HostPeer;
                if (peer < 0) continue;
                switch (type)
                {
                    case NetworkEvent.Type.Connect:
                        _events.Enqueue(new TransportEvent { Type = TransportEventType.Connected, Peer = peer });
                        break;
                    case NetworkEvent.Type.Disconnect:
                        if (IsHost) { _peerIds.Remove(connection); _connections.Remove(peer); }
                        else _server = default;
                        _events.Enqueue(new TransportEvent { Type = TransportEventType.Disconnected, Peer = peer });
                        break;
                    case NetworkEvent.Type.Data:
                        int length = reader.Length;
                        var data = new byte[length];
                        reader.ReadBytes(new Span<byte>(data));
                        _events.Enqueue(new TransportEvent { Type = TransportEventType.Data, Peer = peer, Data = data, Length = length });
                        break;
                }
            }
        }

        public bool Poll(out TransportEvent e)
        {
            if (_events.Count > 0) { e = _events.Dequeue(); return true; }
            e = default;
            return false;
        }

        public void Send(int peer, Channel channel, byte[] data, int length)
        {
            if (_disposed) return;
            NetworkConnection connection;
            if (IsHost) { if (!_connections.TryGetValue(peer, out connection)) return; }
            else connection = _server;
            if (connection == default) return;

            var pipe = channel == Channel.Reliable ? _reliable : _unreliable;
            if (_driver.BeginSend(pipe, connection, out var writer) != 0) return;
            writer.WriteBytes(new Span<byte>(data, 0, length));
            _driver.EndSend(writer);
        }

        public void Disconnect(int peer)
        {
            if (_disposed) return;
            if (IsHost)
            {
                if (!_connections.TryGetValue(peer, out var c)) return;
                _driver.Disconnect(c);
                _connections.Remove(peer);
                _peerIds.Remove(c);
            }
            else if (_server != default)
            {
                _driver.Disconnect(_server);
                _server = default;
            }
            // Push the disconnect out before the driver is possibly disposed.
            _driver.ScheduleUpdate().Complete();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_driver.IsCreated) _driver.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BOW.Net;
using UnityEngine;

namespace BOW.Game
{
    /// <summary>A match seen on the local network (TDD 8.3).</summary>
    public sealed class FoundMatch
    {
        public string Address;
        public LanBeacon Beacon;
        public float LastSeen;
    }

    /// <summary>
    /// LAN discovery (TDD 8.3, US-5.1, US-5.2): a host broadcasts a beacon every second on UDP 47777;
    /// the Join screen listens and lists matches, dropping any not heard from for 3 s. Works on any shared
    /// subnet, including a phone hotspot, with no internet.
    /// </summary>
    public sealed class LanDiscovery : IDisposable
    {
        private const float BeaconInterval = 1f;
        private const float ExpirySeconds = 3f;

        private UdpClient _socket;
        private readonly List<FoundMatch> _found = new List<FoundMatch>();
        private float _nextBeacon;
        private AndroidMulticastLock _lock;

        public IReadOnlyList<FoundMatch> Found => _found;

        /// <summary>Starts listening for hosts. Returns false if the discovery port can't be opened.</summary>
        public bool StartListening()
        {
            Stop();
            try
            {
                _socket = new UdpClient();
                _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _socket.Client.Bind(new IPEndPoint(IPAddress.Any, NetProtocol.DiscoveryPort));
                _socket.EnableBroadcast = true;
                _lock = AndroidMulticastLock.Acquire();
                return true;
            }
            catch (SocketException e)
            {
                Debug.LogWarning($"LAN discovery unavailable: {e.Message}");
                Stop();
                return false;
            }
        }

        /// <summary>Starts broadcasting this device's match.</summary>
        public void StartBroadcasting()
        {
            Stop();
            try
            {
                _socket = new UdpClient { EnableBroadcast = true };
                _nextBeacon = 0f;
            }
            catch (SocketException e)
            {
                Debug.LogWarning($"LAN broadcast unavailable: {e.Message}");
                Stop();
            }
        }

        /// <summary>Call every frame. A host passes its beacon; a listener passes null.</summary>
        public void Update(LanBeacon? beacon)
        {
            if (_socket == null) return;
            float now = Time.unscaledTime;

            if (beacon.HasValue)
            {
                if (now < _nextBeacon) return;
                _nextBeacon = now + BeaconInterval;
                byte[] data = beacon.Value.Encode();
                foreach (var target in BroadcastAddresses())
                {
                    try { _socket.Send(data, data.Length, new IPEndPoint(target, NetProtocol.DiscoveryPort)); }
                    catch (SocketException) { /* this interface can't broadcast; others may */ }
                }
                return;
            }

            try
            {
                while (_socket.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _socket.Receive(ref from);
                    if (!LanBeacon.TryDecode(data, data.Length, out var b)) continue;
                    string address = from.Address.ToString();
                    var match = _found.Find(m => m.Address == address && m.Beacon.GamePort == b.GamePort);
                    if (match == null) _found.Add(match = new FoundMatch { Address = address });
                    match.Beacon = b;
                    match.LastSeen = now;
                }
            }
            catch (SocketException) { }
            _found.RemoveAll(m => now - m.LastSeen > ExpirySeconds);
        }

        public void Stop()
        {
            _socket?.Close();
            _socket = null;
            _found.Clear();
            _lock?.Release();
            _lock = null;
        }

        public void Dispose() => Stop();

        /// <summary>The limited broadcast address plus each interface's subnet broadcast (some routers drop 255.255.255.255).</summary>
        private static IEnumerable<IPAddress> BroadcastAddresses()
        {
            yield return IPAddress.Broadcast;
            foreach (var info in LocalIPv4Interfaces())
            {
                byte[] ip = info.Address.GetAddressBytes(), mask = info.IPv4Mask.GetAddressBytes();
                var b = new byte[4];
                for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                yield return new IPAddress(b);
            }
        }

        private static IEnumerable<UnicastIPAddressInformation> LocalIPv4Interfaces()
        {
            NetworkInterface[] interfaces;
            try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); }
            catch (Exception) { yield break; }
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && ua.IPv4Mask != null) yield return ua;
            }
        }

        /// <summary>This device's LAN address, for showing to players who want to join by IP.</summary>
        public static string LocalAddress()
        {
            foreach (var ua in LocalIPv4Interfaces()) return ua.Address.ToString();
            return "unknown";
        }
    }

    /// <summary>
    /// Android drops broadcast packets unless an app holds a Wi-Fi multicast lock (TDD 8.3), so the Join screen
    /// takes one while it is open. Needs CHANGE_WIFI_MULTICAST_STATE in the manifest. Does nothing elsewhere.
    /// </summary>
    public sealed class AndroidMulticastLock
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _lock;
#endif

        public static AndroidMulticastLock Acquire()
        {
            var result = new AndroidMulticastLock();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi"))
                {
                    result._lock = wifi.Call<AndroidJavaObject>("createMulticastLock", "BallOutWarDiscovery");
                    result._lock.Call("setReferenceCounted", false);
                    result._lock.Call("acquire");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Couldn't take the multicast lock; LAN games may not appear: {e.Message}");
            }
#endif
            return result;
        }

        public void Release()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { _lock?.Call("release"); } catch (Exception) { }
            _lock?.Dispose();
            _lock = null;
#endif
        }
    }
}

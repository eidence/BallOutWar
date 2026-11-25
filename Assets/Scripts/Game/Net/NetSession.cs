using System;
using BOW.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BOW.Game
{
    /// <summary>
    /// Holds the current LAN session (host or guest) across scene loads, pumps it every frame, and moves
    /// between the Menu and Match scenes when the host starts a match or returns everyone to the lobby.
    /// </summary>
    public static class NetSession
    {
        private static Runner _runner;

        public static NetHost Host { get; private set; }
        public static NetGuest Guest { get; private set; }
        public static bool Active => Host != null || Guest != null;
        public static ushort Port { get; private set; } = NetProtocol.GamePort;

        /// <summary>Why the last session ended, to show on the menu ("The host left the match."). Cleared once shown.</summary>
        public static string EndReason { get; set; }

        /// <summary>The host's notices ("Sam joined"), newest last; the lobby screen shows them.</summary>
        public static event Action<string> Notice;

        public static void StartHosting(string playerName, int character)
        {
            End();
            var transport = LanTransport.Host(Port);
            Host = new NetHost(transport, $"{playerName}'s game", playerName, character);
            Host.MatchStarted += OnMatchStarted;
            Host.Notice += n => Notice?.Invoke(n);
            EnsureRunner();
            _runner.Discovery.StartBroadcasting();
        }

        public static void Join(string address, string playerName, int character, ushort port = NetProtocol.GamePort)
        {
            End();
            Guest = new NetGuest(LanTransport.Join(address, port), playerName, character);
            Guest.MatchStarted += OnMatchStarted;
            Guest.ReturnedToLobby += () => LoadScene(BootLoader.MenuScene);
            Guest.Ended += reason =>
            {
                EndReason = reason;
                End();
                if (SceneManager.GetActiveScene().name != BootLoader.MenuScene) LoadScene(BootLoader.MenuScene);
            };
            EnsureRunner();
        }

        /// <summary>Host only: sends everyone back to the lobby after a match.</summary>
        public static void ReturnToLobby()
        {
            if (Host == null) return;
            Host.ReturnToLobby();
            LoadScene(BootLoader.MenuScene);
        }

        /// <summary>Leaves or closes the session. Guests' seats go to an AI; a closing host ends the match for everyone.</summary>
        public static void End()
        {
            if (Host != null) { Host.Dispose(); Host = null; }
            if (Guest != null) { var g = Guest; Guest = null; g.Dispose(); }
            if (_runner != null) _runner.Discovery.Stop();
        }

        private static void OnMatchStarted()
        {
            if (SceneManager.GetActiveScene().name != BootLoader.MatchScene) LoadScene(BootLoader.MatchScene);
        }

        private static void LoadScene(string scene)
        {
            if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadScene(scene);
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;
            var go = new GameObject("Net Session");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<Runner>();
        }

        /// <summary>Pumps the network every frame, before scene scripts (execution order), and cleans up on quit.</summary>
        [DefaultExecutionOrder(-100)]
        private sealed class Runner : MonoBehaviour
        {
            public readonly LanDiscovery Discovery = new LanDiscovery();

            private void Update()
            {
                Host?.Update();
                Guest?.Update();
                if (Host != null) Discovery.Update(Host.Beacon(Port));
            }

            private void OnApplicationQuit() => End();

            private void OnDestroy() => Discovery.Dispose();
        }
    }
}

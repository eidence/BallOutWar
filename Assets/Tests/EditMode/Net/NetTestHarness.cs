using System;
using System.Collections.Generic;
using BOW.AI;
using BOW.Net;

namespace BOW.Simulation.Tests.Net
{
    /// <summary>A host and guests on a <see cref="LoopbackNetwork"/>, pumped at 60 Hz like the game loop.</summary>
    internal sealed class NetTestHarness
    {
        public const float Dt = 1f / 60f;

        public readonly LoopbackNetwork Network;
        public readonly NetHost Host;
        public readonly List<NetGuest> Guests = new List<NetGuest>();
        public readonly List<LoopbackTransport> GuestTransports = new List<LoopbackTransport>();
        public readonly List<SimEvent> HostEvents = new List<SimEvent>();
        public readonly List<List<SimEvent>> GuestEvents = new List<List<SimEvent>>();
        public readonly List<string> Notices = new List<string>();

        /// <summary>Input for the host's own seat each tick (default: an AI plays it).</summary>
        public Func<MatchSimulation, PlayerInput> HostInput;
        /// <summary>Input for a guest each tick (default: an AI plays it from what the guest sees).</summary>
        public Func<int, NetGuest, PlayerInput> GuestInput;

        private AIController _hostAi;
        private readonly Dictionary<NetGuest, AIController> _guestAi = new Dictionary<NetGuest, AIController>();
        private readonly MatchState _prev = new MatchState();
        private readonly MatchState _curr = new MatchState();

        public NetTestHarness(float latency = 0f, float jitter = 0f, float loss = 0f, int seed = 1)
        {
            Network = new LoopbackNetwork(seed) { Latency = latency, Jitter = jitter, Loss = loss };
            Host = new NetHost(Network.CreateHost(), "Test match", "Host", 0, seed);
            Host.Notice += n => Notices.Add(n);
            Host.MatchStarted += () => _hostAi = new AIController(NetHost.LocalSeat, AIDifficulty.Medium, 99);
        }

        public NetGuest AddGuest(string name = "Guest", int character = 1, ushort code = 0)
        {
            var transport = Network.CreateGuest();
            var guest = new NetGuest(transport, name, character, code);
            var events = new List<SimEvent>();
            guest.MatchStarted += () => { _guestAi[guest] = new AIController(guest.LocalSeat, AIDifficulty.Medium, (uint)(17 + guest.LocalSeat)); events.Clear(); };
            Guests.Add(guest);
            GuestTransports.Add(transport);
            GuestEvents.Add(events);
            return guest;
        }

        /// <summary>Runs the network and sessions for the given time.</summary>
        public void Run(float seconds)
        {
            int ticks = (int)Math.Round(seconds / Dt);
            for (int t = 0; t < ticks; t++) Tick();
        }

        /// <summary>Runs until the condition holds; returns false if it didn't within the time limit.</summary>
        public bool RunUntil(Func<bool> done, float maxSeconds)
        {
            int ticks = (int)Math.Round(maxSeconds / Dt);
            for (int t = 0; t < ticks; t++)
            {
                if (done()) return true;
                Tick();
            }
            return done();
        }

        public void Tick()
        {
            Network.Advance(Dt);
            Host.Update();
            if (Host.InMatch)
            {
                var input = HostInput != null ? HostInput(Host.Simulation) : _hostAi.Think(Host.Simulation);
                Host.Step(input, Dt);
                HostEvents.AddRange(Host.Simulation.Events);
            }
            for (int i = 0; i < Guests.Count; i++)
            {
                var g = Guests[i];
                g.Update();
                if (g.Status != GuestStatus.InMatch) continue;
                PlayerInput input = GuestInput != null ? GuestInput(i, g) : _guestAi.TryGetValue(g, out var ai) ? ai.Think(g.Simulation) : default;
                g.Tick(input);
                g.GetDisplay(_prev, _curr, out _);
                GuestEvents[i].AddRange(g.TakeEvents());
            }
        }

        /// <summary>Connects every guest, readies them, and starts the match.</summary>
        public void JoinAllAndStart(int aiSeats = 0)
        {
            Run(0.5f);
            foreach (var g in Guests) g.SetReady(true);
            Run(0.5f);
            for (int seat = 1, added = 0; seat < 4 && added < aiSeats; seat++)
                if (Host.Lobby.Seats[seat].Type == SeatType.Open) { Host.CycleSeat(seat); added++; }
            if (!Host.StartMatch()) throw new InvalidOperationException("Could not start: " + Host.StartBlocker());
            Run(0.5f);
        }
    }
}

using System;
using System.Collections.Generic;
using BOW.AI;
using BOW.Simulation;

namespace BOW.Net
{
    /// <summary>
    /// The host side of a multiplayer session (TDD 8.1, 8.5, 8.8, 8.9). Runs the lobby, then the only real
    /// simulation: it applies guest inputs, runs AI seats, and sends snapshots and events to every guest.
    /// The host is also a player and always sits at seat 0. Engine-free so it can be tested over
    /// <see cref="LoopbackNetwork"/>.
    /// </summary>
    public sealed class NetHost : IDisposable
    {
        public const int LocalSeat = 0;
        private const int MaxQueuedInputs = 4;

        private readonly ITransport _transport;
        private readonly PacketWriter _writer = new PacketWriter(1024);
        private readonly PacketReader _reader = new PacketReader();
        private readonly Dictionary<int, int> _peerSeat = new Dictionary<int, int>();
        private readonly Guest[] _guests = new Guest[SeatFrame.SeatCount];
        private readonly AIController[] _ai = new AIController[SeatFrame.SeatCount];
        private readonly PlayerInput[] _inputs = new PlayerInput[SeatFrame.SeatCount];
        private readonly uint[] _acked = new uint[SeatFrame.SeatCount];
        private readonly PlayerInput[] _readInputs = new PlayerInput[NetProtocol.RedundantInputs];
        private readonly Random _seeds;
        private float _pauseDeadline = -1f;
        private uint _snapshotSequence;
        private int _frames;

        public LobbyState Lobby { get; } = new LobbyState();
        public MatchSimulation Simulation { get; private set; }
        public bool InMatch => Simulation != null;
        public ushort JoinCode { get; set; }

        /// <summary>The lobby changed (someone joined, left, changed character or readied up).</summary>
        public event Action LobbyChanged;
        /// <summary>A match (or rematch) started. Build the match view from <see cref="Simulation"/>.</summary>
        public event Action MatchStarted;
        /// <summary>A short message for the host's screen, such as "Mitzi left: an AI took over".</summary>
        public event Action<string> Notice;

        public NetHost(ITransport transport, string matchName, string hostName, int hostCharacter, int? randomSeed = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (!transport.IsHost) throw new ArgumentException("NetHost needs a host transport.", nameof(transport));
            _seeds = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
            Lobby.MatchName = PlayerNames.Normalise(matchName);
            Lobby.Seats[LocalSeat] = new LobbySeat { Type = SeatType.Human, Name = PlayerNames.Normalise(hostName), Character = (byte)(hostCharacter % LobbyState.CharacterCount), IsHost = true, Ready = true };
            for (int i = 1; i < Lobby.Seats.Length; i++)
                Lobby.Seats[i] = new LobbySeat { Type = SeatType.Open, Character = (byte)(i % LobbyState.CharacterCount), Difficulty = AIDifficulty.Medium };
        }

        public LanBeacon Beacon(ushort gamePort) => new LanBeacon
        {
            Protocol = NetProtocol.Version,
            MatchName = Lobby.MatchName,
            Players = (byte)Lobby.HumanCount,
            MaxPlayers = (byte)(Lobby.HumanCount + Lobby.OpenCount),
            Mode = Lobby.Settings.Mode,
            InGame = InMatch,
            CodeRequired = JoinCode != 0,
            GamePort = gamePort,
        };

        // ------------------------------------------------------------------ lobby controls

        public void SetSettings(LobbySettings settings)
        {
            if (InMatch || settings == null || settings.SameAs(Lobby.Settings)) return;
            Lobby.Settings = settings.Clone();
            ClearGuestReady(); // TDD 8.5: changing settings clears everyone's Ready
            BroadcastLobby();
        }

        public void SetHostCharacter(int character)
        {
            Lobby.Seats[LocalSeat].Character = (byte)(character % LobbyState.CharacterCount);
            BroadcastLobby();
        }

        /// <summary>Cycles a non-host seat Open → AI → Closed → Open. A human in the seat is kicked first.</summary>
        public void CycleSeat(int seat)
        {
            if (InMatch || seat == LocalSeat || seat < 0 || seat >= Lobby.Seats.Length) return;
            ref LobbySeat s = ref Lobby.Seats[seat];
            switch (s.Type)
            {
                case SeatType.Human: Kick(seat); return;
                case SeatType.Open: s.Type = SeatType.AI; s.Name = "AI"; break;
                case SeatType.AI: s.Type = SeatType.Closed; s.Name = ""; break;
                default: s.Type = SeatType.Open; s.Name = ""; break;
            }
            s.Ready = false;
            BroadcastLobby();
        }

        public void SetSeatDifficulty(int seat, AIDifficulty difficulty)
        {
            if (seat < 0 || seat >= Lobby.Seats.Length) return;
            Lobby.Seats[seat].Difficulty = difficulty;
            BroadcastLobby();
        }

        public void SetSeatCharacter(int seat, int character)
        {
            if (seat < 0 || seat >= Lobby.Seats.Length || Lobby.Seats[seat].Type == SeatType.Human && seat != LocalSeat) return;
            Lobby.Seats[seat].Character = (byte)(character % LobbyState.CharacterCount);
            BroadcastLobby();
        }

        public void Kick(int seat)
        {
            int peer = PeerForSeat(seat);
            if (peer < 0) return;
            Send(peer, Channel.Reliable, _writer.Begin(MessageType.Reject).Also(w => w.U8((byte)RejectReason.Kicked)));
            _transport.Disconnect(peer);
            RemoveGuest(peer, "removed");
        }

        // ------------------------------------------------------------------ match control

        public string StartBlocker() => Lobby.StartBlocker();

        /// <summary>Starts the match (or a rematch) for everyone. Open seats are closed.</summary>
        public bool StartMatch()
        {
            if (Lobby.StartBlocker() != null) return false;
            for (int i = 0; i < Lobby.Seats.Length; i++)
                if (Lobby.Seats[i].Type == SeatType.Open) Lobby.Seats[i] = new LobbySeat { Type = SeatType.Closed, Character = Lobby.Seats[i].Character };

            uint seed = (uint)_seeds.Next(1, int.MaxValue);
            var settings = Lobby.ToMatchSettings(seed);
            Simulation = new MatchSimulation(settings);
            Lobby.InGame = true;
            _pauseDeadline = -1f;
            for (int i = 0; i < _ai.Length; i++)
            {
                _ai[i] = Lobby.Seats[i].Type == SeatType.AI ? new AIController(i, Lobby.Seats[i].Difficulty, seed * 7919u + (uint)i) : null;
                _acked[i] = 0;
                if (_guests[i] != null) _guests[i].ResetForMatch();
            }

            _writer.Begin(MessageType.StartMatch);
            Lobby.Write(_writer);
            _writer.U32(seed);
            Broadcast(Channel.Reliable);
            MatchStarted?.Invoke();
            SendSnapshot();
            return true;
        }

        /// <summary>Goes back to the lobby after a match; guests return with Ready cleared.</summary>
        public void ReturnToLobby()
        {
            if (!InMatch) return;
            Simulation = null;
            Lobby.InGame = false;
            for (int i = 0; i < Lobby.Seats.Length; i++)
            {
                // Seats an AI took over from a dropped player open up again.
                if (Lobby.Seats[i].Type == SeatType.AI && Lobby.Seats[i].Name == "AI (dropped)") Lobby.Seats[i] = new LobbySeat { Type = SeatType.Open, Character = Lobby.Seats[i].Character };
            }
            ClearGuestReady();
            Broadcast(Channel.Reliable, _writer.Begin(MessageType.ReturnToLobby));
            BroadcastLobby();
        }

        /// <summary>One simulation tick: gathers inputs, steps, sends events, and every other tick a snapshot.</summary>
        public void Step(PlayerInput localInput, float deltaTime)
        {
            if (!InMatch) return;
            var sim = Simulation;

            for (int i = 0; i < SeatFrame.SeatCount; i++)
            {
                if (i == LocalSeat) _inputs[i] = localInput;
                else if (_ai[i] != null) _inputs[i] = _ai[i].Think(sim);
                else if (_guests[i] != null) _inputs[i] = _guests[i].NextInput(ref _acked[i]);
                else _inputs[i] = default;
            }
            sim.Step(_inputs);
            SendEvents();

            // Guest pauses end on their own after 60 s (TDD 8.8).
            if (sim.State.Phase == MatchPhase.Paused && _pauseDeadline > 0f)
            {
                _pauseDeadline -= deltaTime;
                if (_pauseDeadline <= 0f) Resume();
            }

            // 30 Hz by host frame (not sim tick), so paused and finished matches keep reaching guests.
            if (++_frames % NetProtocol.SnapshotInterval == 0) SendSnapshot();
        }

        /// <summary>"Pause for all" by the host: no budget (TDD 8.8).</summary>
        public void Pause()
        {
            if (!InMatch || !Simulation.Pause()) return;
            _pauseDeadline = -1f;
            SendEvents();
            SendSnapshot();
        }

        public void Resume()
        {
            if (!InMatch || !Simulation.Resume()) return;
            _pauseDeadline = -1f;
            SendEvents();
            SendSnapshot();
        }

        /// <summary>The host is leaving: tells every guest the match is over, then closes all connections (US-6.4).</summary>
        public void Close()
        {
            Broadcast(Channel.Reliable, _writer.Begin(MessageType.MatchEnded));
            foreach (var peer in new List<int>(_peerSeat.Keys)) _transport.Disconnect(peer);
            _peerSeat.Clear();
        }

        public void Dispose()
        {
            Close();
            _transport.Dispose();
        }

        // ------------------------------------------------------------------ network

        /// <summary>Handles everything the network delivered since the last call. Call once per frame.</summary>
        public void Update()
        {
            _transport.Update();
            for (int n = 0; n < NetProtocol.MaxEventsPerPoll && _transport.Poll(out var e); n++)
            {
                switch (e.Type)
                {
                    case TransportEventType.Connected:
                        break; // they introduce themselves with Hello
                    case TransportEventType.Disconnected:
                        RemoveGuest(e.Peer, "left");
                        break;
                    case TransportEventType.Data:
                        try { Handle(e.Peer, e.Data, e.Length); }
                        catch (PacketException) { /* malformed packet from a guest: ignore it (TDD 12.3) */ }
                        break;
                }
            }
        }

        private void Handle(int peer, byte[] data, int length)
        {
            _reader.Set(data, length);
            bool versionOk = _reader.Header(out _, out var type);
            if (!versionOk)
            {
                if (type == MessageType.Hello) Reject(peer, RejectReason.VersionMismatch);
                return;
            }

            if (type == MessageType.Hello) { HandleHello(peer); return; }
            if (!_peerSeat.TryGetValue(peer, out int seat)) return; // not joined yet
            var guest = _guests[seat];

            switch (type)
            {
                case MessageType.LobbyRequest:
                    if (InMatch) return;
                    bool ready = _reader.Bool();
                    byte character = (byte)(_reader.U8() % LobbyState.CharacterCount);
                    Lobby.Seats[seat].Ready = ready;
                    Lobby.Seats[seat].Character = character;
                    BroadcastLobby();
                    break;

                case MessageType.InputFrame:
                    if (!InMatch) return;
                    int count = Codecs.ReadInputs(_reader, _readInputs);
                    for (int i = 0; i < count; i++) guest.Receive(_readInputs[i]);
                    break;

                case MessageType.PauseRequest:
                    if (!InMatch || guest.PausesLeft <= 0) return;
                    if (Simulation.Pause())
                    {
                        guest.PausesLeft--;
                        _pauseDeadline = NetProtocol.PauseSeconds;
                        SendEvents();
                        SendSnapshot();
                    }
                    break;

                case MessageType.ResumeRequest:
                    Resume();
                    break;

                case MessageType.Leave:
                    _transport.Disconnect(peer);
                    RemoveGuest(peer, "left");
                    break;
            }
        }

        private void HandleHello(int peer)
        {
            if (_peerSeat.ContainsKey(peer)) return;
            string name = PlayerNames.Normalise(_reader.Str());
            byte character = (byte)(_reader.U8() % LobbyState.CharacterCount);
            ushort code = _reader.U16();

            if (JoinCode != 0 && code != JoinCode) { Reject(peer, RejectReason.WrongCode); return; }
            if (InMatch) { Reject(peer, RejectReason.MatchInProgress); return; }
            int seat = -1;
            for (int i = 1; i < Lobby.Seats.Length; i++) if (Lobby.Seats[i].Type == SeatType.Open) { seat = i; break; }
            if (seat < 0) { Reject(peer, RejectReason.LobbyFull); return; }

            _peerSeat[peer] = seat;
            _guests[seat] = new Guest(peer);
            Lobby.Seats[seat] = new LobbySeat { Type = SeatType.Human, Name = UniqueName(name, seat), Character = character };

            _writer.Begin(MessageType.Welcome);
            _writer.U8((byte)seat);
            Send(peer, Channel.Reliable, _writer);
            BroadcastLobby();
            Notice?.Invoke($"{Lobby.Seats[seat].Name} joined");
        }

        private string UniqueName(string name, int seat)
        {
            string candidate = name;
            for (int n = 2; ; n++)
            {
                bool taken = false;
                for (int i = 0; i < Lobby.Seats.Length; i++)
                    if (i != seat && Lobby.Seats[i].Type == SeatType.Human && Lobby.Seats[i].Name == candidate) taken = true;
                if (!taken) return candidate;
                string suffix = " " + n;
                candidate = (name.Length + suffix.Length > NetProtocol.MaxNameLength ? name.Substring(0, NetProtocol.MaxNameLength - suffix.Length) : name) + suffix;
            }
        }

        private void Reject(int peer, RejectReason reason)
        {
            _writer.Begin(MessageType.Reject);
            _writer.U8((byte)reason);
            Send(peer, Channel.Reliable, _writer);
            _transport.Disconnect(peer);
        }

        /// <summary>A guest left or dropped: in the lobby the seat opens; in a match an AI takes over and keeps the score (TDD 8.9).</summary>
        private void RemoveGuest(int peer, string verb)
        {
            if (!_peerSeat.TryGetValue(peer, out int seat)) return;
            _peerSeat.Remove(peer);
            _guests[seat] = null;
            string name = Lobby.Seats[seat].Name;
            if (InMatch)
            {
                Lobby.Seats[seat].Type = SeatType.AI;
                Lobby.Seats[seat].Name = "AI (dropped)";
                _ai[seat] = new AIController(seat, AIDifficulty.Medium, (uint)(Simulation.State.Tick + seat + 1));
                Notice?.Invoke($"{name} {verb}: an AI took over");
            }
            else
            {
                Lobby.Seats[seat] = new LobbySeat { Type = SeatType.Open, Character = Lobby.Seats[seat].Character };
                Notice?.Invoke($"{name} {verb}");
                BroadcastLobby();
            }
        }

        private int PeerForSeat(int seat)
        {
            foreach (var kv in _peerSeat) if (kv.Value == seat) return kv.Key;
            return -1;
        }

        private void ClearGuestReady()
        {
            for (int i = 0; i < Lobby.Seats.Length; i++) if (!Lobby.Seats[i].IsHost) Lobby.Seats[i].Ready = false;
        }

        private void BroadcastLobby()
        {
            _writer.Begin(MessageType.LobbyState);
            Lobby.Write(_writer);
            Broadcast(Channel.Reliable);
            LobbyChanged?.Invoke();
        }

        private void SendEvents()
        {
            var events = Simulation.Events;
            if (Codecs.CountShared(events) == 0) return;
            Codecs.WriteEvents(_writer, events);
            Broadcast(Channel.Reliable);
        }

        private void SendSnapshot()
        {
            Codecs.WriteSnapshot(_writer, Simulation.State, _acked, ++_snapshotSequence);
            Broadcast(Channel.Unreliable);
        }

        private void Broadcast(Channel channel, PacketWriter packet = null)
        {
            foreach (var peer in _peerSeat.Keys) Send(peer, channel, packet ?? _writer);
        }

        private void Send(int peer, Channel channel, PacketWriter packet) => _transport.Send(peer, channel, packet);

        /// <summary>A connected guest's input stream: inputs arrive out of order and repeated, and are applied one per tick.</summary>
        private sealed class Guest
        {
            private readonly Queue<PlayerInput> _queue = new Queue<PlayerInput>();
            private uint _newest;
            private PlayerInput _last;

            public int Peer { get; }
            public int PausesLeft { get; set; } = NetProtocol.PauseBudget;

            public Guest(int peer) { Peer = peer; }

            public void ResetForMatch()
            {
                _queue.Clear();
                _newest = 0;
                _last = default;
                PausesLeft = NetProtocol.PauseBudget;
            }

            public void Receive(PlayerInput input)
            {
                if (input.Sequence <= _newest) return; // a repeat or a late packet
                _newest = input.Sequence;
                _queue.Enqueue(input);
                // If the guest got ahead (a burst after a stall), drop the oldest but keep any blast press.
                while (_queue.Count > MaxQueuedInputs)
                {
                    var dropped = _queue.Dequeue();
                    if (dropped.BlastPressed && _queue.Count > 0)
                    {
                        var next = _queue.Dequeue();
                        next.BlastPressed = true;
                        var rest = _queue.ToArray();
                        _queue.Clear();
                        _queue.Enqueue(next);
                        foreach (var r in rest) _queue.Enqueue(r);
                    }
                }
            }

            /// <summary>The input for this tick. With none queued, the last movement repeats (never a blast).</summary>
            public PlayerInput NextInput(ref uint acked)
            {
                if (_queue.Count > 0)
                {
                    _last = _queue.Dequeue();
                    acked = _last.Sequence;
                    return _last;
                }
                var repeat = _last;
                repeat.BlastPressed = false;
                return repeat;
            }
        }
    }

    internal static class PacketWriterExtensions
    {
        public static PacketWriter Also(this PacketWriter w, Action<PacketWriter> write) { write(w); return w; }
    }
}

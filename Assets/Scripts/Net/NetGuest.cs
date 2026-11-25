using System;
using System.Collections.Generic;
using BOW.Simulation;

namespace BOW.Net
{
    public enum GuestStatus : byte
    {
        Connecting,
        Lobby,
        InMatch,
        Rejected,
        Disconnected,
    }

    /// <summary>
    /// The guest side of a multiplayer session (TDD 8.1, 8.7). Joins the host's lobby, then during a match sends
    /// its inputs and renders the host's snapshots: other paddles and balls from a 100 ms interpolation buffer,
    /// its own paddle predicted ahead with the same paddle rules the host uses.
    /// Engine-free so it can be tested over <see cref="LoopbackNetwork"/>.
    /// </summary>
    public sealed class NetGuest : IDisposable
    {
        private const int BufferSize = 32;
        private const float SnapError = 0.3f;     // TDD 8.7: larger corrections snap, smaller ones blend
        private const float BlendSeconds = 0.1f;

        private readonly ITransport _transport;
        private readonly PacketWriter _writer = new PacketWriter(256);
        private readonly PacketReader _reader = new PacketReader();
        private readonly string _name;
        private readonly ushort _joinCode;
        private int _character;
        private bool _ready;

        // Match
        private readonly MatchState[] _snapshots = new MatchState[BufferSize];
        private readonly uint[] _ackScratch = new uint[SeatFrame.SeatCount];
        private int _snapshotCount;
        private int _newestTick = -1;
        private uint _newestSequence;
        private double _renderTick = -1;
        private readonly List<PlayerInput> _pendingInputs = new List<PlayerInput>();
        private readonly List<PlayerInput> _sentWindow = new List<PlayerInput>();
        private uint _sequence;
        private SeatState _predicted;
        private float _offset;
        private readonly List<SimEvent> _incomingEvents = new List<SimEvent>();
        private readonly List<SimEvent> _readyEvents = new List<SimEvent>();
        private readonly MatchState _scratch = new MatchState();

        public GuestStatus Status { get; private set; } = GuestStatus.Connecting;
        public RejectReason RejectReason { get; private set; }
        public int LocalSeat { get; private set; } = -1;
        public LobbyState Lobby { get; private set; }

        /// <summary>The match being shown. Its <see cref="MatchSimulation.State"/> is the latest display state; it is never stepped.</summary>
        public MatchSimulation Simulation { get; private set; }
        public int[] Characters { get; private set; }

        /// <summary>Ticks of snapshot history currently buffered ahead of the render time; useful for a connection indicator.</summary>
        public double BufferedTicks => _newestTick - _renderTick;

        public event Action LobbyChanged;
        public event Action MatchStarted;
        public event Action ReturnedToLobby;
        /// <summary>The session is over: rejected, kicked, the host left, or the connection dropped. The string says why.</summary>
        public event Action<string> Ended;

        public NetGuest(ITransport transport, string name, int character, ushort joinCode = 0)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _name = PlayerNames.Normalise(name);
            _character = character % LobbyState.CharacterCount;
            _joinCode = joinCode;
            for (int i = 0; i < BufferSize; i++) _snapshots[i] = new MatchState();
        }

        // ------------------------------------------------------------------ lobby

        public void SetReady(bool ready)
        {
            _ready = ready;
            SendLobbyRequest();
        }

        public void SetCharacter(int character)
        {
            _character = character % LobbyState.CharacterCount;
            SendLobbyRequest();
        }

        public bool IsReady => _ready;
        public int Character => _character;

        private void SendLobbyRequest()
        {
            if (Status != GuestStatus.Lobby) return;
            _writer.Begin(MessageType.LobbyRequest);
            _writer.Bool(_ready);
            _writer.U8((byte)_character);
            _transport.Send(NetProtocol.HostPeer, Channel.Reliable, _writer);
        }

        // ------------------------------------------------------------------ match

        /// <summary>
        /// One local tick (60 Hz): sends this tick's input, moves the predicted paddle and advances the render clock.
        /// Returns true when a blast was triggered locally, so the caller can play its effect at once (TDD 8.7).
        /// </summary>
        public bool Tick(PlayerInput input)
        {
            if (Status != GuestStatus.InMatch || _newestTick < 0) return false;
            var sim = Simulation;
            var phase = sim.State.Phase;
            bool moving = phase == MatchPhase.Countdown || phase == MatchPhase.Playing;

            input.Sequence = ++_sequence;
            bool blasted = false;
            if (moving)
            {
                blasted = PredictTick(ref _predicted, input, phase);
                _pendingInputs.Add(input);
                if (_pendingInputs.Count > 120) _pendingInputs.RemoveAt(0);
            }
            else
            {
                input.Move = 0f;
                input.HasTarget = false;
                input.BlastPressed = false;
            }

            _sentWindow.Add(input);
            if (_sentWindow.Count > NetProtocol.RedundantInputs) _sentWindow.RemoveAt(0);
            Codecs.WriteInputs(_writer, _sentWindow);
            _transport.Send(NetProtocol.HostPeer, Channel.Unreliable, _writer);

            // Render clock: one tick per tick, gently pulled toward (newest snapshot - delay). While the host's
            // clock is stopped (paused or ended) it catches up to the newest tick and waits there.
            double target = _newestTick - NetProtocol.InterpolationDelayTicks;
            if (!moving) _renderTick = Math.Min(_newestTick, Math.Max(_renderTick, target) + 1);
            else if (_renderTick < 0 || Math.Abs(target - _renderTick) > NetProtocol.InterpolationDelayTicks * 2) _renderTick = target;
            else _renderTick += 1 + (target - _renderTick) * 0.05;

            _offset *= Math.Max(0f, 1f - sim.DeltaTime / BlendSeconds);
            if (Math.Abs(_offset) < 0.001f) _offset = 0f;
            ReleaseEvents();
            return blasted;
        }

        /// <summary>One tick of the local paddle exactly as the host runs it: move, then blast if the cooldown allows.</summary>
        private bool PredictTick(ref SeatState seat, PlayerInput input, MatchPhase phase)
        {
            if (!seat.IsActive) return false;
            PaddlePrediction.Advance(ref seat, input, Simulation.Config, Simulation.Arena, Simulation.DeltaTime);
            if (!input.BlastPressed || phase != MatchPhase.Playing || seat.BlastCooldown > 0f) return false;
            seat.BlastCooldown = Simulation.Config.BlastCooldown;
            return true;
        }

        /// <summary>
        /// Fills the two states the views interpolate between, and the blend factor. Remote paddles and balls are
        /// 100 ms in the past; the local paddle is the prediction. Also copies <paramref name="current"/> into
        /// <see cref="Simulation"/>'s state for the HUD.
        /// </summary>
        public void GetDisplay(MatchState previous, MatchState current, out float alpha)
        {
            alpha = 1f;
            if (_snapshotCount == 0) return;

            int newest = IndexOfTick(_newestTick);
            int a = -1, b = -1;
            for (int i = 0; i < _snapshotCount; i++)
            {
                var s = _snapshots[i];
                if (s.Tick <= _renderTick && (a < 0 || s.Tick > _snapshots[a].Tick)) a = i;
                if (s.Tick > _renderTick && (b < 0 || s.Tick < _snapshots[b].Tick)) b = i;
            }
            if (a < 0) a = b;
            if (b < 0) b = a;
            if (a < 0) a = b = newest;

            previous.CopyFrom(_snapshots[a]);
            current.CopyFrom(_snapshots[b]);
            int span = _snapshots[b].Tick - _snapshots[a].Tick;
            alpha = span > 0 ? (float)Math.Max(0.0, Math.Min(1.0, (_renderTick - _snapshots[a].Tick) / span)) : 1f;

            // The HUD and phase come from the newest state we know, so pauses, scores and results show without delay.
            var latest = _snapshots[newest];
            current.Phase = latest.Phase;
            current.CountdownRemaining = latest.CountdownRemaining;
            current.ClockRemaining = latest.ClockRemaining;
            current.WinnerSeat = latest.WinnerSeat;
            current.IsDraw = latest.IsDraw;
            current.SuddenDeath = latest.SuddenDeath;
            current.FinalCountdownRaised = latest.FinalCountdownRaised;
            for (int i = 0; i < MatchState.MaxSeats; i++)
            {
                var src = latest.Seats[i];
                ref SeatState dst = ref current.Seats[i];
                dst.Status = src.Status;
                dst.GoalsAgainst = src.GoalsAgainst;
                dst.Lives = src.Lives;
                dst.Placement = src.Placement;
                dst.EliminationOrder = src.EliminationOrder;
                dst.Blasts = src.Blasts;
                dst.Saves = src.Saves;
                dst.LongestCleanTime = src.LongestCleanTime;
            }

            // Own paddle: predicted now, with any small correction blended out.
            if (LocalSeat >= 0 && _predicted.IsActive)
            {
                float shown = _predicted.PaddlePosition + _offset;
                for (int pass = 0; pass < 2; pass++)
                {
                    ref SeatState me = ref (pass == 0 ? previous : current).Seats[LocalSeat];
                    me.PaddlePosition = shown;
                    me.PaddleVelocity = _predicted.PaddleVelocity;
                    me.BlastCooldown = _predicted.BlastCooldown;
                }
            }

            Simulation.LoadState(current);
        }

        /// <summary>Events whose moment has come at the render time (goals, blasts, phase changes...). Cleared on each call.</summary>
        public IReadOnlyList<SimEvent> TakeEvents()
        {
            _scratchEvents.Clear();
            _scratchEvents.AddRange(_readyEvents);
            _readyEvents.Clear();
            return _scratchEvents;
        }
        private readonly List<SimEvent> _scratchEvents = new List<SimEvent>();

        public void RequestPause() => SendSimple(MessageType.PauseRequest);
        public void RequestResume() => SendSimple(MessageType.ResumeRequest);

        /// <summary>Leaves the session politely (US-6.4): the host hands this seat to an AI.</summary>
        public void Leave()
        {
            if (Status == GuestStatus.Lobby || Status == GuestStatus.InMatch || Status == GuestStatus.Connecting)
            {
                SendSimple(MessageType.Leave);
                _transport.Disconnect(NetProtocol.HostPeer);
            }
            Status = GuestStatus.Disconnected;
        }

        public void Dispose()
        {
            Leave();
            _transport.Dispose();
        }

        private void SendSimple(MessageType type)
        {
            if (Status == GuestStatus.Rejected || Status == GuestStatus.Disconnected) return;
            _transport.Send(NetProtocol.HostPeer, Channel.Reliable, _writer.Begin(type));
        }

        // ------------------------------------------------------------------ network

        public void Update()
        {
            _transport.Update();
            for (int n = 0; n < NetProtocol.MaxEventsPerPoll && _transport.Poll(out var e); n++)
            {
                switch (e.Type)
                {
                    case TransportEventType.Connected:
                        _writer.Begin(MessageType.Hello);
                        _writer.Str(_name, 32);
                        _writer.U8((byte)_character);
                        _writer.U16(_joinCode);
                        _transport.Send(NetProtocol.HostPeer, Channel.Reliable, _writer);
                        break;
                    case TransportEventType.Disconnected:
                        End(GuestStatus.Disconnected, Status == GuestStatus.Connecting ? "Couldn't reach the host." : "The host left the match.");
                        break;
                    case TransportEventType.Data:
                        try { Handle(e.Data, e.Length); }
                        catch (PacketException) { }
                        break;
                }
            }
        }

        private void Handle(byte[] data, int length)
        {
            _reader.Set(data, length);
            if (!_reader.Header(out _, out var type)) return;
            switch (type)
            {
                case MessageType.Welcome:
                    LocalSeat = _reader.U8();
                    Status = GuestStatus.Lobby;
                    break;

                case MessageType.Reject:
                    RejectReason = (RejectReason)_reader.U8();
                    End(GuestStatus.Rejected, RejectReasonText.Describe(RejectReason));
                    break;

                case MessageType.LobbyState:
                    Lobby = LobbyState.Read(_reader);
                    if (LocalSeat >= 0)
                    {
                        _ready = Lobby.Seats[LocalSeat].Ready;
                        _character = Lobby.Seats[LocalSeat].Character;
                    }
                    LobbyChanged?.Invoke();
                    break;

                case MessageType.StartMatch:
                    Lobby = LobbyState.Read(_reader);
                    StartMatch(_reader.U32());
                    break;

                case MessageType.Snapshot:
                    if (Status == GuestStatus.InMatch) ReceiveSnapshot();
                    break;

                case MessageType.GameEvents:
                    if (Status == GuestStatus.InMatch) ReceiveEvents();
                    break;

                case MessageType.ReturnToLobby:
                    Simulation = null;
                    Status = GuestStatus.Lobby;
                    _ready = false;
                    ReturnedToLobby?.Invoke();
                    break;

                case MessageType.MatchEnded:
                    End(GuestStatus.Disconnected, "The host ended the match.");
                    break;
            }
        }

        private void StartMatch(uint seed)
        {
            Simulation = new MatchSimulation(Lobby.ToMatchSettings(seed));
            Characters = Lobby.Characters();
            Status = GuestStatus.InMatch;
            _snapshotCount = 0;
            _newestTick = -1;
            _newestSequence = 0;
            _renderTick = -1;
            _pendingInputs.Clear();
            _sentWindow.Clear();
            _incomingEvents.Clear();
            _readyEvents.Clear();
            _offset = 0f;
            _predicted = Simulation.State.Seats[LocalSeat];
            MatchStarted?.Invoke();
        }

        private void ReceiveSnapshot()
        {
            _scratch.CopyFrom(Simulation.State);
            uint sequence = Codecs.ReadSnapshot(_reader, _scratch, _ackScratch);
            if (sequence <= _newestSequence) return; // late or repeated
            _newestSequence = sequence;
            int tick = _scratch.Tick;

            // A paused or finished match repeats its tick, so a same-tick snapshot replaces the stored one.
            int slot = IndexOfTick(tick);
            if (slot < 0)
            {
                // Keep the newest BufferSize snapshots: replace the oldest when full.
                slot = _snapshotCount;
                if (_snapshotCount < BufferSize) _snapshotCount++;
                else
                {
                    slot = 0;
                    for (int i = 1; i < BufferSize; i++) if (_snapshots[i].Tick < _snapshots[slot].Tick) slot = i;
                }
            }
            _snapshots[slot].CopyFrom(_scratch);
            _newestTick = Math.Max(_newestTick, tick);
            Reconcile(_scratch.Seats[LocalSeat], _ackScratch[LocalSeat], _scratch.Phase);
        }

        /// <summary>Resets the predicted paddle to the host's and replays the inputs the host hasn't applied yet (TDD 8.7).</summary>
        private void Reconcile(SeatState authoritative, uint acked, MatchPhase phase)
        {
            float before = _predicted.PaddlePosition + _offset;
            _pendingInputs.RemoveAll(i => i.Sequence <= acked);
            var seat = authoritative;
            if (phase == MatchPhase.Countdown || phase == MatchPhase.Playing)
                foreach (var input in _pendingInputs) PredictTick(ref seat, input, phase);
            _predicted = seat;

            float error = before - _predicted.PaddlePosition;
            _offset = Math.Abs(error) < SnapError ? error : 0f;
        }

        private void ReceiveEvents()
        {
            int start = _incomingEvents.Count;
            Codecs.ReadEvents(_reader, _incomingEvents);
            // Our own blasts were already shown when pressed.
            for (int i = _incomingEvents.Count - 1; i >= start; i--)
                if (_incomingEvents[i].Type == SimEventType.Blast && _incomingEvents[i].Seat == LocalSeat) _incomingEvents.RemoveAt(i);
            // Pause and end show at once; everything else waits for the render clock to reach it.
            ReleaseEvents();
        }

        private void ReleaseEvents()
        {
            for (int i = 0; i < _incomingEvents.Count; i++)
            {
                var e = _incomingEvents[i];
                bool now = e.Type == SimEventType.PhaseChanged || e.Type == SimEventType.MatchEnded || _renderTick < 0;
                if (!now && e.Tick > _renderTick) continue;
                _readyEvents.Add(e);
                _incomingEvents.RemoveAt(i--);
            }
        }

        private int IndexOfTick(int tick)
        {
            for (int i = 0; i < _snapshotCount; i++) if (_snapshots[i].Tick == tick) return i;
            return -1;
        }

        private void End(GuestStatus status, string reason)
        {
            if (Status == GuestStatus.Rejected || Status == GuestStatus.Disconnected) return;
            Status = status;
            Ended?.Invoke(reason);
        }
    }
}

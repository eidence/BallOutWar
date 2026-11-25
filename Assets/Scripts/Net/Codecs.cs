using System;
using System.Collections.Generic;
using BOW.Simulation;

namespace BOW.Net
{
    /// <summary>Serialisers for the in-match messages (TDD 8.6).</summary>
    public static class Codecs
    {
        // ---------- Snapshot: Host -> All, unreliable, 30 Hz ----------

        /// <summary>
        /// Writes the whole match state plus, per seat, the last input sequence the host has applied.
        /// Positions are full floats (about 330 bytes, ~10 KB/s per guest, inside the 20 KB/s budget of NFR-4);
        /// TDD 8.6's 16-bit quantisation is a later optimisation.
        /// </summary>
        public static void WriteSnapshot(PacketWriter w, MatchState s, uint[] ackedInput, uint sequence)
        {
            w.Begin(MessageType.Snapshot);
            w.U32(sequence);
            w.I32(s.Tick);
            w.U8((byte)s.Phase);
            w.F32(s.CountdownRemaining);
            w.F32(s.ClockRemaining);
            w.F32(s.Elapsed);
            byte flags = 0;
            if (s.BallsLaunched) flags |= 1;
            if (s.SuddenDeath) flags |= 2;
            if (s.FinalCountdownRaised) flags |= 4;
            if (s.IsDraw) flags |= 8;
            w.U8(flags);
            w.U8((byte)s.EliminationCount);
            w.I8((sbyte)s.WinnerSeat);
            w.F32(s.SpeedMultiplier);
            w.U32(s.Random.State);
            for (int i = 0; i < MatchState.MaxSeats; i++)
            {
                var seat = s.Seats[i];
                w.U8((byte)seat.Status);
                w.F32(seat.PaddlePosition);
                w.F32(seat.PaddleVelocity);
                w.F32(seat.BlastCooldown);
                w.U8((byte)Math.Min(255, seat.GoalsAgainst));
                w.U8((byte)Math.Max(0, Math.Min(255, seat.Lives)));
                w.U8((byte)seat.EliminationOrder);
                w.U8((byte)seat.Placement);
                w.U16((ushort)Math.Min(ushort.MaxValue, seat.Blasts));
                w.U16((ushort)Math.Min(ushort.MaxValue, seat.Saves));
                w.F32(seat.TimeSinceConceded);
                w.F32(seat.LongestCleanTime);
                w.U32(ackedInput != null ? ackedInput[i] : 0u);
            }
            for (int i = 0; i < MatchState.MaxBalls; i++)
            {
                var b = s.Balls[i];
                byte bf = 0;
                if (b.Active) bf |= 1;
                if (b.PendingSpawn) bf |= 2;
                w.U8(bf);
                w.F32(b.Position.X);
                w.F32(b.Position.Y);
                w.F32(b.Velocity.X);
                w.F32(b.Velocity.Y);
                w.I8((sbyte)b.LastTouchSeat);
                w.F32(b.SlowTime);
                w.F32(b.SpawnTimer);
            }
        }

        public static uint ReadSnapshot(PacketReader r, MatchState s, uint[] ackedInput)
        {
            uint sequence = r.U32();
            s.Tick = r.I32();
            s.Phase = (MatchPhase)Math.Min(r.U8(), (byte)MatchPhase.Ended);
            s.CountdownRemaining = r.F32();
            s.ClockRemaining = r.F32();
            s.Elapsed = r.F32();
            byte flags = r.U8();
            s.BallsLaunched = (flags & 1) != 0;
            s.SuddenDeath = (flags & 2) != 0;
            s.FinalCountdownRaised = (flags & 4) != 0;
            s.IsDraw = (flags & 8) != 0;
            s.EliminationCount = r.U8();
            s.WinnerSeat = r.I8();
            s.SpeedMultiplier = r.F32();
            s.Random = new SimRandom { State = r.U32() };
            for (int i = 0; i < MatchState.MaxSeats; i++)
            {
                s.Seats[i] = new SeatState
                {
                    Status = (SeatStatus)Math.Min(r.U8(), (byte)SeatStatus.Spectating),
                    PaddlePosition = r.F32(),
                    PaddleVelocity = r.F32(),
                    BlastCooldown = r.F32(),
                    GoalsAgainst = r.U8(),
                    Lives = r.U8(),
                    EliminationOrder = r.U8(),
                    Placement = r.U8(),
                    Blasts = r.U16(),
                    Saves = r.U16(),
                    TimeSinceConceded = r.F32(),
                    LongestCleanTime = r.F32(),
                };
                uint ack = r.U32();
                if (ackedInput != null) ackedInput[i] = ack;
            }
            for (int i = 0; i < MatchState.MaxBalls; i++)
            {
                byte bf = r.U8();
                s.Balls[i] = new BallState
                {
                    Active = (bf & 1) != 0,
                    PendingSpawn = (bf & 2) != 0,
                    Position = new Vec2(r.F32(), r.F32()),
                    Velocity = new Vec2(r.F32(), r.F32()),
                    LastTouchSeat = r.I8(),
                    SlowTime = r.F32(),
                    SpawnTimer = r.F32(),
                };
            }
            return sequence;
        }

        // ---------- InputFrame: Guest -> Host, unreliable, 60 Hz ----------

        /// <summary>Writes the newest inputs (oldest first), repeating recent ones so a lost packet costs nothing.</summary>
        public static void WriteInputs(PacketWriter w, IReadOnlyList<PlayerInput> recent)
        {
            w.Begin(MessageType.InputFrame);
            int n = Math.Min(recent.Count, NetProtocol.RedundantInputs);
            w.U8((byte)n);
            for (int i = recent.Count - n; i < recent.Count; i++)
            {
                var input = recent[i];
                w.U32(input.Sequence);
                byte flags = 0;
                if (input.HasTarget) flags |= 1;
                if (input.BlastPressed) flags |= 2;
                w.U8(flags);
                w.F32(input.HasTarget ? input.Target : input.Move);
            }
        }

        /// <summary>Reads an input frame, clamping values so a modified client can't send anything out of range (TDD 12.3).</summary>
        public static int ReadInputs(PacketReader r, PlayerInput[] into)
        {
            int n = Math.Min((int)r.U8(), into.Length);
            for (int i = 0; i < n; i++)
            {
                uint seq = r.U32();
                byte flags = r.U8();
                float v = r.FiniteF32();
                bool target = (flags & 1) != 0;
                into[i] = new PlayerInput
                {
                    Sequence = seq,
                    HasTarget = target,
                    Target = target ? Math.Max(-50f, Math.Min(50f, v)) : 0f,
                    Move = target ? 0f : Math.Max(-1f, Math.Min(1f, v)),
                    BlastPressed = (flags & 2) != 0,
                };
            }
            return n;
        }

        // ---------- GameEvents: Host -> All, reliable ----------

        /// <summary>True for events guests need for sound, effects and the HUD.</summary>
        public static bool IsShared(SimEventType type) => type != SimEventType.BlastDenied;

        public static void WriteEvents(PacketWriter w, IReadOnlyList<SimEvent> events)
        {
            w.Begin(MessageType.GameEvents);
            int countAt = w.Length;
            w.U8(0);
            int n = 0;
            foreach (var e in events)
            {
                if (!IsShared(e.Type) || n == 255) continue;
                w.U8((byte)e.Type);
                w.I32(e.Tick);
                w.I8((sbyte)e.Seat);
                w.I8((sbyte)e.Ball);
                w.F32(e.Position.X);
                w.F32(e.Position.Y);
                n++;
            }
            w.Buffer[countAt] = (byte)n;
        }

        public static void ReadEvents(PacketReader r, List<SimEvent> into)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                into.Add(new SimEvent
                {
                    Type = (SimEventType)r.U8(),
                    Tick = r.I32(),
                    Seat = r.I8(),
                    Ball = r.I8(),
                    Position = new Vec2(r.F32(), r.F32()),
                });
            }
        }

        public static int CountShared(IReadOnlyList<SimEvent> events)
        {
            int n = 0;
            foreach (var e in events) if (IsShared(e.Type)) n++;
            return n;
        }
    }

    /// <summary>The LAN discovery beacon, broadcast by a host once a second (TDD 8.3).</summary>
    public struct LanBeacon
    {
        private const uint Magic = 0x4C574F42; // "BOWL"

        public ushort Protocol;
        public string MatchName;
        public byte Players;
        public byte MaxPlayers;
        public GameMode Mode;
        public bool InGame;
        public bool CodeRequired;
        public ushort GamePort;

        public bool Joinable => Protocol == NetProtocol.Version && !InGame && Players < MaxPlayers;

        public string Status
        {
            get
            {
                if (Protocol != NetProtocol.Version) return "Different version";
                if (InGame) return "In a match";
                if (Players >= MaxPlayers) return "Full";
                return $"{Players}/{MaxPlayers} players";
            }
        }

        public byte[] Encode()
        {
            var w = new PacketWriter(64);
            w.U32(Magic);
            w.U16(Protocol);
            w.Str(MatchName, 32);
            w.U8(Players);
            w.U8(MaxPlayers);
            w.U8((byte)Mode);
            w.Bool(InGame);
            w.Bool(CodeRequired);
            w.U16(GamePort);
            return w.ToArray();
        }

        public static bool TryDecode(byte[] data, int length, out LanBeacon beacon)
        {
            beacon = default;
            try
            {
                var r = new PacketReader(data, length);
                if (r.U32() != Magic) return false;
                beacon.Protocol = r.U16();
                beacon.MatchName = r.Str();
                beacon.Players = r.U8();
                beacon.MaxPlayers = r.U8();
                beacon.Mode = (GameMode)r.U8();
                beacon.InGame = r.Bool();
                beacon.CodeRequired = r.Bool();
                beacon.GamePort = r.U16();
                return true;
            }
            catch (PacketException)
            {
                return false;
            }
        }
    }
}

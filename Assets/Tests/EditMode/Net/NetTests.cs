using System;
using System.Collections.Generic;
using System.Linq;
using BOW.Net;
using NUnit.Framework;

namespace BOW.Simulation.Tests.Net
{
    public class CodecTests
    {
        [Test]
        public void Snapshot_RoundTripsTheWholeMatchState()
        {
            var sim = SimTestUtil.Create(balls: 3);
            var inputs = new PlayerInput[4];
            for (int t = 0; t < 600; t++)
            {
                for (int i = 0; i < 4; i++) inputs[i] = PlayerInput.Analog((float)Math.Sin(t * 0.05 + i), t % 97 == i);
                sim.Step(inputs);
            }

            var w = new PacketWriter();
            var acks = new uint[] { 1, 2, 3, 4 };
            Codecs.WriteSnapshot(w, sim.State, acks, 42u);
            var r = new PacketReader(w.Buffer, w.Length);
            Assert.That(r.Header(out _, out var type), Is.True);
            Assert.That(type, Is.EqualTo(MessageType.Snapshot));

            var copy = new MatchState();
            var readAcks = new uint[4];
            Assert.That(Codecs.ReadSnapshot(r, copy, readAcks), Is.EqualTo(42u));
            Assert.That(copy.ContentEquals(sim.State), Is.True);
            Assert.That(readAcks, Is.EqualTo(acks));
            Assert.That(r.Remaining, Is.Zero);
            Assert.That(w.Length, Is.LessThan(400), "snapshot size drives bandwidth (NFR-4)");
        }

        [Test]
        public void Inputs_AreClampedAndBadNumbersRejected()
        {
            var w = new PacketWriter();
            Codecs.WriteInputs(w, new List<PlayerInput> { new PlayerInput { Sequence = 5, Move = 7f, BlastPressed = true } });
            var r = new PacketReader(w.Buffer, w.Length);
            r.Header(out _, out _);
            var into = new PlayerInput[3];
            Assert.That(Codecs.ReadInputs(r, into), Is.EqualTo(1));
            Assert.That(into[0].Move, Is.EqualTo(1f));
            Assert.That(into[0].BlastPressed, Is.True);
            Assert.That(into[0].Sequence, Is.EqualTo(5u));

            Codecs.WriteInputs(w, new List<PlayerInput> { new PlayerInput { Sequence = 6, Move = float.NaN } });
            r.Set(w.Buffer, w.Length);
            r.Header(out _, out _);
            Assert.Throws<PacketException>(() => Codecs.ReadInputs(r, into));
        }

        [Test]
        public void Beacon_RoundTripsAndIgnoresOtherTraffic()
        {
            var b = new LanBeacon { Protocol = NetProtocol.Version, MatchName = "Jo's game", Players = 2, MaxPlayers = 4, Mode = GameMode.Elimination, GamePort = 47778 };
            byte[] data = b.Encode();
            Assert.That(LanBeacon.TryDecode(data, data.Length, out var read), Is.True);
            Assert.That(read.MatchName, Is.EqualTo("Jo's game"));
            Assert.That(read.Joinable, Is.True);
            Assert.That(read.Status, Is.EqualTo("2/4 players"));

            data[0] ^= 0xFF;
            Assert.That(LanBeacon.TryDecode(data, data.Length, out _), Is.False);
            Assert.That(LanBeacon.TryDecode(new byte[3], 3, out _), Is.False);

            read.Protocol = 999;
            Assert.That(read.Joinable, Is.False);
            Assert.That(read.Status, Is.EqualTo("Different version"));
        }

        [Test]
        public void PlayerNames_AreNormalised()
        {
            Assert.That(PlayerNames.Normalise("  Jo\u0007nathan  "), Is.EqualTo("Jonathan"));
            Assert.That(PlayerNames.Normalise("ab"), Is.EqualTo("Player"));
            Assert.That(PlayerNames.Normalise(new string('x', 40)).Length, Is.EqualTo(NetProtocol.MaxNameLength));
        }
    }

    public class LobbyTests
    {
        [Test]
        public void Guests_JoinOpenSeatsInOrder_WithUniqueNames()
        {
            var h = new NetTestHarness(latency: 0.03f);
            var a = h.AddGuest("Sam", 2);
            var b = h.AddGuest("Sam", 3);
            h.Run(0.5f);

            Assert.That(a.Status, Is.EqualTo(GuestStatus.Lobby));
            Assert.That(a.LocalSeat, Is.EqualTo(1));
            Assert.That(b.LocalSeat, Is.EqualTo(2));
            Assert.That(h.Host.Lobby.Seats[1].Name, Is.EqualTo("Sam"));
            Assert.That(h.Host.Lobby.Seats[2].Name, Is.EqualTo("Sam 2"));
            Assert.That(b.Lobby.Seats[1].Character, Is.EqualTo(2), "guests see the full lobby");
            Assert.That(h.Host.Beacon(47778).Status, Is.EqualTo("3/4 players"));
        }

        [Test]
        public void Start_WaitsForReady_AndSettingsChangesClearReady()
        {
            var h = new NetTestHarness();
            var g = h.AddGuest();
            h.Run(0.2f);
            Assert.That(h.Host.StartBlocker(), Does.Contain("ready"));

            g.SetReady(true);
            h.Run(0.2f);
            Assert.That(h.Host.StartBlocker(), Is.Null);

            h.Host.SetSettings(new LobbySettings { Mode = GameMode.Elimination, GoalsToEliminate = 3 });
            h.Run(0.2f);
            Assert.That(g.IsReady, Is.False);
            Assert.That(g.Lobby.Settings.Mode, Is.EqualTo(GameMode.Elimination));
            Assert.That(h.Host.StartBlocker(), Is.Not.Null);
        }

        [Test]
        public void Join_IsRefusedWhenFull_InProgress_OrWithTheWrongCode()
        {
            var h = new NetTestHarness();
            h.Host.CycleSeat(2); h.Host.CycleSeat(2);           // seat 2: Open -> AI -> Closed
            h.Host.CycleSeat(3); h.Host.CycleSeat(3);
            var first = h.AddGuest("First");
            h.Run(0.2f);
            var second = h.AddGuest("Second");
            string reason = null;
            second.Ended += r => reason = r;
            h.Run(0.2f);
            Assert.That(first.Status, Is.EqualTo(GuestStatus.Lobby));
            Assert.That(second.Status, Is.EqualTo(GuestStatus.Rejected));
            Assert.That(second.RejectReason, Is.EqualTo(RejectReason.LobbyFull));
            Assert.That(reason, Is.EqualTo("That match is full."));

            var coded = new NetTestHarness();
            coded.Host.JoinCode = 1234;
            var wrong = coded.AddGuest("Wrong", 0, 4321);
            var right = coded.AddGuest("Right", 0, 1234);
            coded.Run(0.2f);
            Assert.That(wrong.RejectReason, Is.EqualTo(RejectReason.WrongCode));
            Assert.That(right.Status, Is.EqualTo(GuestStatus.Lobby));

            right.SetReady(true);
            coded.Run(0.2f);
            Assert.That(coded.Host.StartMatch(), Is.True);
            var late = coded.AddGuest("Late", 0, 1234);
            coded.Run(0.2f);
            Assert.That(late.RejectReason, Is.EqualTo(RejectReason.MatchInProgress));
        }

        [Test]
        public void Join_WithAnotherProtocolVersion_IsRefused()
        {
            var net = new LoopbackNetwork();
            var host = new NetHost(net.CreateHost(), "Match", "Host", 0);
            var raw = net.CreateGuest();
            net.Advance(0.01);
            host.Update();
            var w = new PacketWriter();
            w.U16(NetProtocol.Version + 1);
            w.U8((byte)MessageType.Hello);
            w.Str("Old");
            w.U8(0);
            w.U16(0);
            raw.Send(NetProtocol.HostPeer, Channel.Reliable, w);
            net.Advance(0.01);
            host.Update();
            net.Advance(0.01);

            bool rejected = false;
            while (raw.Poll(out var e))
            {
                if (e.Type != TransportEventType.Data) continue;
                var r = new PacketReader(e.Data, e.Length);
                r.Header(out _, out var type);
                if (type == MessageType.Reject && (RejectReason)r.U8() == RejectReason.VersionMismatch) rejected = true;
            }
            Assert.That(rejected, Is.True);
            Assert.That(host.Lobby.HumanCount, Is.EqualTo(1));
        }

        [Test]
        public void LeavingTheLobby_OpensTheSeat()
        {
            var h = new NetTestHarness();
            var g = h.AddGuest("Leaver");
            h.Run(0.2f);
            Assert.That(h.Host.Lobby.Seats[1].Type, Is.EqualTo(SeatType.Human));
            g.Leave();
            h.Run(0.2f);
            Assert.That(h.Host.Lobby.Seats[1].Type, Is.EqualTo(SeatType.Open));
            Assert.That(h.Notices, Has.Some.Contains("Leaver left"));
        }
    }

    public class NetMatchTests
    {
        [Test]
        public void FullMatch_OverALaggyNetwork_EndsWithTheSameResultEverywhere()
        {
            // TDD 8.7 test conditions: 150 ms round trip, 2% loss, 20 ms jitter.
            var h = new NetTestHarness(latency: 0.075f, jitter: 0.02f, loss: 0.02f, seed: 7);
            var a = h.AddGuest("Alpha", 1);
            var b = h.AddGuest("Bravo", 2);
            h.Host.SetSettings(new LobbySettings { Mode = GameMode.Timed, DurationSeconds = 40, BallCount = 2 });
            h.JoinAllAndStart(aiSeats: 1);
            Assert.That(a.Status, Is.EqualTo(GuestStatus.InMatch));

            Assert.That(h.RunUntil(() => h.Guests.All(g => g.Simulation.State.Phase == MatchPhase.Ended), 120f), Is.True, "match should end on every device");
            var hostState = h.Host.Simulation.State;
            foreach (var g in h.Guests)
            {
                var s = g.Simulation.State;
                Assert.That(s.WinnerSeat, Is.EqualTo(hostState.WinnerSeat));
                Assert.That(s.IsDraw, Is.EqualTo(hostState.IsDraw));
                for (int seat = 0; seat < 4; seat++)
                {
                    Assert.That(s.Seats[seat].GoalsAgainst, Is.EqualTo(hostState.Seats[seat].GoalsAgainst), $"seat {seat} score");
                    Assert.That(s.Seats[seat].Placement, Is.EqualTo(hostState.Seats[seat].Placement), $"seat {seat} placement");
                }
            }

            int hostGoals = h.HostEvents.Count(e => e.Type == SimEventType.Goal);
            Assert.That(hostGoals, Is.GreaterThan(0), "AIs should concede some goals in 40 s");
            for (int i = 0; i < h.Guests.Count; i++)
                Assert.That(h.GuestEvents[i].Count(e => e.Type == SimEventType.Goal), Is.EqualTo(hostGoals), "goal events are reliable");
        }

        [Test]
        public void OwnPaddle_IsPredicted_AndAgreesWithTheHostOnceInputStops()
        {
            var h = new NetTestHarness(latency: 0.05f);
            var g = h.AddGuest();
            float move = 0f;
            h.GuestInput = (i, guest) => PlayerInput.Analog(move);
            h.HostInput = sim => default;
            h.JoinAllAndStart();

            var prev = new MatchState();
            var curr = new MatchState();
            move = 1f;
            h.Run(0.1f);
            g.GetDisplay(prev, curr, out _);
            float predicted = curr.Seats[g.LocalSeat].PaddlePosition;
            float hostNow = h.Host.Simulation.State.Seats[g.LocalSeat].PaddlePosition;
            Assert.That(predicted, Is.GreaterThan(hostNow + 0.05f), "the guest sees its own move before the host has it");

            move = 0f;
            h.Run(1f);
            g.GetDisplay(prev, curr, out _);
            Assert.That(curr.Seats[g.LocalSeat].PaddlePosition,
                Is.EqualTo(h.Host.Simulation.State.Seats[g.LocalSeat].PaddlePosition).Within(1e-4f));
        }

        [Test]
        public void GuestBlast_ShowsAtOnce_AndTheHostAppliesIt()
        {
            var h = new NetTestHarness(latency: 0.06f);
            var g = h.AddGuest();
            bool press = false;
            h.GuestInput = (i, guest) => PlayerInput.Analog(0f, press);
            h.HostInput = sim => default;
            h.JoinAllAndStart();
            h.RunUntil(() => h.Host.Simulation.State.Phase == MatchPhase.Playing, 5f);
            h.Run(0.3f);

            press = true;
            bool shown = g.Tick(PlayerInput.Analog(0f, true));
            press = false;
            Assert.That(shown, Is.True, "local blast effect plays immediately");
            h.Run(0.5f);
            Assert.That(h.Host.Simulation.State.Seats[g.LocalSeat].Blasts, Is.EqualTo(1));
        }

        [Test]
        public void DroppedGuest_IsReplacedByAnAI_AndTheMatchGoesOn()
        {
            var h = new NetTestHarness(latency: 0.02f);
            var g = h.AddGuest("Dropper");
            h.AddGuest("Stayer");
            h.Host.SetSettings(new LobbySettings { DurationSeconds = 30 });
            h.JoinAllAndStart();
            h.Run(5f);

            int seat = g.LocalSeat;
            h.Network.Cut(h.GuestTransports[0]);
            h.Run(0.5f);
            Assert.That(h.Host.Lobby.Seats[seat].Type, Is.EqualTo(SeatType.AI));
            Assert.That(h.Notices, Has.Some.Contains("Dropper left"));
            Assert.That(g.Status, Is.EqualTo(GuestStatus.Disconnected));

            float before = h.Host.Simulation.State.Seats[seat].PaddlePosition;
            bool moved = h.RunUntil(() => Math.Abs(h.Host.Simulation.State.Seats[seat].PaddlePosition - before) > 0.2f, 10f);
            Assert.That(moved, Is.True, "an AI drives the dropped seat");
            Assert.That(h.RunUntil(() => h.Host.Simulation.IsEnded, 60f), Is.True);
            h.Run(1f);
            Assert.That(h.Guests[1].Simulation.State.Phase, Is.EqualTo(MatchPhase.Ended));
        }

        [Test]
        public void GuestPauses_AreLimited_AndEndOnTheirOwn()
        {
            var h = new NetTestHarness(latency: 0.02f);
            var g = h.AddGuest();
            h.JoinAllAndStart();
            h.RunUntil(() => h.Host.Simulation.State.Phase == MatchPhase.Playing, 5f);

            for (int i = 0; i < NetProtocol.PauseBudget; i++)
            {
                g.RequestPause();
                h.Run(0.2f);
                Assert.That(h.Host.Simulation.State.Phase, Is.EqualTo(MatchPhase.Paused), $"pause {i + 1}");
                Assert.That(g.Simulation.State.Phase, Is.EqualTo(MatchPhase.Paused));
                g.RequestResume();
                h.RunUntil(() => h.Host.Simulation.State.Phase == MatchPhase.Playing, 5f);
            }

            g.RequestPause();
            h.Run(0.2f);
            Assert.That(h.Host.Simulation.State.Phase, Is.EqualTo(MatchPhase.Playing), "out of pauses");

            // The host's own pause has no budget, and a guest pause resumes by itself after 60 s.
            var h2 = new NetTestHarness();
            var g2 = h2.AddGuest();
            h2.JoinAllAndStart();
            h2.RunUntil(() => h2.Host.Simulation.State.Phase == MatchPhase.Playing, 5f);
            g2.RequestPause();
            h2.Run(1f);
            Assert.That(h2.Host.Simulation.State.Phase, Is.EqualTo(MatchPhase.Paused));
            h2.Run(NetProtocol.PauseSeconds);
            Assert.That(h2.Host.Simulation.State.Phase, Is.Not.EqualTo(MatchPhase.Paused));
        }

        [Test]
        public void HostLeaving_EndsTheSessionForGuests()
        {
            var h = new NetTestHarness(latency: 0.02f);
            var g = h.AddGuest();
            string reason = null;
            g.Ended += r => reason = r;
            h.JoinAllAndStart();
            h.Host.Close();
            h.Run(0.3f);
            Assert.That(g.Status, Is.EqualTo(GuestStatus.Disconnected));
            Assert.That(reason, Is.EqualTo("The host ended the match."));
        }

        [Test]
        public void Rematch_And_ReturnToLobby_ReachEveryGuest()
        {
            var h = new NetTestHarness(latency: 0.02f);
            var g = h.AddGuest();
            int starts = 0;
            bool backInLobby = false;
            g.MatchStarted += () => starts++;
            g.ReturnedToLobby += () => backInLobby = true;
            h.Host.SetSettings(new LobbySettings { DurationSeconds = 30 });
            h.JoinAllAndStart();
            Assert.That(starts, Is.EqualTo(1));

            Assert.That(h.Host.StartMatch(), Is.True, "rematch");
            h.Run(0.3f);
            Assert.That(starts, Is.EqualTo(2));
            Assert.That(g.Simulation.State.Tick, Is.LessThan(30), "the guest shows the new match");

            h.Host.ReturnToLobby();
            h.Run(0.3f);
            Assert.That(backInLobby, Is.True);
            Assert.That(g.Status, Is.EqualTo(GuestStatus.Lobby));
            Assert.That(g.IsReady, Is.False);
        }
    }
}

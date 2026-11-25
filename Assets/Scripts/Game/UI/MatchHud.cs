using System;
using BOW.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace BOW.Game
{
    /// <summary>
    /// In-match HUD (TDD 6.2): a portrait panel per player with their score or lives, the match clock,
    /// countdown and banners, a blast button with cooldown, and the pause and results screens.
    /// Opponent panels sit along the top on the side of their goal; the local player's panel is bottom-left.
    /// </summary>
    public sealed class MatchHud
    {
        private static readonly Color PanelColor = new Color(0.04f, 0.06f, 0.10f, 0.78f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        private static readonly string[] Places = { "", "1st", "2nd", "3rd", "4th" };

        private readonly MatchSimulation _sim;
        private readonly int _localSeat;
        private readonly int[] _characters;
        private readonly RenderTexture[] _portraits;
        private readonly Canvas _canvas;
        private readonly SeatPanel[] _panels = new SeatPanel[SeatFrame.SeatCount];
        private readonly Text _clock;
        private readonly Text _banner;
        private readonly Image _blastFill;
        private readonly Text _blastLabel;
        private readonly RectTransform _pauseScreen;
        private readonly RectTransform _resultsScreen;
        private readonly Text _resultsTitle;
        private readonly Text[] _resultLines = new Text[SeatFrame.SeatCount];
        private readonly RawImage[] _resultPortraits = new RawImage[SeatFrame.SeatCount];
        private float _bannerTime;
        private float _bannerDuration;

        public event Action PauseClicked;
        public event Action RestartClicked;
        public event Action BlastPressed;

        public MatchHud(MatchSimulation sim, int localSeat, int[] characters, RenderTexture[] portraits)
        {
            _sim = sim;
            _localSeat = localSeat;
            _characters = characters;
            _portraits = portraits;
            _canvas = UiFactory.CreateCanvas("Match HUD", 10);
            var root = _canvas.transform;

            // Player panels: left, far and right opponents across the top; you at the bottom left.
            int left = (localSeat + 3) % 4, far = (localSeat + 2) % 4, right = (localSeat + 1) % 4;
            _panels[left] = new SeatPanel(this, root, left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f));
            _panels[far] = new SeatPanel(this, root, far, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -20f));
            _panels[right] = new SeatPanel(this, root, right, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-130f, -20f));
            _panels[localSeat] = new SeatPanel(this, root, localSeat, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f));

            // Clock beside the far panel, at top centre.
            var clockRect = UiFactory.Rect("Clock", root, new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(12f, -20f), new Vector2(260f, 110f));
            UiFactory.Image(clockRect, PanelColor);
            _clock = UiFactory.Text(UiFactory.Stretch("Time", clockRect), "3:00", 64, Color.white);

            _banner = UiFactory.Text(UiFactory.Rect("Banner", root, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 200f)),
                "", 120, Color.white);

            // Pause button (top-right corner).
            UiFactory.Button(root, "II", new Vector2(1f, 1f), new Vector2(-62f, -62f), new Vector2(84f, 84f), PanelColor, () => PauseClicked?.Invoke());

            // Blast button with a cooldown ring (bottom-right), fires on touch-down.
            var blast = UiFactory.Rect("Blast", root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(230f, 230f));
            var blastBg = UiFactory.Image(blast, new Color(0.05f, 0.08f, 0.14f, 0.7f), ProceduralTextures.Circle());
            _blastFill = UiFactory.Image(UiFactory.Stretch("Fill", blast), SimSpace.SeatColors[localSeat], ProceduralTextures.Circle());
            _blastFill.type = Image.Type.Filled;
            _blastFill.fillMethod = Image.FillMethod.Radial360;
            _blastFill.raycastTarget = false;
            var inner = UiFactory.Rect("Inner", blast, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 190f));
            UiFactory.Image(inner, new Color(0.05f, 0.08f, 0.14f, 0.92f), ProceduralTextures.Circle()).raycastTarget = false;
            _blastLabel = UiFactory.Text(UiFactory.Stretch("Label", blast), "BLAST", 40, Color.white);
            blastBg.gameObject.AddComponent<PressButton>().Pressed += () => BlastPressed?.Invoke();

            // Pause screen.
            _pauseScreen = UiFactory.Stretch("Pause Screen", root);
            UiFactory.Image(_pauseScreen, new Color(0f, 0f, 0f, 0.6f));
            UiFactory.Text(UiFactory.Rect("Title", _pauseScreen, new Vector2(0.5f, 0.65f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 160f)), "PAUSED", 110, Color.white);
            UiFactory.Button(_pauseScreen, "Resume", new Vector2(0.5f, 0.45f), Vector2.zero, new Vector2(420f, 100f), new Color(0.1f, 0.55f, 0.75f), () => PauseClicked?.Invoke());
            UiFactory.Button(_pauseScreen, "Restart", new Vector2(0.5f, 0.32f), Vector2.zero, new Vector2(420f, 100f), new Color(0.25f, 0.28f, 0.35f), () => RestartClicked?.Invoke());
            _pauseScreen.gameObject.SetActive(false);

            // Results screen.
            _resultsScreen = UiFactory.Stretch("Results Screen", root);
            UiFactory.Image(_resultsScreen, new Color(0f, 0f, 0f, 0.7f));
            _resultsTitle = UiFactory.Text(UiFactory.Rect("Title", _resultsScreen, new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200f, 160f)), "", 110, Gold);
            for (int i = 0; i < SeatFrame.SeatCount; i++)
            {
                var row = UiFactory.Rect($"Row {i}", _resultsScreen, new Vector2(0.5f, 0.66f), new Vector2(0.5f, 0.5f), new Vector2(0f, -i * 120f), new Vector2(1100f, 108f));
                UiFactory.Image(row, PanelColor);
                var portrait = UiFactory.Rect("Portrait", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(96f, 96f));
                _resultPortraits[i] = portrait.gameObject.AddComponent<RawImage>();
                _resultLines[i] = UiFactory.Text(UiFactory.Rect("Line", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(124f, 0f), new Vector2(960f, 100f)),
                    "", 40, Color.white, TextAnchor.MiddleLeft);
            }
            UiFactory.Button(_resultsScreen, "Rematch", new Vector2(0.5f, 0.12f), Vector2.zero, new Vector2(420f, 100f), new Color(0.1f, 0.55f, 0.75f), () => RestartClicked?.Invoke());
            _resultsScreen.gameObject.SetActive(false);

            for (int i = 0; i < SeatFrame.SeatCount; i++)
                if (sim.State.Seats[i].Status == SeatStatus.Closed) _panels[i].Root.gameObject.SetActive(false);
        }

        public void Destroy() => UnityEngine.Object.Destroy(_canvas.gameObject);

        public string Label(int seat) => seat == _localSeat ? "You" : Characters.Names[_characters[seat]];

        public void ShowBanner(string text, Color color, float seconds = 1.4f)
        {
            _banner.text = text;
            _banner.color = color;
            _bannerTime = _bannerDuration = seconds;
        }

        public void OnGoal(int seat) => _panels[seat].Shake();

        public void Update(MatchState state, float deltaTime)
        {
            bool elimination = _sim.Settings.Mode == GameMode.Elimination;
            for (int i = 0; i < SeatFrame.SeatCount; i++)
                if (state.Seats[i].Status != SeatStatus.Closed) _panels[i].Update(state.Seats[i], elimination, deltaTime);

            // Clock.
            if (_sim.Settings.Mode == GameMode.Timed)
            {
                _clock.text = state.SuddenDeath ? "SUDDEN\nDEATH" : TimeSpan.FromSeconds(Mathf.Ceil(state.ClockRemaining)).ToString(@"m\:ss");
                _clock.fontSize = state.SuddenDeath ? 34 : 64;
                bool urgent = state.FinalCountdownRaised || state.SuddenDeath;
                _clock.color = urgent ? Color.Lerp(new Color(1f, 0.3f, 0.25f), Color.white, Mathf.PingPong(Time.time * 2f, 1f) * 0.4f) : Color.white;
            }
            else
            {
                _clock.text = elimination ? $"{state.ActiveSeatCount} LEFT" : "PRACTICE";
                _clock.fontSize = 44;
            }

            // Countdown takes over the banner.
            if (state.Phase == MatchPhase.Countdown)
            {
                int n = Mathf.CeilToInt(state.CountdownRemaining);
                _banner.text = n > 0 ? n.ToString() : "GO!";
                _banner.color = Gold;
                float frac = state.CountdownRemaining - Mathf.Floor(state.CountdownRemaining);
                _banner.rectTransform.localScale = Vector3.one * (1f + frac * 0.5f);
                _bannerTime = 0.4f;
                _bannerDuration = 0.4f;
            }
            else if (_bannerTime > 0f)
            {
                _bannerTime -= deltaTime;
                float t = 1f - _bannerTime / _bannerDuration;
                _banner.rectTransform.localScale = Vector3.one * (t < 0.15f ? Mathf.Lerp(1.6f, 1f, t / 0.15f) : 1f);
                var c = _banner.color;
                c.a = Mathf.Clamp01(_bannerTime / 0.3f);
                _banner.color = c;
                if (_bannerTime <= 0f) _banner.text = "";
            }

            // Blast button: ring fills as the cooldown recovers.
            var me = state.Seats[_localSeat];
            float ready = 1f - Mathf.Clamp01(me.BlastCooldown / _sim.Config.BlastCooldown);
            _blastFill.fillAmount = ready;
            _blastFill.color = ready >= 1f ? SimSpace.SeatColors[_localSeat] : SimSpace.SeatColors[_localSeat] * 0.5f;
            _blastLabel.color = ready >= 1f ? Color.white : new Color(1f, 1f, 1f, 0.45f);

            _pauseScreen.gameObject.SetActive(state.Phase == MatchPhase.Paused);
            bool ended = state.Phase == MatchPhase.Ended;
            if (ended && !_resultsScreen.gameObject.activeSelf) ShowResults(state);
            if (!ended && _resultsScreen.gameObject.activeSelf) _resultsScreen.gameObject.SetActive(false);
        }

        private void ShowResults(MatchState state)
        {
            _resultsScreen.gameObject.SetActive(true);
            _banner.text = "";
            _resultsTitle.text = state.IsDraw ? "DRAW!" : state.WinnerSeat == _localSeat ? "YOU WIN!" : $"{Label(state.WinnerSeat).ToUpperInvariant()} WINS!";

            int row = 0;
            for (int place = 1; place <= SeatFrame.SeatCount; place++)
            for (int seat = 0; seat < SeatFrame.SeatCount; seat++)
            {
                var s = state.Seats[seat];
                if (s.Placement != place) continue;
                _resultPortraits[row].texture = _portraits[_characters[seat]];
                _resultLines[row].text = $"{Places[place]}   {Label(seat)}   -   {s.GoalsAgainst} conceded, {s.Saves} saves, {s.Blasts} blasts";
                _resultLines[row].color = place == 1 ? Gold : Color.white;
                _resultLines[row].transform.parent.gameObject.SetActive(true);
                row++;
            }
            for (; row < SeatFrame.SeatCount; row++) _resultLines[row].transform.parent.gameObject.SetActive(false);
        }

        /// <summary>One player's portrait, name and score.</summary>
        private sealed class SeatPanel
        {
            private const float ShakeSeconds = 0.45f;
            private readonly Text _score;
            private readonly Text _status;
            private readonly Image _frame;
            private readonly RawImage _portrait;
            private readonly Vector2 _home;
            private readonly Color _color;
            private int _lastValue = int.MinValue;
            private float _shake;

            public RectTransform Root { get; }

            public SeatPanel(MatchHud hud, Transform parent, int seat, Vector2 anchor, Vector2 pivot, Vector2 position)
            {
                _color = SimSpace.SeatColors[seat];
                bool local = seat == hud._localSeat;
                Root = UiFactory.Rect($"Panel {SimSpace.SeatNames[seat]}", parent, anchor, pivot, position, new Vector2(local ? 380f : 330f, 110f));
                UiFactory.Image(Root, PanelColor);
                _home = Root.anchoredPosition;

                var ring = UiFactory.Rect("Ring", Root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(98f, 98f));
                _frame = UiFactory.Image(ring, _color, ProceduralTextures.Circle());
                var mask = UiFactory.Rect("Mask", ring, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(86f, 86f));
                UiFactory.Image(mask, Color.white, ProceduralTextures.Circle());
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                _portrait = UiFactory.Stretch("Portrait", mask).gameObject.AddComponent<RawImage>();
                _portrait.texture = hud._portraits[hud._characters[seat]];
                _portrait.raycastTarget = false;

                UiFactory.Text(UiFactory.Rect("Name", Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(116f, -8f), new Vector2(200f, 36f)),
                    local ? $"YOU ({Characters.Names[hud._characters[seat]]})" : Characters.Names[hud._characters[seat]], 26, _color, TextAnchor.MiddleLeft);
                _status = UiFactory.Text(UiFactory.Rect("Status", Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(116f, 8f), new Vector2(200f, 30f)),
                    "", 22, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);
                _score = UiFactory.Text(UiFactory.Rect("Score", Root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(110f, 100f)),
                    "0", 72, Color.white, TextAnchor.MiddleRight);
            }

            public void Shake() => _shake = ShakeSeconds;

            public void Update(SeatState s, bool elimination, float dt)
            {
                int value = elimination ? s.Lives : s.GoalsAgainst;
                if (value != _lastValue)
                {
                    _score.text = value.ToString();
                    _lastValue = value;
                }

                bool out_ = s.Status == SeatStatus.Eliminated;
                _status.text = out_ ? "KNOCKED OUT" : s.Status == SeatStatus.Spectating ? "WATCHING" : elimination ? "lives" : "conceded";
                _portrait.color = out_ ? new Color(0.35f, 0.35f, 0.35f) : Color.white;
                _frame.color = out_ ? Color.gray : _color;

                if (_shake > 0f)
                {
                    _shake = Mathf.Max(0f, _shake - dt);
                    float t = _shake / ShakeSeconds;
                    Root.anchoredPosition = _home + new Vector2(Mathf.Sin(t * 60f) * 14f * t, 0f);
                    _score.color = Color.Lerp(Color.white, new Color(1f, 0.3f, 0.25f), t);
                    _score.transform.localScale = Vector3.one * (1f + 0.4f * t);
                }
                else
                {
                    Root.anchoredPosition = _home;
                }
            }
        }
    }
}

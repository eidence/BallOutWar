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
        private readonly Image _blastCore;
        private float _blastReadyTime;
        private readonly RectTransform _pauseScreen;
        private readonly RectTransform _resultsScreen;
        private readonly RectTransform _quitConfirm;
        private readonly Text _resultsTitle;
        private readonly Text[] _resultLines = new Text[SeatFrame.SeatCount];
        private readonly RawImage[] _resultPortraits = new RawImage[SeatFrame.SeatCount];
        private float _bannerTime;
        private float _bannerDuration;

        public event Action PauseClicked;
        public event Action RestartClicked;
        public event Action QuitClicked;
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
            _panels[left] = new SeatPanel(this, root, left, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -22f));
            _panels[far] = new SeatPanel(this, root, far, new Vector2(0.5f, 1f), new Vector2(1f, 1f), new Vector2(-160f, -22f));
            _panels[right] = new SeatPanel(this, root, right, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-150f, -22f));
            _panels[localSeat] = new SeatPanel(this, root, localSeat, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(50f, 28f));

            // Clock in its hexagonal frame at top centre.
            var frame = HudSprites.TimerFrame;
            var clockRect = UiFactory.Rect("Clock", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f),
                new Vector2(140f * HudSprites.Aspect(frame), 140f));
            UiFactory.Image(clockRect, Color.white, frame).raycastTarget = false;
            _clock = UiFactory.Text(UiFactory.Stretch("Time", clockRect), "3:00", 64, Color.white);

            _banner = UiFactory.Text(UiFactory.Rect("Banner", root, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 200f)),
                "", 120, Color.white);

            // Pause button (top-right corner).
            var pause = UiFactory.Button(root, "", new Vector2(1f, 1f), new Vector2(-70f, -70f), new Vector2(104f, 104f), Color.white, () => PauseClicked?.Invoke());
            ((Image)pause.targetGraphic).sprite = HudSprites.Pause;
            pause.name = "Pause";

            // Blast button (bottom-right), fires on touch-down. The ring's segments light up clockwise as the
            // cooldown recovers, and the starburst core appears when the blast is ready.
            var blast = UiFactory.Rect("Blast", root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 32f), new Vector2(270f, 270f));
            var blastBg = UiFactory.Image(blast, Color.white, HudSprites.BlastEmpty);
            _blastFill = UiFactory.Image(UiFactory.Stretch("Fill", blast), Color.white, HudSprites.BlastFill);
            _blastFill.type = Image.Type.Filled;
            _blastFill.fillMethod = Image.FillMethod.Radial360;
            _blastFill.fillOrigin = (int)Image.Origin360.Top;
            _blastFill.fillClockwise = true;
            _blastFill.raycastTarget = false;
            var core = UiFactory.Rect("Core", blast, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                Vector2.one * 270f * HudSprites.BlastCoreScale);
            _blastCore = UiFactory.Image(core, Color.white, HudSprites.BlastCore);
            _blastCore.raycastTarget = false;
            blastBg.gameObject.AddComponent<PressButton>().Pressed += () => BlastPressed?.Invoke();

            // Pause screen.
            _pauseScreen = UiFactory.Stretch("Pause Screen", root);
            UiFactory.Image(_pauseScreen, new Color(0f, 0f, 0f, 0.6f));
            UiFactory.Text(UiFactory.Rect("Title", _pauseScreen, new Vector2(0.5f, 0.65f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800f, 160f)), "PAUSED", 110, Color.white);
            UiFactory.Button(_pauseScreen, "Resume", new Vector2(0.5f, 0.45f), Vector2.zero, new Vector2(420f, 100f), new Color(0.1f, 0.55f, 0.75f), () => PauseClicked?.Invoke());
            UiFactory.Button(_pauseScreen, "Restart", new Vector2(0.5f, 0.32f), Vector2.zero, new Vector2(420f, 100f), new Color(0.25f, 0.28f, 0.35f), () => RestartClicked?.Invoke());
            UiFactory.Button(_pauseScreen, "Quit to menu", new Vector2(0.5f, 0.19f), Vector2.zero, new Vector2(420f, 100f), new Color(0.25f, 0.28f, 0.35f), () => _quitConfirm.gameObject.SetActive(true));
            _pauseScreen.gameObject.SetActive(false);

            // Quit confirmation (US-6.4).
            _quitConfirm = UiFactory.Stretch("Quit Confirm", root);
            UiFactory.Image(_quitConfirm, new Color(0f, 0f, 0f, 0.6f));
            var box = UiFactory.Rect("Box", _quitConfirm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 420f));
            UiFactory.Image(box, PanelColor);
            UiFactory.Text(UiFactory.Rect("Question", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(900f, 160f)),
                "Leave the match?\nIt will count as a loss.", 52, Color.white);
            UiFactory.Button(box, "Leave", new Vector2(0.5f, 0f), new Vector2(-190f, 100f), new Vector2(320f, 100f), new Color(0.7f, 0.2f, 0.2f), () => QuitClicked?.Invoke());
            UiFactory.Button(box, "Stay", new Vector2(0.5f, 0f), new Vector2(190f, 100f), new Vector2(320f, 100f), new Color(0.25f, 0.28f, 0.35f), () => _quitConfirm.gameObject.SetActive(false));
            _quitConfirm.gameObject.SetActive(false);

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
            UiFactory.Button(_resultsScreen, "Rematch", new Vector2(0.5f, 0.12f), new Vector2(-230f, 0f), new Vector2(420f, 100f), new Color(0.1f, 0.55f, 0.75f), () => RestartClicked?.Invoke());
            UiFactory.Button(_resultsScreen, "Main menu", new Vector2(0.5f, 0.12f), new Vector2(230f, 0f), new Vector2(420f, 100f), new Color(0.25f, 0.28f, 0.35f), () => QuitClicked?.Invoke());
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

            // Blast button: segments light up as the cooldown recovers; the core pops in, then pulses, when ready.
            var me = state.Seats[_localSeat];
            float ready = 1f - Mathf.Clamp01(me.BlastCooldown / _sim.Config.BlastCooldown);
            _blastFill.fillAmount = ready;
            if (ready >= 1f)
            {
                _blastReadyTime += deltaTime;
                float pop = Mathf.Clamp01(_blastReadyTime / 0.15f);
                float pulse = 1f + 0.04f * Mathf.Sin(Time.time * 6f);
                _blastCore.color = new Color(1f, 1f, 1f, pop);
                _blastCore.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, pop) * pulse;
                _blastFill.color = Color.white;
            }
            else
            {
                _blastReadyTime = 0f;
                _blastCore.color = new Color(1f, 1f, 1f, 0f);
                _blastFill.color = new Color(0.75f, 0.85f, 0.9f, 0.85f);
            }

            bool paused = state.Phase == MatchPhase.Paused;
            _pauseScreen.gameObject.SetActive(paused);
            if (!paused && _quitConfirm.gameObject.activeSelf) _quitConfirm.gameObject.SetActive(false);
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

        /// <summary>One player's slanted panel: portrait ring, name, score, and life pips in Elimination.</summary>
        private sealed class SeatPanel
        {
            private const float ShakeSeconds = 0.45f;
            private const int MaxPips = 10;
            private readonly Text _score;
            private readonly Text _status;
            private readonly Image _ring;
            private readonly Image _panel;
            private readonly RawImage _portrait;
            private readonly Image[] _pips;
            private readonly Vector2 _home;
            private readonly Color _color;
            private readonly Color _ringTint;
            private readonly Color _panelTint;
            private int _lastValue = int.MinValue;
            private float _shake;

            public RectTransform Root { get; }

            public SeatPanel(MatchHud hud, Transform parent, int seat, Vector2 anchor, Vector2 pivot, Vector2 position)
            {
                _color = SimSpace.SeatColors[seat];
                _panelTint = Color.Lerp(Color.white, _color, 0.65f);
                _ringTint = Color.Lerp(Color.white, _color, 0.45f);
                bool local = seat == hud._localSeat;
                float height = local ? 136f : 116f;
                var sprite = HudSprites.Panel;
                Root = UiFactory.Rect($"Panel {SimSpace.SeatNames[seat]}", parent, anchor, pivot, position,
                    new Vector2(height * HudSprites.Aspect(sprite), height));
                _panel = UiFactory.Image(Root, _panelTint, sprite);
                _panel.raycastTarget = false;
                _home = Root.anchoredPosition;

                // Portrait ring overhangs the panel's left end.
                float ringSize = height + 16f;
                var ring = UiFactory.Rect("Ring", Root, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(ringSize * 0.32f, 0f), Vector2.one * ringSize);
                var mask = UiFactory.Rect("Mask", ring, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * ringSize * 0.8f);
                UiFactory.Image(mask, Color.white, ProceduralTextures.Circle());
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                _portrait = UiFactory.Stretch("Portrait", mask).gameObject.AddComponent<RawImage>();
                _portrait.texture = hud._portraits[hud._characters[seat]];
                _portrait.raycastTarget = false;
                _ring = UiFactory.Image(UiFactory.Stretch("Frame", ring), _ringTint, HudSprites.PortraitRing);
                _ring.raycastTarget = false;

                float textLeft = ringSize * 0.32f + ringSize * 0.5f + 8f;
                UiFactory.Text(UiFactory.Rect("Name", Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(textLeft, -12f), new Vector2(200f, 36f)),
                    local ? $"YOU ({Characters.Names[hud._characters[seat]]})" : Characters.Names[hud._characters[seat]].ToUpperInvariant(),
                    local ? 28 : 26, Color.Lerp(_color, Color.white, 0.35f), TextAnchor.MiddleLeft);
                _status = UiFactory.Text(UiFactory.Rect("Status", Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(textLeft - 6f, 12f), new Vector2(200f, 30f)),
                    "", 22, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
                _score = UiFactory.Text(UiFactory.Rect("Score", Root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-height * 0.42f, 0f), new Vector2(110f, 100f)),
                    "0", local ? 76 : 66, Color.white, TextAnchor.MiddleRight);

                // Life pips in Elimination (when there are few enough to fit).
                int lives = hud._sim.Settings.GoalsToEliminate;
                if (hud._sim.Settings.Mode == GameMode.Elimination && lives <= MaxPips)
                {
                    _pips = new Image[lives];
                    float width = Mathf.Min(30f, 170f / lives - 3f);
                    float pipHeight = width / HudSprites.Aspect(HudSprites.PipFull);
                    for (int i = 0; i < lives; i++)
                    {
                        var pip = UiFactory.Rect($"Pip {i}", Root, new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                            new Vector2(textLeft - 8f + i * (width + 3f), 30f), new Vector2(width, pipHeight));
                        _pips[i] = UiFactory.Image(pip, Color.white, HudSprites.PipFull);
                        _pips[i].raycastTarget = false;
                    }
                }
            }

            public void Shake() => _shake = ShakeSeconds;

            public void Update(SeatState s, bool elimination, float dt)
            {
                int value = elimination ? s.Lives : s.GoalsAgainst;
                if (value != _lastValue)
                {
                    _score.text = value.ToString();
                    _lastValue = value;
                    if (_pips != null)
                        for (int i = 0; i < _pips.Length; i++)
                        {
                            bool full = i < value;
                            _pips[i].sprite = full ? HudSprites.PipFull : HudSprites.PipEmpty;
                            _pips[i].color = full ? Color.Lerp(Color.white, _color, 0.5f) : Color.white;
                        }
                }

                bool out_ = s.Status == SeatStatus.Eliminated;
                bool showPips = _pips != null && !out_ && s.Status != SeatStatus.Spectating;
                _status.text = out_ ? "KNOCKED OUT" : s.Status == SeatStatus.Spectating ? "WATCHING" : showPips ? "" : elimination ? "lives" : "conceded";
                if (_pips != null)
                    foreach (var pip in _pips) pip.enabled = showPips;
                _portrait.color = out_ ? new Color(0.35f, 0.35f, 0.35f) : Color.white;
                _ring.color = out_ ? Color.gray : _ringTint;
                _panel.color = out_ ? new Color(0.5f, 0.5f, 0.55f, 0.8f) : _panelTint;

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

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
        public enum Role { Offline, Host, Guest }

        private MenuButton _restartButton, _quitButton, _rematchButton, _menuButton;
        private RectTransform _pauseBox;
        private Text _quitQuestion, _quitDetail;
        private Role _role;
        private readonly string[] _names;

        private readonly Image _blastFill;
        private readonly Image _blastCore;
        private float _blastReadyTime;
        private readonly RectTransform _pauseScreen;
        private readonly RectTransform _resultsScreen;
        private readonly RectTransform _quitConfirm;
        private readonly Text _resultsTitle;
        private readonly Text[] _resultLines = new Text[SeatFrame.SeatCount];
        private readonly RawImage[] _resultPortraits = new RawImage[SeatFrame.SeatCount];
        private readonly RectTransform[] _resultRows = new RectTransform[SeatFrame.SeatCount];
        private readonly Image[] _resultRims = new Image[SeatFrame.SeatCount];
        private readonly Image[] _resultStripes = new Image[SeatFrame.SeatCount];
        private readonly Image[] _resultRings = new Image[SeatFrame.SeatCount];
        private readonly Text[] _resultPlaces = new Text[SeatFrame.SeatCount];
        private readonly Text[] _resultStats = new Text[SeatFrame.SeatCount];
        private float _bannerTime;
        private float _bannerDuration;

        public event Action PauseClicked;
        public event Action RestartClicked;
        public event Action QuitClicked;
        public event Action BlastPressed;

        // The top bar: four score panels and the clock, sized to leave the pause button its corner on a 16:9 screen.
        private const float PanelHeight = 104f, ClockHeight = 120f, PanelGap = 36f;

        /// <param name="names">Player names by seat for LAN matches; null entries (and offline play) show the character name.</param>
        public MatchHud(MatchSimulation sim, int localSeat, int[] characters, RenderTexture[] portraits, string[] names = null)
        {
            _names = names;
            _sim = sim;
            _localSeat = localSeat;
            _characters = characters;
            _portraits = portraits;
            _canvas = UiFactory.CreateCanvas("Match HUD", 10);
            var root = _canvas.transform;

            // Clock in its hexagonal frame at top centre, with all four scores in one row beside it: you first, then
            // the others clockwise round the table (left, far, right), two each side of the clock. The right-hand pair
            // is mirrored, so every panel slants in towards the clock and the portraits sit at the two ends.
            var frame = HudSprites.TimerFrame;
            float clockWidth = ClockHeight * HudSprites.Aspect(frame);
            float panelWidth = PanelHeight * HudSprites.Aspect(HudSprites.Panel);
            float inner = clockWidth * 0.5f + PanelGap + panelWidth * 0.5f, outer = inner + panelWidth + PanelGap;
            int[] order = { localSeat, (localSeat + 3) % 4, (localSeat + 2) % 4, (localSeat + 1) % 4 };
            float[] xs = { -outer, -inner, inner, outer };
            var topCentre = new Vector2(0.5f, 1f);
            for (int i = 0; i < order.Length; i++)
                _panels[order[i]] = new SeatPanel(this, root, order[i], PanelHeight, topCentre, topCentre, new Vector2(xs[i], -18f), mirrored: xs[i] > 0f);
            bool leftHanded = SettingsStore.Current.LeftHanded; // the blast button swaps sides for left-handed players (US-7.5)

            var clockRect = UiFactory.Rect("Clock", root, topCentre, topCentre, new Vector2(0f, -14f), new Vector2(clockWidth, ClockHeight));
            UiFactory.Image(clockRect, Color.white, frame).raycastTarget = false;
            _clock = Numerals(UiFactory.Text(UiFactory.Stretch("Time", clockRect), "3:00", 64, Color.white, outline: false), new Color(0.55f, 0.85f, 1f));

            // Big call-outs (OUCH!, countdown, GO!): the HUD's numeral style, heavier, shaded darker toward the bottom.
            _banner = Numerals(UiFactory.Text(UiFactory.Rect("Banner", root, new Vector2(0.5f, 0.6f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 200f)),
                "", 120, Color.white, outline: false), new Color(0.62f, 0.62f, 0.7f), 2.6f);

            // Pause button (top-right corner).
            var pause = UiFactory.Button(root, "", new Vector2(1f, 1f), new Vector2(-70f, -70f), new Vector2(104f, 104f), Color.white, () => PauseClicked?.Invoke());
            ((Image)pause.targetGraphic).sprite = HudSprites.Pause;
            pause.name = "Pause";

            // Blast button (bottom-right), fires on touch-down. The ring's segments light up clockwise as the
            // cooldown recovers, and the starburst core appears when the blast is ready.
            var blastCorner = leftHanded ? new Vector2(0f, 0f) : new Vector2(1f, 0f);
            var blast = UiFactory.Rect("Blast", root, blastCorner, blastCorner, new Vector2(leftHanded ? 36f : -36f, 32f), new Vector2(270f, 270f));
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

            // Pause screen: a framed panel with the three choices stacked, Resume the gold one.
            _pauseScreen = UiFactory.Stretch("Pause Screen", root);
            _pauseBox = MenuStyle.Dialog(_pauseScreen, new Vector2(720f, 540f), MenuStyle.Cyan, "PAUSED", Color.white, 80);
            var top = new Vector2(0.5f, 1f);
            MenuStyle.Make(_pauseBox, "Resume", MenuIcon.Play, MenuStyle.Kind.Primary, top, new Vector2(0f, -200f), new Vector2(480f, 104f), () => PauseClicked?.Invoke());
            _restartButton = MenuStyle.Make(_pauseBox, "Restart", null, MenuStyle.Kind.Secondary, top, new Vector2(0f, -320f), new Vector2(480f, 92f), () => RestartClicked?.Invoke());
            _quitButton = MenuStyle.Make(_pauseBox, "Quit to menu", MenuIcon.Exit, MenuStyle.Kind.Quiet, top, new Vector2(0f, -430f), new Vector2(480f, 92f), () => _quitConfirm.gameObject.SetActive(true));
            _pauseScreen.gameObject.SetActive(false);

            // Quit confirmation (US-6.4).
            _quitConfirm = UiFactory.Stretch("Quit Confirm", root);
            UiFactory.Image(_quitConfirm, new Color(0.01f, 0.02f, 0.05f, 0.6f));
            var box = UiFactory.Rect("Box", _quitConfirm, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960f, 440f));
            MenuStyle.Panel(box, MenuStyle.Red);
            _quitQuestion = MenuStyle.Heading(UiFactory.Rect("Question", box, top, top, new Vector2(0f, -40f), new Vector2(880f, 80f)),
                "Leave the match?", 56, Color.white);
            _quitDetail = UiFactory.Text(UiFactory.Rect("Detail", box, top, top, new Vector2(0f, -124f), new Vector2(880f, 60f)),
                "It will count as a loss.", 32, MenuStyle.TextMuted, TextAnchor.MiddleCenter, outline: false);
            var bottom = new Vector2(0.5f, 0f);
            MenuStyle.Make(box, "Leave", MenuIcon.Exit, MenuStyle.Kind.Danger, bottom, new Vector2(-190f, 100f), new Vector2(330f, 100f), () => QuitClicked?.Invoke());
            MenuStyle.Make(box, "Stay", null, MenuStyle.Kind.Secondary, bottom, new Vector2(190f, 100f), new Vector2(330f, 100f), () => _quitConfirm.gameObject.SetActive(false));
            _quitConfirm.gameObject.SetActive(false);

            // Results screen: the headline, then one card per player in finishing order.
            _resultsScreen = UiFactory.Stretch("Results Screen", root);
            UiFactory.Image(_resultsScreen, new Color(0.01f, 0.02f, 0.05f, 0.72f));
            _resultsTitle = MenuStyle.Heading(UiFactory.Rect("Title", _resultsScreen, new Vector2(0.5f, 0.84f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 200f)),
                "", 110, Gold);
            for (int i = 0; i < SeatFrame.SeatCount; i++)
            {
                var row = UiFactory.Rect($"Row {i}", _resultsScreen, new Vector2(0.5f, 0.67f), new Vector2(0.5f, 0.5f), new Vector2(0f, -i * 122f), new Vector2(1160f, 108f));
                _resultRows[i] = row;
                MenuStyle.Recess(row, new Color(0.03f, 0.05f, 0.1f, 0.92f), 1.2f).raycastTarget = false;
                _resultRims[i] = MenuStyle.Outline(row, Color.white, 1.2f);
                var stripe = UiFactory.Rect("Colour", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(10f, 84f));
                _resultStripes[i] = MenuStyle.Recess(stripe, Color.white, 6f);
                _resultStripes[i].raycastTarget = false;
                _resultPlaces[i] = MenuStyle.Heading(UiFactory.Rect("Place", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(110f, 90f)),
                    "", 48, Color.white, TextAnchor.MiddleLeft);
                var ring = UiFactory.Rect("Ring", row, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(200f, 0f), new Vector2(92f, 92f));
                _resultRings[i] = MenuStyle.PortraitFrame(ring, null, Color.white, out _resultPortraits[i]);
                _resultLines[i] = FitText(MenuStyle.Heading(UiFactory.Rect("Name", row, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(270f, 0f), new Vector2(360f, 80f)),
                    "", 44, Color.white, TextAnchor.MiddleLeft));
                _resultStats[i] = UiFactory.Text(UiFactory.Rect("Stats", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-34f, 0f), new Vector2(500f, 80f)),
                    "", 28, MenuStyle.TextMuted, TextAnchor.MiddleRight, outline: false);
            }
            _rematchButton = MenuStyle.Make(_resultsScreen, "Rematch", MenuIcon.Play, MenuStyle.Kind.Primary, new Vector2(0.5f, 0.1f), new Vector2(-240f, 0f), new Vector2(440f, 108f), () => RestartClicked?.Invoke());
            _menuButton = MenuStyle.Make(_resultsScreen, "Main menu", MenuIcon.Back, MenuStyle.Kind.Quiet, new Vector2(0.5f, 0.1f), new Vector2(240f, 0f), new Vector2(440f, 100f), () => QuitClicked?.Invoke());
            _resultsScreen.gameObject.SetActive(false);

            for (int i = 0; i < SeatFrame.SeatCount; i++)
                if (sim.State.Seats[i].Status == SeatStatus.Closed) _panels[i].Root.gameObject.SetActive(false);
        }

        /// <summary>Sets the pause, quit and results buttons for offline play, a LAN host or a LAN guest.</summary>
        public void ConfigureFor(Role role)
        {
            _role = role;
            bool offline = role == Role.Offline;
            _restartButton.gameObject.SetActive(offline);
            if (!offline)
            {
                // No Restart online: Quit moves up into its place and the panel closes up.
                ((RectTransform)_quitButton.transform).anchoredPosition = new Vector2(0f, -320f);
                _pauseBox.sizeDelta = new Vector2(_pauseBox.sizeDelta.x, 430f);
            }
            switch (role)
            {
                case Role.Host:
                    _quitButton.Label.text = "End match";
                    _quitQuestion.text = "End the match for everyone?";
                    _quitDetail.text = "Everyone goes back to the lobby.";
                    _menuButton.Label.text = "Back to lobby";
                    break;
                case Role.Guest:
                    _quitButton.Label.text = "Leave match";
                    _quitDetail.text = "An AI will take your place.";
                    _rematchButton.gameObject.SetActive(false);
                    _menuButton.Label.text = "Leave";
                    ((RectTransform)_menuButton.transform).anchoredPosition = Vector2.zero;
                    break;
            }
        }

        public void Destroy() => UnityEngine.Object.Destroy(_canvas.gameObject);

        public string Label(int seat) => seat == _localSeat ? "You" : _names?[seat] ?? Characters.Names[_characters[seat]];

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
            if (_role == Role.Guest) _resultsTitle.text += "\n<size=36><color=#B8CCE6>Waiting for the host: rematch or lobby</color></size>";

            int row = 0;
            for (int place = 1; place <= SeatFrame.SeatCount; place++)
            for (int seat = 0; seat < SeatFrame.SeatCount; seat++)
            {
                var s = state.Seats[seat];
                if (s.Placement != place) continue;
                bool first = place == 1;
                var placeColor = first ? Gold : MenuStyle.TextMuted;
                _resultPortraits[row].texture = _portraits[_characters[seat]];
                _resultPlaces[row].text = Places[place];
                _resultPlaces[row].color = placeColor;
                _resultLines[row].text = Label(seat);
                _resultLines[row].color = first ? Gold : Color.white;
                _resultStats[row].text = $"{s.GoalsAgainst} conceded   {s.Saves} saves   {s.Blasts} blasts";
                _resultRims[row].color = first ? Gold : new Color(0.35f, 0.5f, 0.68f, 0.35f);
                _resultRings[row].color = first ? Gold : MenuStyle.RingColor(false);
                _resultStripes[row].color = SimSpace.SeatColors[seat];
                _resultRows[row].gameObject.SetActive(true);
                row++;
            }
            for (; row < SeatFrame.SeatCount; row++) _resultRows[row].gameObject.SetActive(false);
        }

        /// <summary>Keeps text inside its box: it shrinks a long name or a big score instead of spilling over the frame.</summary>
        /// <summary>
        /// The style for the HUD's numbers (scores and clock): bold italic, shaded from white down to
        /// <paramref name="bottom"/>, with a dark edge and a drop shadow so they read over the arena.
        /// </summary>
        private static Text Numerals(Text text, Color bottom, float weight = 1f)
        {
            UiFactory.UseDisplayFont(text);
            var go = text.gameObject;
            go.AddComponent<TextGradient>().Bottom = bottom;
            var edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(0.02f, 0.03f, 0.08f, 0.95f);
            edge.effectDistance = new Vector2(2.5f, -2.5f) * weight;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -6f) * weight;
            return text;
        }

        private static Text FitText(Text text)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = text.fontSize;
            text.resizeTextMinSize = Mathf.Max(10, text.fontSize / 3);
            return text;
        }

        /// <summary>One player's slanted panel: portrait ring, name, score, and life pips in Elimination.</summary>
        private sealed class SeatPanel
        {
            private const float ShakeSeconds = 0.45f;
            private const int MaxPips = 10;
            private readonly Text _score;
            private readonly Text _status;
            private readonly RectTransform _name;
            private readonly Vector2 _nameCentred, _nameRaised;
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

            public SeatPanel(MatchHud hud, Transform parent, int seat, float height, Vector2 anchor, Vector2 pivot, Vector2 position, bool mirrored = false)
            {
                _color = SimSpace.SeatColors[seat];
                _panelTint = Color.Lerp(Color.white, _color, 0.65f);
                _ringTint = Color.Lerp(Color.white, _color, 0.45f);
                bool local = seat == hud._localSeat;
                var sprite = HudSprites.Panel;
                Root = UiFactory.Rect($"Panel {SimSpace.SeatNames[seat]}", parent, anchor, pivot, position,
                    new Vector2(height * HudSprites.Aspect(sprite), height));
                _panel = UiFactory.Image(UiFactory.Stretch("Back", Root), _panelTint, sprite);
                _panel.raycastTarget = false;
                _home = Root.anchoredPosition;

                // Portrait ring overhangs the panel's left end.
                float ringSize = height + 16f;
                var ring = UiFactory.Rect("Ring", Root, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(ringSize * 0.32f, 0f), Vector2.one * ringSize);
                _ring = MenuStyle.PortraitFrame(ring, hud._portraits[hud._characters[seat]], _ringTint, out _portrait);

                // Text stays inside the panel's metal frame: the border is about 16% of the height top and bottom,
                // and the slanted ends lean right, so the bottom row starts further left than the top row.
                float panelWidth = height * HudSprites.Aspect(sprite);
                float border = height * 0.17f;
                float textLeft = ringSize * 0.32f + ringSize * 0.5f + 6f;
                float scoreWidth = panelWidth * 0.2f;
                float scoreRight = panelWidth * 0.18f; // clear of the slanted right end, since the italic numbers lean into it
                float textWidth = panelWidth - textLeft - scoreRight - scoreWidth;
                // The name sits in the middle of the panel, and moves up only when there's a second line under it
                // (life pips, or KNOCKED OUT / WATCHING). It stops short of the score so long names don't touch it.
                _nameCentred = new Vector2(textLeft + height * 0.02f, 0f);
                _nameRaised = new Vector2(textLeft + height * 0.08f, height * 0.16f);
                _name = UiFactory.Rect("Name", Root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), _nameCentred, new Vector2(textWidth - 8f, height * 0.34f));
                FitText(UiFactory.Text(_name,
                    local ? "YOU" : hud.Label(seat).ToUpperInvariant(),
                    Mathf.RoundToInt(height * 0.27f), Color.Lerp(_color, Color.white, 0.35f), TextAnchor.MiddleLeft));
                UiFactory.UseDisplayFont(_name.GetComponent<Text>());
                _status = FitText(UiFactory.Text(UiFactory.Rect("Status", Root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(textLeft - height * 0.04f, border), new Vector2(textWidth, height * 0.28f)),
                    "", Mathf.RoundToInt(height * 0.21f), new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleLeft));
                _score = FitText(Numerals(UiFactory.Text(UiFactory.Rect("Score", Root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-scoreRight, 0f), new Vector2(scoreWidth, height - border * 2f)),
                    "0", Mathf.RoundToInt(height * 0.56f), Color.white, TextAnchor.MiddleRight, outline: false),
                    Color.Lerp(Color.white, _color, 0.6f)));

                // Life pips in Elimination (when there are few enough to fit).
                int lives = hud._sim.Settings.GoalsToEliminate;
                if (hud._sim.Settings.Mode == GameMode.Elimination && lives <= MaxPips)
                {
                    _pips = new Image[lives];
                    float width = Mathf.Min(30f, textWidth / lives - 3f); // stays clear of the score
                    float pipHeight = width / HudSprites.Aspect(HudSprites.PipFull);
                    for (int i = 0; i < lives; i++)
                    {
                        var pip = UiFactory.Rect($"Pip {i}", Root, new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                            new Vector2(textLeft - height * 0.04f + i * (width + 3f), border + height * 0.14f), new Vector2(width, pipHeight));
                        _pips[i] = UiFactory.Image(pip, Color.white, HudSprites.PipFull);
                        _pips[i].raycastTarget = false;
                    }
                }

                if (mirrored)
                {
                    Mirror();
                    _nameCentred.x = -_nameCentred.x;
                    _nameRaised.x = -_nameRaised.x;
                }
            }

            /// <summary>
            /// Flips the built panel left to right: the art is turned over and every part moves to the other side,
            /// with left-aligned text becoming right-aligned. The text itself still reads normally.
            /// </summary>
            private void Mirror()
            {
                foreach (RectTransform part in Root)
                {
                    float minX = part.anchorMin.x, maxX = part.anchorMax.x;
                    part.anchorMin = new Vector2(1f - maxX, part.anchorMin.y);
                    part.anchorMax = new Vector2(1f - minX, part.anchorMax.y);
                    part.pivot = new Vector2(1f - part.pivot.x, part.pivot.y);
                    part.anchoredPosition = new Vector2(-part.anchoredPosition.x, part.anchoredPosition.y);
                    var text = part.GetComponent<Text>();
                    if (text != null) text.alignment = text.alignment switch
                    {
                        TextAnchor.MiddleLeft => TextAnchor.MiddleRight,
                        TextAnchor.MiddleRight => TextAnchor.MiddleLeft,
                        _ => text.alignment,
                    };
                }
                _panel.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
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
                _status.text = out_ ? "KNOCKED OUT" : s.Status == SeatStatus.Spectating ? "WATCHING" : "";
                _name.anchoredPosition = showPips || _status.text.Length > 0 ? _nameRaised : _nameCentred;
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

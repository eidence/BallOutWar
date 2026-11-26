using System;
using System.Collections.Generic;
using BOW.AI;
using BOW.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BOW.Game
{
    /// <summary>
    /// Main menu and solo setup (US-7.1, US-1.1, US-1.4, US-1.5, US-4.1, US-6.4, US-6.5).
    /// Built in code for the prototype; screens will move to prefabs once the UI art exists.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {

        private enum MenuScreen { Main, Setup, Rules, ExitConfirm, Lan, Settings, Store, Resume }

        private MatchLaunchConfig _config;
        private PortraitStudio _studio;
        private MenuBackdrop _backdrop;
        private RectTransform _main, _setup, _exit;
        private MenuLogo _logo;
        private bool _mainShown;
        private readonly List<MenuButton> _mainButtons = new List<MenuButton>();
        private MenuScreen _screen;
        private readonly List<OptionRow> _rows = new List<OptionRow>();
        private readonly Image[] _characterRings = new Image[Characters.Count];
        private readonly Image[] _cardRims = new Image[Characters.Count];
        private readonly Text[] _cardNames = new Text[Characters.Count];
        private readonly MenuChip[] _modeChips = new MenuChip[2];
        private readonly MenuChip[] _opponentChips = new MenuChip[3];
        private readonly MenuChip[] _levelChips = new MenuChip[4];
        private MenuButton _rulesButton;
        private RectTransform _rules;
        private OptionRow _durationRow, _goalsRow, _tieRow;
        private LanMenu _lan;
        private SettingsMenu _settings;
        private StoreMenu _store;
        private MenuScreen _storeReturn = MenuScreen.Main;
        private Text _lockNote;
        private RectTransform _resumePanel;
        private Text _resumeDetails;
        private ResumeStore.SavedMatch _saved;

        private void Awake()
        {
            SettingsStore.Apply();
            var cam = Camera.main;
            SceneLook.Apply(cam);

            _config = ProfileStore.Load().LastMatch.Clone();
            _studio = new PortraitStudio(transform);
            StartCoroutine(_studio.RenderOnce());
            _backdrop = new MenuBackdrop(cam, transform, new[] { 0, 1, 2, 3 });

            var canvas = UiFactory.CreateCanvas("Menu UI", 10).transform;
            BuildMain(canvas);
            BuildSetup(canvas);
            BuildExitConfirm(canvas);
            _lan = new LanMenu(canvas, _studio.Portraits, () => Show(MenuScreen.Main));
            _settings = new SettingsMenu(canvas, () => Show(MenuScreen.Main));
            _store = new StoreMenu(canvas, () => Show(_storeReturn));
            EntitlementStore.Current.Changed += OnEntitlementsChanged;
            // Back from a LAN match, or a session that just ended: reopen Play Local.
            BuildResume(canvas);
            _saved = NetSession.Active ? null : ResumeStore.Load();
            Show(NetSession.Active || NetSession.EndReason != null ? MenuScreen.Lan : _saved != null ? MenuScreen.Resume : MenuScreen.Main);
        }

        private void Update()
        {
            _backdrop.Update(Time.deltaTime);
            _lan.Update(Time.deltaTime);
            if (!BackPressed()) return;
            // Android back / Escape goes back one screen; from the main menu it asks before exiting (US-6.4).
            switch (_screen)
            {
                case MenuScreen.Setup: Show(MenuScreen.Main); break;
                case MenuScreen.Rules: Show(MenuScreen.Setup); break;
                case MenuScreen.Main: Show(MenuScreen.ExitConfirm); break;
                case MenuScreen.ExitConfirm: Show(MenuScreen.Main); break;
                case MenuScreen.Lan: _lan.Back(); break;
                case MenuScreen.Settings: _settings.Close(); break;
                case MenuScreen.Store: _store.Close(); break;
                case MenuScreen.Resume: Show(MenuScreen.Main); break; // keep the save; it is offered again next launch
            }
        }

        private void OnDestroy()
        {
            _lan?.Dispose();
            _store?.Dispose();
            EntitlementStore.Current.Changed -= OnEntitlementsChanged;
        }

        private void OnEntitlementsChanged()
        {
            if (_screen == MenuScreen.Setup || _screen == MenuScreen.Rules) RefreshSetup();
        }

        private void OpenStore(MenuScreen returnTo, string reason = null)
        {
            _storeReturn = returnTo;
            Show(MenuScreen.Store);
            if (reason != null) _store.Show(reason);
        }

        private static bool BackPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            return pad != null && pad.buttonEast.wasPressedThisFrame;
        }

        private void Show(MenuScreen screen)
        {
            // The main screen stays visible behind its own dialogs (exit, resume); its entrance plays when it appears.
            bool mainVisible = screen == MenuScreen.Main || screen == MenuScreen.ExitConfirm || screen == MenuScreen.Resume;
            bool appearing = mainVisible && !_mainShown;
            _mainShown = mainVisible;
            _screen = screen;
            _main.gameObject.SetActive(mainVisible);
            if (appearing) PlayMainIntro();
            _setup.gameObject.SetActive(screen == MenuScreen.Setup || screen == MenuScreen.Rules);
            _rules.gameObject.SetActive(screen == MenuScreen.Rules);
            _exit.gameObject.SetActive(screen == MenuScreen.ExitConfirm);
            if (screen == MenuScreen.Lan) _lan.Show();
            else if (_lan != null) _lan.Hide();
            if (screen == MenuScreen.Settings) _settings.Show();
            else _settings?.Hide();
            if (screen == MenuScreen.Store) _store.Show();
            else _store?.Hide();
            if (_resumePanel != null) _resumePanel.gameObject.SetActive(screen == MenuScreen.Resume);
            if (screen == MenuScreen.Resume) RefreshResume();
            if (screen == MenuScreen.Setup || screen == MenuScreen.Rules) RefreshSetup();
        }

        // ----- Main menu -----

        /// <summary>
        /// Logo and a column of buttons down the left, over a dark fade; the live demo match plays on the right.
        /// Play Solo is the hero action (gold, glowing); Settings and Exit sit in a smaller row at the bottom.
        /// </summary>
        private void BuildMain(Transform canvas)
        {
            _main = UiFactory.Stretch("Main", canvas);
            MenuStyle.LeftFade(_main, 1150f, 0.85f);

            var topLeft = new Vector2(0f, 1f);
            const float left = 64f, width = 600f;

            var logoSprite = MenuStyle.Logo;
            var logo = UiFactory.Rect("Logo", _main, topLeft, new Vector2(0.5f, 0.5f), new Vector2(left + width * 0.5f, -190f), new Vector2(660f, 330f));
            if (logoSprite != null)
            {
                var image = logo.gameObject.AddComponent<Image>();
                image.sprite = logoSprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }
            else MenuStyle.Heading(logo, "BALL OUT WAR", 96, MenuStyle.Gold);
            _logo = logo.gameObject.AddComponent<MenuLogo>();

            _mainButtons.Clear();
            float y = -388f;
            MenuButton Add(string label, MenuIcon icon, Color accent, float glow, float height, Action onClick)
            {
                var b = MenuStyle.Button(_main, label, icon, accent, topLeft, topLeft, new Vector2(left, y), new Vector2(width, height), glow, onClick);
                _mainButtons.Add(b);
                y -= height + 18f;
                return b;
            }

            Add("Play Solo", MenuIcon.Play, MenuStyle.Gold, 0.6f, 112f, () => Show(MenuScreen.Setup)).Pulse = true;
            Add("Play Local", MenuIcon.Local, MenuStyle.Cyan, 0.22f, 100f, () => Show(MenuScreen.Lan));
            var online = Add("Play Online", MenuIcon.Online, MenuStyle.Cyan, 0f, 100f, () => { });
            online.Interactable = false;
            online.Tag("SOON", MenuStyle.Gold);
            Add("Store", MenuIcon.Store, MenuStyle.Cyan, 0.22f, 100f, () => OpenStore(MenuScreen.Main));

            // Secondary row: two half-width buttons.
            y -= 8f;
            float half = (width - 18f) * 0.5f;
            var muted = new Color(0.55f, 0.68f, 0.82f);
            _mainButtons.Add(MenuStyle.Button(_main, "Settings", MenuIcon.Settings, muted, topLeft, topLeft, new Vector2(left, y), new Vector2(half, 84f), 0f,
                () => Show(MenuScreen.Settings)));
            _mainButtons.Add(MenuStyle.Button(_main, "Exit", MenuIcon.Exit, muted, topLeft, topLeft, new Vector2(left + half + 18f, y), new Vector2(half, 84f), 0f,
                () => Show(MenuScreen.ExitConfirm)));

            var version = UiFactory.Rect("Version", _main, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-36f, 24f), new Vector2(500f, 40f));
            var versionText = UiFactory.Text(version, "v" + Application.version, 24, new Color(1f, 1f, 1f, 0.45f), TextAnchor.LowerRight, outline: false);
            versionText.fontStyle = FontStyle.Italic;
        }

        /// <summary>Replays the logo and button entrance whenever the main screen opens.</summary>
        private void PlayMainIntro()
        {
            _logo.Intro();
            for (int i = 0; i < _mainButtons.Count; i++) _mainButtons[i].Intro(0.12f + i * 0.055f);
        }

        private void BuildExitConfirm(Transform canvas)
        {
            _exit = UiFactory.Stretch("Exit Confirm", canvas);
            var box = MenuStyle.Dialog(_exit, new Vector2(900f, 400f), MenuStyle.Cyan, "Leave Ball Out War?", Color.white);
            var bottom = new Vector2(0.5f, 0f);
            var centre = new Vector2(0.5f, 0.5f);
            MenuStyle.Button(box, "Exit", MenuIcon.Exit, MenuStyle.Red, bottom, centre, new Vector2(-190f, 105f), new Vector2(340f, 100f), 0.3f, ExitGame);
            MenuStyle.Button(box, "Stay", null, MenuStyle.Cyan, bottom, centre, new Vector2(190f, 105f), new Vector2(340f, 100f), 0.3f, () => Show(MenuScreen.Main));
        }

        // ----- Offline resume (TDD 9.4, US-6.6) -----

        private void BuildResume(Transform canvas)
        {
            _resumePanel = UiFactory.Stretch("Resume", canvas);
            var box = MenuStyle.Dialog(_resumePanel, new Vector2(1000f, 480f), MenuStyle.Gold, "Resume your match?", MenuStyle.Gold);
            _resumeDetails = UiFactory.Text(UiFactory.Rect("Details", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(940f, 120f)),
                "", 34, MenuStyle.TextMuted, TextAnchor.MiddleCenter, outline: false);
            var bottom = new Vector2(0.5f, 0f);
            var centre = new Vector2(0.5f, 0.5f);
            MenuStyle.Button(box, "Resume", MenuIcon.Play, MenuStyle.Gold, bottom, centre, new Vector2(-200f, 105f), new Vector2(360f, 104f), 0.5f, ResumeMatch).Pulse = true;
            MenuStyle.Button(box, "Discard", null, new Color(0.55f, 0.68f, 0.82f), bottom, centre, new Vector2(200f, 105f), new Vector2(340f, 104f), 0f, DiscardResume);
            _resumePanel.gameObject.SetActive(false);
        }

        private void RefreshResume()
        {
            if (_saved == null) return;
            var c = _saved.Config;
            string mode = c.Mode == GameMode.Elimination ? "Elimination" : "Timed";
            var age = System.DateTime.UtcNow - new System.DateTime(_saved.SavedAtUtcTicks, System.DateTimeKind.Utc);
            string when = age.TotalMinutes < 1 ? "just now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago" : $"{(int)age.TotalHours} h ago";
            _resumeDetails.text = $"{mode} as {Characters.Names[c.PlayerCharacter]} vs {c.Opponents} AI\nSaved {when}";
        }

        private void ResumeMatch()
        {
            if (_saved == null) { Show(MenuScreen.Main); return; }
            ResumeStore.Pending = _saved;
            MatchLaunch.Pending = _saved.Config.Clone();
            SceneManager.LoadScene(BootLoader.MatchScene);
        }

        private void DiscardResume()
        {
            ResumeStore.Delete();
            _saved = null;
            Show(MenuScreen.Main);
        }

        private static void ExitGame()
        {
            ProfileStore.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ----- Solo setup -----

        private static readonly Color CardFill = new Color(0.03f, 0.05f, 0.1f, 0.9f);
        private static readonly Color CardRim = new Color(0.35f, 0.5f, 0.68f, 0.4f);

        /// <summary>
        /// One panel along the bottom, with the demo match still playing above it: four driver cards, a single row of
        /// choices (mode, opponents, AI level) that are all on show, and START. The finer rules (match length, balls,
        /// tie-break) sit behind the Rules button, since most players never change them.
        /// </summary>
        private void BuildSetup(Transform canvas)
        {
            _setup = UiFactory.Stretch("Setup", canvas);
            UiFactory.Image(_setup, new Color(0.01f, 0.02f, 0.05f, 0.3f));
            // Full height, 28 px clear of the screen edges; everything inside keeps 40 px between blocks.
            var panel = UiFactory.Rect("Panel", _setup, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1800f, -56f));
            panel.anchorMin = new Vector2(0.5f, 0f);
            panel.anchorMax = new Vector2(0.5f, 1f);
            MenuStyle.Panel(panel, MenuStyle.Cyan);
            var topLeft = new Vector2(0f, 1f);

            MenuStyle.Heading(UiFactory.Rect("Heading", panel, topLeft, topLeft, new Vector2(50f, -30f), new Vector2(900f, 70f)),
                "CHOOSE YOUR DRIVER", 50, Color.white, TextAnchor.MiddleLeft);
            _lockNote = UiFactory.Text(UiFactory.Rect("Lock note", panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -30f), new Vector2(800f, 70f)),
                "", 26, MenuStyle.Gold, TextAnchor.MiddleRight, outline: false);
            _lockNote.horizontalOverflow = HorizontalWrapMode.Wrap;

            // Driver cards (US-1.4).
            for (int c = 0; c < Characters.Count; c++)
            {
                int character = c;
                // Cards fill the height between the heading and the choices; the portrait grows with them.
                var card = UiFactory.Rect(Characters.Names[c], panel, topLeft, topLeft, Vector2.zero, Vector2.zero);
                card.anchorMin = new Vector2(0f, 0f);
                card.anchorMax = new Vector2(0f, 1f);
                card.offsetMin = new Vector2(50f + c * 430f, 276f);
                card.offsetMax = new Vector2(50f + c * 430f + 410f, -140f);
                var fill = MenuStyle.Recess(card, CardFill, 1.2f);
                _cardRims[c] = MenuStyle.Outline(card, CardRim, 1.2f);
                var button = card.gameObject.AddComponent<Button>();
                button.targetGraphic = fill;
                var colors = button.colors;
                colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
                colors.selectedColor = Color.white;
                colors.pressedColor = new Color(1.8f, 1.8f, 1.8f, 1f);
                button.colors = colors;
                button.onClick.AddListener(() => { GameAudio.Play(Sfx.Click); _config.PlayerCharacter = character; RefreshSetup(); });

                var ring = UiFactory.Stretch("Ring", card);
                ring.offsetMin = new Vector2(24f, 92f);
                ring.offsetMax = new Vector2(-24f, -24f);
                var fit = ring.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = 1f;
                _characterRings[c] = MenuStyle.PortraitFrame(ring, _studio.Portraits[c], MenuStyle.RingColor(false), out _);

                _cardNames[c] = MenuStyle.Heading(UiFactory.Rect("Name", card, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 48f), new Vector2(380f, 64f)),
                    Characters.Names[c], 44, Color.white);
            }

            // One row of choices, every option visible, spanning the same width as the cards.
            var choices = UiFactory.Rect("Choices", panel, new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0f, 206f), new Vector2(1800f, 60f));
            panel = choices;
            const float row = -30f, chipH = 60f, gap = 8f;
            MenuStyle.Caption(panel, "MODE", 50f, row, 90f);
            _modeChips[0] = new MenuChip(panel, "TIMED", 150f, row, 150f, chipH, () => SetMode(GameMode.Timed));
            _modeChips[1] = new MenuChip(panel, "ELIMINATION", 150f + 150f + gap, row, 210f, chipH, () => SetMode(GameMode.Elimination));

            MenuStyle.Caption(panel, "OPPONENTS", 587f, row, 170f);
            for (int n = 0; n < _opponentChips.Length; n++)
            {
                int count = n + 1;
                _opponentChips[n] = new MenuChip(panel, count.ToString(), 767f + n * (64f + gap), row, 64f, chipH,
                    () => { _config.Opponents = count; RefreshSetup(); });
            }

            MenuStyle.Caption(panel, "AI LEVEL", 1044f, row, 140f);
            float[] levelWidths = { 116f, 150f, 116f, 150f };
            float x = 1194f;
            for (int l = 0; l < _levelChips.Length; l++)
            {
                var level = (AIDifficulty)l;
                _levelChips[l] = new MenuChip(panel, level.ToString().ToUpperInvariant(), x, row, levelWidths[l], chipH, () =>
                {
                    for (int i = 0; i < _config.Difficulty.Length; i++) _config.Difficulty[i] = level;
                    RefreshSetup();
                });
                x += levelWidths[l] + gap;
            }

            // Bottom row, 50 px in from each side, all centred on one line: Back, the rules summary (opens the rules), START.
            panel = (RectTransform)choices.parent;
            var back = MenuStyle.BackButton(panel, "Back", () => Show(MenuScreen.Main));
            ((RectTransform)back.transform).anchoredPosition = new Vector2(200f, 82f);
            _rulesButton = MenuStyle.Make(panel, "Rules", MenuIcon.Settings, MenuStyle.Kind.Quiet, new Vector2(0f, 0f), new Vector2(760f, 82f), new Vector2(760f, 96f),
                () => Show(MenuScreen.Rules));
            _rulesButton.Label.resizeTextMaxSize = 34; // one line, never wrapped
            MenuStyle.Make(panel, "START", MenuIcon.Play, MenuStyle.Kind.Primary, new Vector2(1f, 0f), new Vector2(-240f, 82f), new Vector2(380f, 108f), StartSolo);

            BuildRules(canvas);
        }

        /// <summary>The rarely-changed match rules, in a dialog over the setup panel.</summary>
        private void BuildRules(Transform canvas)
        {
            _rules = UiFactory.Stretch("Rules", canvas);
            var box = MenuStyle.Dialog(_rules, new Vector2(1000f, 560f), MenuStyle.Cyan, "MATCH RULES", Color.white, 56);
            float x = 140f, top = -175f, step = 84f;
            _rows.Add(_durationRow = new OptionRow(box, "Match length", x, top,
                () => $"{_config.DurationSeconds / 60f:0} min",
                d => _config.DurationSeconds = Cycle(MatchLaunchConfig.DurationOptions, _config.DurationSeconds, d)));
            _rows.Add(_goalsRow = new OptionRow(box, "Goals to knock out", x, top,
                () => _config.GoalsToEliminate.ToString(),
                d => _config.GoalsToEliminate = Wrap(_config.GoalsToEliminate + d, MatchSettings.MinGoalsToEliminate, MatchSettings.MaxGoalsToEliminate)));
            _rows.Add(new OptionRow(box, "Balls", x, top - step,
                () => _config.BallCount.ToString(),
                d => _config.BallCount = Wrap(_config.BallCount + d, MatchSettings.MinBalls, MatchSettings.MaxBalls)));
            _rows.Add(_tieRow = new OptionRow(box, "On a tie", x, top - step * 2f,
                () => _config.TieRule == TieRule.SuddenDeath ? "Sudden death" : "Draw",
                d => _config.TieRule = _config.TieRule == TieRule.SuddenDeath ? TieRule.AllowDraw : TieRule.SuddenDeath));
            MenuStyle.Make(box, "Done", null, MenuStyle.Kind.Secondary, new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(320f, 96f), () => Show(MenuScreen.Setup));
            foreach (var r in _rows) r.Changed += RefreshSetup;
            _rules.gameObject.SetActive(false);
        }

        private void SetMode(GameMode mode)
        {
            _config.Mode = mode;
            RefreshSetup();
        }

        private void RefreshSetup()
        {
            _config.Sanitise();
            for (int c = 0; c < Characters.Count; c++)
            {
                bool picked = c == _config.PlayerCharacter;
                _characterRings[c].color = MenuStyle.RingColor(picked);
                _cardRims[c].color = picked ? MenuStyle.Gold : CardRim;
                _cardNames[c].color = picked ? MenuStyle.Gold : Color.white;
            }

            bool timed = _config.Mode == GameMode.Timed;
            _modeChips[0].SetOn(timed);
            _modeChips[1].SetOn(!timed);
            for (int n = 0; n < _opponentChips.Length; n++) _opponentChips[n].SetOn(_config.Opponents == n + 1);
            for (int l = 0; l < _levelChips.Length; l++) _levelChips[l].SetOn((int)_config.Difficulty[1] == l);

            _durationRow.SetVisible(timed);
            _tieRow.SetVisible(timed);
            _goalsRow.SetVisible(!timed);
            _lockNote.text = EntitlementStore.SoloNote(_config.Mode) ?? "";
            _rulesButton.Label.text = timed
                ? $"{_config.DurationSeconds / 60f:0} min · {Plural(_config.BallCount, "ball")} · {(_config.TieRule == TieRule.SuddenDeath ? "sudden death" : "draws")}"
                : $"{Plural(_config.GoalsToEliminate, "goal")} out · {Plural(_config.BallCount, "ball")}";
            foreach (var r in _rows) r.Refresh();
        }

        private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

        private void StartSolo()
        {
            // Locked modes: one free try, then the store (US-8.9).
            var status = EntitlementStore.Current.StatusFor(_config.Mode);
            if (status == BOW.Meta.ModeStatus.Locked)
            {
                OpenStore(MenuScreen.Setup, "Elimination needs the Modes unlock.");
                return;
            }
            if (status == BOW.Meta.ModeStatus.Trial) EntitlementStore.Current.UseTrial(_config.Mode);
            var profile = ProfileStore.Load();
            profile.LastMatch = _config.Clone();
            ProfileStore.Save();
            MatchLaunch.Pending = _config.Clone();
            SceneManager.LoadScene(BootLoader.MatchScene);
        }

        private static int Wrap(int value, int min, int max) => value < min ? max : value > max ? min : value;

        private static float Cycle(float[] options, float current, int delta)
        {
            int i = Array.IndexOf(options, current);
            if (i < 0) i = 0;
            return options[(i + delta + options.Length) % options.Length];
        }

    }
}

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
        private static readonly Color Cyan = MenuTheme.Cyan;
        private static readonly Color Panel = MenuTheme.Panel;
        private static readonly Color ButtonBlue = MenuTheme.ButtonBlue;
        private static readonly Color ButtonGrey = MenuTheme.ButtonGrey;

        private enum MenuScreen { Main, Setup, ExitConfirm, Lan }

        private MatchLaunchConfig _config;
        private PortraitStudio _studio;
        private MenuBackdrop _backdrop;
        private RectTransform _main, _setup, _exit;
        private MenuScreen _screen;
        private readonly List<OptionRow> _rows = new List<OptionRow>();
        private readonly Image[] _characterRings = new Image[Characters.Count];
        private Text _characterName;
        private readonly OptionRow[] _opponentRows = new OptionRow[SeatFrame.SeatCount];
        private OptionRow _durationRow, _goalsRow, _tieRow;
        private LanMenu _lan;

        private void Awake()
        {
            Application.targetFrameRate = 60;
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
            // Back from a LAN match, or a session that just ended: reopen Play Local.
            Show(NetSession.Active || NetSession.EndReason != null ? MenuScreen.Lan : MenuScreen.Main);
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
                case MenuScreen.Main: Show(MenuScreen.ExitConfirm); break;
                case MenuScreen.ExitConfirm: Show(MenuScreen.Main); break;
                case MenuScreen.Lan: _lan.Back(); break;
            }
        }

        private void OnDestroy() => _lan?.Dispose();

        private static bool BackPressed()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            return pad != null && pad.buttonEast.wasPressedThisFrame;
        }

        private void Show(MenuScreen screen)
        {
            _screen = screen;
            _main.gameObject.SetActive(screen == MenuScreen.Main);
            _setup.gameObject.SetActive(screen == MenuScreen.Setup);
            _exit.gameObject.SetActive(screen == MenuScreen.ExitConfirm);
            if (screen == MenuScreen.Lan) _lan.Show();
            else if (_lan != null) _lan.Hide();
            if (screen == MenuScreen.Setup) RefreshSetup();
        }

        // ----- Main menu -----

        private void BuildMain(Transform canvas)
        {
            _main = UiFactory.Stretch("Main", canvas);

            var title = UiFactory.Rect("Title", _main, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(1400f, 180f));
            UiFactory.Text(title, "BALL OUT WAR", 150, Cyan);
            var tag = UiFactory.Rect("Tagline", _main, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -205f), new Vector2(1400f, 60f));
            UiFactory.Text(tag, "Defend your goal. Blast the rest.", 40, Color.white);

            var column = UiFactory.Rect("Buttons", _main, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(80f, -60f), new Vector2(560f, 760f));
            float y = 330f;
            void Add(string label, bool enabled, Color color, Action onClick)
            {
                var b = UiFactory.Button(column, label, new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(540f, 92f), color, onClick);
                b.interactable = enabled;
                y -= 108f;
            }

            Add("Play Solo", true, ButtonBlue, () => Show(MenuScreen.Setup));
            Add("Practice", true, ButtonBlue, StartPractice);
            Add("Play Local", true, ButtonBlue, () => Show(MenuScreen.Lan));
            Add("Play Online  (soon)", false, ButtonGrey, () => { });
            Add("Store  (soon)", false, ButtonGrey, () => { });
            Add("Settings  (soon)", false, ButtonGrey, () => { });
            Add("Exit", true, ButtonGrey, () => Show(MenuScreen.ExitConfirm));
        }

        private void BuildExitConfirm(Transform canvas)
        {
            _exit = UiFactory.Stretch("Exit Confirm", canvas);
            UiFactory.Image(_exit, new Color(0f, 0f, 0f, 0.6f));
            var box = UiFactory.Rect("Box", _exit, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 420f));
            UiFactory.Image(box, Panel);
            UiFactory.Text(UiFactory.Rect("Question", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(860f, 120f)),
                "Exit Ball Out War?", 64, Color.white);
            UiFactory.Button(box, "Exit", new Vector2(0.5f, 0f), new Vector2(-180f, 110f), new Vector2(320f, 100f), new Color(0.7f, 0.2f, 0.2f), ExitGame);
            UiFactory.Button(box, "Cancel", new Vector2(0.5f, 0f), new Vector2(180f, 110f), new Vector2(320f, 100f), ButtonGrey, () => Show(MenuScreen.Main));
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

        private void BuildSetup(Transform canvas)
        {
            _setup = UiFactory.Stretch("Setup", canvas);
            UiFactory.Image(_setup, new Color(0f, 0f, 0f, 0.35f));
            var box = UiFactory.Rect("Box", _setup, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1640f, 960f));
            UiFactory.Image(box, Panel);
            UiFactory.Text(UiFactory.Rect("Title", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1000f, 90f)),
                "SOLO MATCH", 72, Cyan);

            // Character picker (US-1.4).
            UiFactory.Text(UiFactory.Rect("Pick", box, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -120f), new Vector2(600f, 50f)),
                "Choose your driver", 36, Color.white, TextAnchor.MiddleLeft);
            for (int c = 0; c < Characters.Count; c++)
            {
                int character = c;
                var slot = UiFactory.Rect(Characters.Names[c], box, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f + c * 190f, -180f), new Vector2(170f, 170f));
                _characterRings[c] = UiFactory.Image(slot, Color.gray, ProceduralTextures.Circle());
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = _characterRings[c];
                button.onClick.AddListener(() => { _config.PlayerCharacter = character; RefreshSetup(); });
                var mask = UiFactory.Rect("Mask", slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 150f));
                UiFactory.Image(mask, Color.white, ProceduralTextures.Circle()).raycastTarget = false;
                mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                var portrait = UiFactory.Stretch("Portrait", mask).gameObject.AddComponent<RawImage>();
                portrait.texture = _studio.Portraits[c];
                portrait.raycastTarget = false;
            }
            _characterName = UiFactory.Text(UiFactory.Rect("Name", box, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -360f), new Vector2(760f, 50f)),
                "", 34, Color.white, TextAnchor.MiddleLeft);

            // Match options (left column) and opponents (right column).
            float leftX = 60f, rightX = 880f, top = -440f, step = 78f;
            _rows.Add(new OptionRow(box, "Mode", leftX, top,
                () => _config.Mode == GameMode.Timed ? "Timed" : "Elimination",
                d => _config.Mode = _config.Mode == GameMode.Timed ? GameMode.Elimination : GameMode.Timed));
            _rows.Add(_durationRow = new OptionRow(box, "Match length", leftX, top - step,
                () => $"{_config.DurationSeconds / 60f:0} min",
                d => _config.DurationSeconds = Cycle(MatchLaunchConfig.DurationOptions, _config.DurationSeconds, d)));
            _rows.Add(_goalsRow = new OptionRow(box, "Goals to knock out", leftX, top - step,
                () => _config.GoalsToEliminate.ToString(),
                d => _config.GoalsToEliminate = Wrap(_config.GoalsToEliminate + d, MatchSettings.MinGoalsToEliminate, MatchSettings.MaxGoalsToEliminate)));
            _rows.Add(new OptionRow(box, "Balls", leftX, top - step * 2f,
                () => _config.BallCount.ToString(),
                d => _config.BallCount = Wrap(_config.BallCount + d, MatchSettings.MinBalls, MatchSettings.MaxBalls)));
            _rows.Add(_tieRow = new OptionRow(box, "On a tie", leftX, top - step * 3f,
                () => _config.TieRule == TieRule.SuddenDeath ? "Sudden death" : "Draw",
                d => _config.TieRule = _config.TieRule == TieRule.SuddenDeath ? TieRule.AllowDraw : TieRule.SuddenDeath));

            _rows.Add(new OptionRow(box, "Opponents", rightX, top,
                () => _config.Opponents.ToString(),
                d => _config.Opponents = Wrap(_config.Opponents + d, 1, 3)));
            _rows.Add(new OptionRow(box, "All AI", rightX, top - step,
                () => "Set all",
                d =>
                {
                    var level = (AIDifficulty)Wrap((int)_config.Difficulty[1] + d, 0, 3);
                    for (int i = 0; i < _config.Difficulty.Length; i++) _config.Difficulty[i] = level;
                }));
            for (int seat = 1; seat < SeatFrame.SeatCount; seat++)
            {
                int s = seat;
                _opponentRows[s] = new OptionRow(box, "", rightX, top - step * (1 + s),
                    () => _config.Difficulty[s].ToString(),
                    d => _config.Difficulty[s] = (AIDifficulty)Wrap((int)_config.Difficulty[s] + d, 0, 3));
                _rows.Add(_opponentRows[s]);
            }

            UiFactory.Button(box, "Back", new Vector2(0f, 0f), new Vector2(240f, 80f), new Vector2(320f, 100f), ButtonGrey, () => Show(MenuScreen.Main));
            UiFactory.Button(box, "START", new Vector2(1f, 0f), new Vector2(-240f, 80f), new Vector2(360f, 110f), new Color(0.1f, 0.65f, 0.35f), StartSolo);

            foreach (var row in _rows) row.Changed += RefreshSetup;
        }

        private void RefreshSetup()
        {
            _config.Sanitise();
            for (int c = 0; c < Characters.Count; c++)
                _characterRings[c].color = c == _config.PlayerCharacter ? Cyan : new Color(0.25f, 0.28f, 0.35f);
            _characterName.text = $"{Characters.Names[_config.PlayerCharacter]}  -  {Characters.Descriptions[_config.PlayerCharacter]}";

            bool timed = _config.Mode == GameMode.Timed;
            _durationRow.SetVisible(timed);
            _tieRow.SetVisible(timed);
            _goalsRow.SetVisible(!timed);

            bool[] active = _config.ActiveSeats();
            int[] characters = _config.CharactersBySeat();
            for (int seat = 1; seat < SeatFrame.SeatCount; seat++)
            {
                _opponentRows[seat].SetVisible(active[seat]);
                _opponentRows[seat].SetLabel($"vs {Characters.Names[characters[seat]]}");
            }
            foreach (var row in _rows) row.Refresh();
        }

        private void StartSolo()
        {
            var profile = ProfileStore.Load();
            profile.LastMatch = _config.Clone();
            ProfileStore.Save();
            MatchLaunch.Pending = _config.Clone();
            SceneManager.LoadScene(BootLoader.MatchScene);
        }

        private void StartPractice()
        {
            var practice = _config.Clone();
            practice.Mode = GameMode.Practice;
            practice.BallCount = 1;
            MatchLaunch.Pending = practice;
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

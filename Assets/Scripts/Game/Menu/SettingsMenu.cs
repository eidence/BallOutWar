using System;
using System.Collections.Generic;
using BOW.Net;
using UnityEngine;
using UnityEngine.UI;

namespace BOW.Game
{
    /// <summary>Settings screen (TDD 9.2, US-7.3, US-7.5): sound, vibration, graphics, frame rate, controls and name.</summary>
    public sealed class SettingsMenu
    {
        private readonly RectTransform _root;
        private readonly Action _back;
        private readonly List<OptionRow> _rows = new List<OptionRow>();
        private readonly InputField _name;

        public SettingsMenu(Transform canvas, Action back)
        {
            _back = back;
            _root = UiFactory.Stretch("Settings", canvas);
            UiFactory.Image(_root, new Color(0f, 0f, 0f, 0.35f));
            var box = UiFactory.Rect("Box", _root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1640f, 960f));
            UiFactory.Image(box, MenuTheme.Panel);
            UiFactory.Text(UiFactory.Rect("Title", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1000f, 90f)),
                "SETTINGS", 72, MenuTheme.Cyan);

            var s = SettingsStore.Current;
            float left = 60f, right = 880f, top = -150f, step = 90f;

            Add(new OptionRow(box, "Volume", left, top, () => Bar(s.MasterVolume), d => s.MasterVolume = Mathf.Clamp(s.MasterVolume + d, 0, GameSettings.MaxVolume)));
            Add(new OptionRow(box, "Sound effects", left, top - step, () => Bar(s.EffectsVolume), d => s.EffectsVolume = Mathf.Clamp(s.EffectsVolume + d, 0, GameSettings.MaxVolume)));
            Add(new OptionRow(box, "Music", left, top - step * 2f, () => Bar(s.MusicVolume), d => s.MusicVolume = Mathf.Clamp(s.MusicVolume + d, 0, GameSettings.MaxVolume)));
            Add(new OptionRow(box, "Vibration", left, top - step * 3f, () => s.Haptics ? "On" : "Off", d => { s.Haptics = !s.Haptics; if (s.Haptics) Haptics.Pulse(40); }));

            Add(new OptionRow(box, "Graphics", right, top, () => SettingsStore.QualityName(s.Quality),
                d => s.Quality = Wrap(s.Quality + d, -1, Mathf.Min(2, QualitySettings.names.Length - 1))));
            Add(new OptionRow(box, "Frame rate", right, top - step, () => $"{s.FrameRate} fps", d => s.FrameRate = s.FrameRate == 60 ? 30 : 60));
            Add(new OptionRow(box, "Blast button", right, top - step * 2f, () => s.LeftHanded ? "Left side" : "Right side", d => s.LeftHanded = !s.LeftHanded));

            UiFactory.Text(UiFactory.Rect("Name label", box, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(right, top - step * 3f), new Vector2(330f, 60f)),
                "Player name", 32, Color.white, TextAnchor.MiddleLeft);
            _name = UiInput.Create(box, new Vector2(0f, 1f), new Vector2(right + 545f, top - step * 3f - 33f), new Vector2(390f, 66f), "Your name",
                ProfileStore.Load().PlayerName, maxLength: NetProtocol.MaxNameLength);
            _name.onEndEdit.AddListener(SaveName);

            var note = UiFactory.Rect("Note", box, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(1500f, 90f));
            var noteText = UiFactory.Text(note, "Graphics: Low saves battery on older phones. Auto picks the best level for this device.", 28,
                new Color(1f, 1f, 1f, 0.6f));
            noteText.horizontalOverflow = HorizontalWrapMode.Wrap;

            UiFactory.Button(box, "Back", new Vector2(0f, 0f), new Vector2(240f, 80f), new Vector2(320f, 100f), MenuTheme.ButtonGrey, Close);
            _root.gameObject.SetActive(false);
        }

        public bool Visible => _root.gameObject.activeSelf;

        public void Show()
        {
            _name.text = ProfileStore.Load().PlayerName;
            foreach (var row in _rows) row.Refresh();
            _root.gameObject.SetActive(true);
        }

        public void Hide() => _root.gameObject.SetActive(false);

        /// <summary>Saves and goes back (Back button, Android back or Escape).</summary>
        public void Close()
        {
            SaveName(_name.text);
            SettingsStore.Save();
            Hide();
            _back();
        }

        private void Add(OptionRow row)
        {
            _rows.Add(row);
            row.Changed += () =>
            {
                SettingsStore.Apply();
                GameAudio.Play(Sfx.Click);
                foreach (var r in _rows) r.Refresh();
            };
        }

        private void SaveName(string value)
        {
            var profile = ProfileStore.Load();
            profile.PlayerName = PlayerNames.Normalise(value);
            _name.text = profile.PlayerName;
            ProfileStore.Save();
        }

        private static string Bar(int value) => value == 0 ? "Off" : $"{value * 10}%";

        private static int Wrap(int value, int min, int max) => value < min ? max : value > max ? min : value;
    }
}

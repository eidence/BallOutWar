using System;
using System.Collections.Generic;
using BOW.AI;
using BOW.Net;
using BOW.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace BOW.Game
{
    /// <summary>
    /// Play Local screens (US-5.1, US-5.2, US-5.6, US-1.2, US-1.3): find or host a game on the local network,
    /// then the lobby where the host sets up seats and options and starts the match.
    /// </summary>
    public sealed class LanMenu
    {
        private const int ListRows = 5;
        private static readonly float[] Durations = { 60f, 120f, 180f, 300f };

        private readonly RenderTexture[] _portraits;
        private readonly Action _back;
        private readonly LanDiscovery _discovery = new LanDiscovery();
        private readonly RectTransform _browse, _lobby;

        // Browse
        private readonly InputField _name, _address;
        private readonly Button[] _matchButtons = new Button[ListRows];
        private readonly Text[] _matchLabels = new Text[ListRows];
        private readonly Text _emptyList, _browseStatus;
        private readonly List<FoundMatch> _shown = new List<FoundMatch>();
        private float _refresh;

        // Lobby
        private readonly Text _lobbyTitle, _lobbyHint, _lobbyStatus, _notice;
        private readonly SeatRow[] _seats = new SeatRow[SeatFrame.SeatCount];
        private readonly Image[] _characterRings = new Image[Characters.Count];
        private readonly OptionRow _modeRow, _lengthRow, _goalsRow, _ballsRow;
        private readonly Button _start, _ready;
        private readonly Text _readyLabel;
        private float _noticeTime;

        public bool Visible => _browse.gameObject.activeSelf || _lobby.gameObject.activeSelf;

        public LanMenu(Transform canvas, RenderTexture[] portraits, Action back)
        {
            _portraits = portraits;
            _back = back;
            var profile = ProfileStore.Load();

            // ---------------- Browse ----------------
            _browse = UiFactory.Stretch("Play Local", canvas);
            UiFactory.Image(_browse, new Color(0f, 0f, 0f, 0.35f));
            var box = Box(_browse, "PLAY ON LOCAL WI-FI");

            Label(box, "Your name", 60f, -140f, 300f);
            _name = UiInput.Create(box, new Vector2(0f, 1f), new Vector2(620f, -165f), new Vector2(520f, 76f), "Your name", profile.PlayerName,
                maxLength: NetProtocol.MaxNameLength);
            _name.onEndEdit.AddListener(SaveName);

            UiFactory.Button(box, "Host a game", new Vector2(0f, 1f), new Vector2(330f, -330f), new Vector2(540f, 120f), MenuTheme.ButtonGreen, Host);
            var hint = UiFactory.Rect("Host hint", box, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -410f), new Vector2(560f, 140f));
            var hintText = UiFactory.Text(hint, "Players on the same Wi-Fi or phone hotspot will see your game. No internet needed.", 28, new Color(1f, 1f, 1f, 0.7f), TextAnchor.UpperLeft);
            hintText.horizontalOverflow = HorizontalWrapMode.Wrap;

            Label(box, "Games nearby", 760f, -250f, 600f);
            for (int i = 0; i < ListRows; i++)
            {
                int index = i;
                _matchButtons[i] = UiFactory.Button(box, "", new Vector2(0f, 1f), new Vector2(1180f, -340f - i * 104f), new Vector2(820f, 92f), MenuTheme.ButtonBlue, () => JoinListed(index));
                _matchLabels[i] = _matchButtons[i].GetComponentInChildren<Text>();
                _matchLabels[i].fontSize = 32;
            }
            _emptyList = UiFactory.Text(UiFactory.Rect("Empty", box, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(770f, -320f), new Vector2(820f, 120f)),
                "Looking for games...", 30, new Color(1f, 1f, 1f, 0.6f), TextAnchor.UpperLeft);

            Label(box, "Or join by IP", 60f, -640f, 400f);
            _address = UiInput.Create(box, new Vector2(0f, 1f), new Vector2(330f, -730f), new Vector2(540f, 76f), "e.g. 192.168.1.20", "", maxLength: 40);
            UiFactory.Button(box, "Join", new Vector2(0f, 1f), new Vector2(720f, -730f), new Vector2(200f, 76f), MenuTheme.ButtonBlue, JoinByAddress);

            _browseStatus = UiFactory.Text(UiFactory.Rect("Status", box, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1500f, 50f)),
                "", 30, new Color(1f, 0.6f, 0.4f));
            UiFactory.Button(box, "Back", new Vector2(0f, 0f), new Vector2(240f, 80f), new Vector2(320f, 100f), MenuTheme.ButtonGrey, () => { Hide(); _back(); });

            // ---------------- Lobby ----------------
            _lobby = UiFactory.Stretch("Lobby", canvas);
            UiFactory.Image(_lobby, new Color(0f, 0f, 0f, 0.35f));
            var lb = Box(_lobby, "");
            _lobbyTitle = lb.Find("Title").GetComponent<Text>();
            _lobbyHint = UiFactory.Text(UiFactory.Rect("Hint", lb, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(1500f, 44f)),
                "", 28, new Color(1f, 1f, 1f, 0.7f));

            for (int i = 0; i < _seats.Length; i++) _seats[i] = new SeatRow(this, lb, i, -170f - i * 150f);

            Label(lb, "Your driver", 900f, -160f, 400f);
            for (int c = 0; c < Characters.Count; c++)
            {
                int character = c;
                var slot = UiFactory.Rect(Characters.Names[c], lb, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(900f + c * 160f, -210f), new Vector2(140f, 140f));
                _characterRings[c] = UiFactory.Image(slot, Color.gray, ProceduralTextures.Circle());
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = _characterRings[c];
                button.onClick.AddListener(() => PickCharacter(character));
                Portrait(slot, c, 122f);
            }

            float x = 900f, top = -390f, step = 76f;
            _modeRow = new OptionRow(lb, "Mode", x, top, () => Settings.Mode == GameMode.Timed ? "Timed" : "Elimination",
                d => EditSettings(s => s.Mode = s.Mode == GameMode.Timed ? GameMode.Elimination : GameMode.Timed));
            _lengthRow = new OptionRow(lb, "Match length", x, top - step, () => $"{Settings.DurationSeconds / 60f:0} min",
                d => EditSettings(s => s.DurationSeconds = (int)Cycle(Durations, s.DurationSeconds, d)));
            _goalsRow = new OptionRow(lb, "Goals to knock out", x, top - step, () => Settings.GoalsToEliminate.ToString(),
                d => EditSettings(s => s.GoalsToEliminate = Wrap(s.GoalsToEliminate + d, MatchSettings.MinGoalsToEliminate, MatchSettings.MaxGoalsToEliminate)));
            _ballsRow = new OptionRow(lb, "Balls", x, top - step * 2f, () => Settings.BallCount.ToString(),
                d => EditSettings(s => s.BallCount = Wrap(s.BallCount + d, MatchSettings.MinBalls, MatchSettings.MaxBalls)));

            _lobbyStatus = UiFactory.Text(UiFactory.Rect("Status", lb, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-460f, 60f), new Vector2(700f, 100f)),
                "", 28, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleRight);
            _notice = UiFactory.Text(UiFactory.Rect("Notice", lb, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(1500f, 44f)),
                "", 30, MenuTheme.Cyan);
            UiFactory.Button(lb, "Leave", new Vector2(0f, 0f), new Vector2(240f, 80f), new Vector2(320f, 100f), MenuTheme.ButtonGrey, Leave);
            _start = UiFactory.Button(lb, "START", new Vector2(1f, 0f), new Vector2(-230f, 80f), new Vector2(360f, 110f), MenuTheme.ButtonGreen, () => NetSession.Host?.StartMatch());
            _ready = UiFactory.Button(lb, "Ready", new Vector2(1f, 0f), new Vector2(-230f, 80f), new Vector2(360f, 110f), MenuTheme.ButtonGreen,
                () => NetSession.Guest?.SetReady(!NetSession.Guest.IsReady));
            _readyLabel = _ready.GetComponentInChildren<Text>();

            NetSession.Notice += ShowNotice;
            Hide();
        }

        /// <summary>Call when the menu scene closes: stops listening and drops the notice handler.</summary>
        public void Dispose()
        {
            NetSession.Notice -= ShowNotice;
            _discovery.Dispose();
        }

        private LobbySettings Settings => NetSession.Host?.Lobby.Settings ?? NetSession.Guest?.Lobby?.Settings ?? new LobbySettings();

        // ------------------------------------------------------------------ show / hide

        /// <summary>Opens the browse screen, or the lobby if a session is already running (back from a match).</summary>
        public void Show()
        {
            if (NetSession.Active) ShowLobby();
            else ShowBrowse();
        }

        public void Hide()
        {
            _discovery.Stop();
            _browse.gameObject.SetActive(false);
            _lobby.gameObject.SetActive(false);
        }

        /// <summary>Android back / Escape.</summary>
        public void Back()
        {
            if (_lobby.gameObject.activeSelf) Leave();
            else { Hide(); _back(); }
        }

        private void ShowBrowse()
        {
            _lobby.gameObject.SetActive(false);
            _browse.gameObject.SetActive(true);
            _browseStatus.text = NetSession.EndReason ?? "";
            NetSession.EndReason = null;
            if (!_discovery.StartListening()) _browseStatus.text = "Can't search for games on this network. You can still join by IP.";
            _refresh = 0f;
        }

        private void ShowLobby()
        {
            _discovery.Stop();
            _browse.gameObject.SetActive(false);
            _lobby.gameObject.SetActive(true);
            RefreshLobby();
        }

        public void Update(float dt)
        {
            if (_browse.gameObject.activeSelf)
            {
                _discovery.Update(null);
                _refresh -= dt;
                if (_refresh <= 0f) { _refresh = 0.25f; RefreshList(); }
            }
            else if (_lobby.gameObject.activeSelf)
            {
                if (!NetSession.Active) { ShowBrowse(); return; } // rejected, kicked or the host left
                RefreshLobby();
                if (_noticeTime > 0f && (_noticeTime -= dt) <= 0f) _notice.text = "";
            }
        }

        // ------------------------------------------------------------------ browse

        private string PlayerName => PlayerNames.Normalise(_name.text);

        private void SaveName(string value)
        {
            var profile = ProfileStore.Load();
            profile.PlayerName = PlayerNames.Normalise(value);
            _name.text = profile.PlayerName;
            ProfileStore.Save();
        }

        private static int SavedCharacter => ProfileStore.Load().LastMatch.PlayerCharacter;

        private void Host()
        {
            SaveName(_name.text);
            try
            {
                NetSession.StartHosting(PlayerName, SavedCharacter);
                ShowLobby();
            }
            catch (Exception e)
            {
                _browseStatus.text = e.Message;
            }
        }

        private void JoinListed(int index)
        {
            if (index >= _shown.Count) return;
            var match = _shown[index];
            if (!match.Beacon.Joinable) { _browseStatus.text = $"Can't join: {match.Beacon.Status.ToLowerInvariant()}."; return; }
            Join(match.Address, match.Beacon.GamePort);
        }

        private void JoinByAddress()
        {
            string address = _address.text.Trim();
            if (address.Length == 0) { _browseStatus.text = "Type the host's IP address first."; return; }
            Join(address, NetProtocol.GamePort);
        }

        private void Join(string address, ushort port)
        {
            SaveName(_name.text);
            try
            {
                NetSession.Join(address, PlayerName, SavedCharacter, port);
                ShowLobby();
            }
            catch (Exception e)
            {
                _browseStatus.text = e.Message;
            }
        }

        private void RefreshList()
        {
            _shown.Clear();
            _shown.AddRange(_discovery.Found);
            _shown.Sort((a, b) => b.Beacon.Joinable.CompareTo(a.Beacon.Joinable));
            for (int i = 0; i < ListRows; i++)
            {
                bool has = i < _shown.Count;
                _matchButtons[i].gameObject.SetActive(has);
                if (!has) continue;
                var b = _shown[i].Beacon;
                string mode = b.Mode == GameMode.Elimination ? "Elimination" : "Timed";
                _matchLabels[i].text = $"{b.MatchName}   {mode}   {b.Status}";
                _matchButtons[i].interactable = b.Joinable;
            }
            _emptyList.gameObject.SetActive(_shown.Count == 0);
        }

        // ------------------------------------------------------------------ lobby

        private void Leave()
        {
            NetSession.End();
            ShowBrowse();
        }

        private void PickCharacter(int character)
        {
            ProfileStore.Load().LastMatch.PlayerCharacter = character;
            ProfileStore.Save();
            if (NetSession.Host != null) NetSession.Host.SetHostCharacter(character);
            else NetSession.Guest?.SetCharacter(character);
        }

        private static void EditSettings(Action<LobbySettings> edit)
        {
            var host = NetSession.Host;
            if (host == null) return;
            var s = host.Lobby.Settings.Clone();
            edit(s);
            host.SetSettings(s);
        }

        private void ShowNotice(string text)
        {
            _notice.text = text;
            _noticeTime = 3f;
        }

        private void RefreshLobby()
        {
            var host = NetSession.Host;
            var guest = NetSession.Guest;
            LobbyState lobby = host != null ? host.Lobby : guest?.Lobby;
            bool isHost = host != null;
            int mySeat = isHost ? NetHost.LocalSeat : guest != null ? guest.LocalSeat : -1;

            if (lobby == null)
            {
                _lobbyTitle.text = "CONNECTING...";
                _lobbyHint.text = "";
                foreach (var row in _seats) row.Hide();
                _start.gameObject.SetActive(false);
                _ready.gameObject.SetActive(false);
                _lobbyStatus.text = "";
                return;
            }

            _lobbyTitle.text = lobby.MatchName.ToUpperInvariant();
            _lobbyHint.text = isHost
                ? $"Others join from Play Local, or by IP {LanDiscovery.LocalAddress()}"
                : "The host picks the options and starts the match.";

            for (int i = 0; i < _seats.Length; i++) _seats[i].Refresh(lobby.Seats[i], isHost, i == mySeat);

            int myCharacter = mySeat >= 0 ? lobby.Seats[mySeat].Character : 0;
            for (int c = 0; c < Characters.Count; c++)
                _characterRings[c].color = c == myCharacter ? MenuTheme.Cyan : new Color(0.25f, 0.28f, 0.35f);

            bool timed = lobby.Settings.Mode == GameMode.Timed;
            _lengthRow.SetVisible(timed);
            _goalsRow.SetVisible(!timed);
            foreach (var row in new[] { _modeRow, _lengthRow, _goalsRow, _ballsRow })
            {
                row.SetEditable(isHost);
                row.Refresh();
            }

            _start.gameObject.SetActive(isHost);
            _ready.gameObject.SetActive(!isHost);
            if (isHost)
            {
                // Only the host needs the Modes unlock; guests play whatever the host owns (US-8.3).
                string blocker = EntitlementStore.Current.CanHost(lobby.Settings.Mode)
                    ? host.StartBlocker()
                    : "Elimination needs the Modes unlock (see Store).";
                _start.interactable = blocker == null;
                _lobbyStatus.text = blocker ?? "Everyone's ready!";
            }
            else
            {
                bool ready = guest.IsReady;
                _readyLabel.text = ready ? "Not ready" : "Ready";
                _ready.image.color = ready ? MenuTheme.ButtonGrey : MenuTheme.ButtonGreen;
                _lobbyStatus.text = ready ? "Waiting for the host to start..." : "Tap Ready when you're set.";
            }
        }

        // ------------------------------------------------------------------ helpers

        private static RectTransform Box(Transform parent, string title)
        {
            var box = UiFactory.Rect("Box", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1640f, 960f));
            UiFactory.Image(box, MenuTheme.Panel);
            var t = UiFactory.Text(UiFactory.Rect("Title", box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1400f, 90f)),
                title, 64, MenuTheme.Cyan);
            t.name = "Title";
            return box;
        }

        private static void Label(Transform parent, string text, float x, float y, float width)
            => UiFactory.Text(UiFactory.Rect(text, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y), new Vector2(width, 50f)),
                text, 34, Color.white, TextAnchor.MiddleLeft);

        private RawImage Portrait(Transform parent, int character, float size)
        {
            var mask = UiFactory.Rect("Mask", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            UiFactory.Image(mask, Color.white, ProceduralTextures.Circle()).raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var portrait = UiFactory.Stretch("Portrait", mask).gameObject.AddComponent<RawImage>();
            portrait.texture = _portraits[character];
            portrait.raycastTarget = false;
            return portrait;
        }

        private static int Wrap(int value, int min, int max) => value < min ? max : value > max ? min : value;

        private static float Cycle(float[] options, float current, int delta)
        {
            int i = Array.IndexOf(options, current);
            if (i < 0) i = 0;
            return options[(i + delta + options.Length) % options.Length];
        }

        /// <summary>One seat in the lobby: colour, portrait, name, status, and the host's controls.</summary>
        private sealed class SeatRow
        {
            private readonly LanMenu _menu;
            private readonly int _seat;
            private readonly RectTransform _root;
            private readonly RawImage _portrait;
            private readonly Text _name, _status;
            private readonly Button _change, _easier, _harder;
            private readonly Text _changeLabel;

            public SeatRow(LanMenu menu, Transform parent, int seat, float y)
            {
                _menu = menu;
                _seat = seat;
                _root = UiFactory.Rect($"Seat {seat}", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, y), new Vector2(800f, 134f));
                UiFactory.Image(_root, new Color(0.08f, 0.1f, 0.15f, 0.9f));
                var stripe = UiFactory.Rect("Colour", _root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(14f, 134f));
                UiFactory.Image(stripe, SimSpace.SeatColors[seat]);
                var slot = UiFactory.Rect("Portrait", _root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(30f, 0f), new Vector2(110f, 110f));
                _portrait = menu.Portrait(slot, 0, 110f);
                _name = UiFactory.Text(UiFactory.Rect("Name", _root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(160f, -14f), new Vector2(380f, 50f)),
                    "", 36, Color.white, TextAnchor.MiddleLeft);
                _status = UiFactory.Text(UiFactory.Rect("Status", _root, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(160f, 14f), new Vector2(380f, 44f)),
                    "", 28, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);
                _change = UiFactory.Button(_root, "Change", new Vector2(1f, 0.5f), new Vector2(-110f, 22f), new Vector2(190f, 64f), MenuTheme.ButtonBlue,
                    () => NetSession.Host?.CycleSeat(_seat));
                _changeLabel = _change.GetComponentInChildren<Text>();
                _easier = UiFactory.Button(_root, "-", new Vector2(1f, 0.5f), new Vector2(-170f, -36f), new Vector2(70f, 46f), MenuTheme.ButtonGrey, () => StepDifficulty(-1));
                _harder = UiFactory.Button(_root, "+", new Vector2(1f, 0.5f), new Vector2(-50f, -36f), new Vector2(70f, 46f), MenuTheme.ButtonGrey, () => StepDifficulty(1));
            }

            private void StepDifficulty(int delta)
            {
                var host = NetSession.Host;
                if (host == null) return;
                var level = host.Lobby.Seats[_seat].Difficulty;
                host.SetSeatDifficulty(_seat, (AIDifficulty)Wrap((int)level + delta, 0, 3));
            }

            public void Hide() => _root.gameObject.SetActive(false);

            public void Refresh(LobbySeat seat, bool isHost, bool isMe)
            {
                _root.gameObject.SetActive(true);
                bool filled = seat.Type == SeatType.Human || seat.Type == SeatType.AI;
                _portrait.gameObject.SetActive(filled);
                _portrait.texture = _menu._portraits[seat.Character % Characters.Count];

                switch (seat.Type)
                {
                    case SeatType.Human:
                        _name.text = isMe ? $"{seat.Name} (you)" : seat.Name;
                        _status.text = seat.IsHost ? "Host" : seat.Ready ? "Ready" : "Not ready";
                        _status.color = seat.IsHost || seat.Ready ? new Color(0.4f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.7f);
                        break;
                    case SeatType.AI:
                        _name.text = $"AI {Characters.Names[seat.Character % Characters.Count]}";
                        _status.text = seat.Difficulty.ToString();
                        _status.color = new Color(1f, 1f, 1f, 0.7f);
                        break;
                    case SeatType.Open:
                        _name.text = "Open";
                        _status.text = "Waiting for a player";
                        _status.color = new Color(1f, 1f, 1f, 0.5f);
                        break;
                    default:
                        _name.text = "Closed";
                        _status.text = "No one plays here";
                        _status.color = new Color(1f, 1f, 1f, 0.5f);
                        break;
                }

                bool canChange = isHost && !seat.IsHost;
                _change.gameObject.SetActive(canChange);
                _changeLabel.text = seat.Type == SeatType.Human ? "Remove" : seat.Type == SeatType.Open ? "Add AI" : seat.Type == SeatType.AI ? "Close" : "Open";
                _easier.gameObject.SetActive(canChange && seat.Type == SeatType.AI);
                _harder.gameObject.SetActive(canChange && seat.Type == SeatType.AI);
            }
        }
    }
}

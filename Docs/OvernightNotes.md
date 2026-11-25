# Overnight work: what's new and how to try it

All of this is committed **locally** on `main` and not pushed yet. All 62 engine-free tests pass. Every script
also compiles against Unity's own libraries (UnityEngine, Unity Transport, Unity IAP). It has **not** been
opened in the Unity editor yet.

## First thing to do

1. Focus the Unity window and let it import. If anything shows red in the Console, tell Claude.
2. Optional: run **Window > General > Test Runner > EditMode** to see the same 62 tests inside Unity.
3. Open the **Boot** scene and press Play.

## New features

| Feature | Where | Notes |
|---|---|---|
| **LAN multiplayer** | Main menu > Play Local | Host a game or join one on the same Wi-Fi or phone hotspot (or by IP). Lobby: add AI, close or open seats, remove players, set AI difficulty, pick your driver, mode, length or goals, balls, Ready and Start. Rematch and back to lobby work. If a player drops, an AI takes over their seat. |
| **Settings** | Main menu > Settings | Volume, sound effects, music (no music yet), vibration, graphics level, 30 or 60 fps, blast button side (left-handed), player name. |
| **Sound effects** | Everywhere | Placeholder sounds made in code: blast, hits, bounces, goals, countdown, Go, knock-out, win and lose, and button clicks. Swap in real sound files later. |
| **Vibration** | Matches (Android) | 20 ms pulse on your blast, 40 ms when you concede. |
| **Modes unlock** | Main menu > Store | One product, `modes_unlock`. Solo Elimination gives one free try, then opens the store. A LAN host needs the unlock to start Elimination; guests don't. In the editor, Unity IAP uses a fake store, so you can test buying. |
| **Resume match** | Main menu on launch | An offline match saves every 10 s and whenever the app is backgrounded. On the next launch the menu asks "Resume your match?". |
| **Meshy tool** | `Tools/Meshy` | Turns the concept views into 3D models. See `Tools/Meshy/README.md`. It needs your `MESHY_API_KEY`. |

## Testing LAN

- **Two devices:** both on the same Wi-Fi or hotspot. One taps Play Local > Host a game; the other should see it
  under "Games nearby" within 1–2 s.
- **One PC:** build a Windows player (File > Build Profiles > Windows) and run it next to the editor. Host in
  one, join in the other. If the game doesn't appear in the list, use "Join by IP" with `127.0.0.1`.
- On Android, the multicast lock and Wi-Fi permissions are added to the manifest automatically at build time.

## Needs you

- **Push:** say "push" and Claude will push these commits.
- **Meshy key:** run `setx MESHY_API_KEY "<key>"`, open a new terminal, then
  `.\Tools\Meshy\Meshy.ps1 -Job bolt` to try one model, about 35 credits. It asks before spending.
- **Google Play:** create the `modes_unlock` in-app product in Play Console, and set its price there. On-device
  receipt validation (TDD 10.2) still needs the Play public key; it's marked TODO in `PurchaseService.cs`.
- **Design question:** in timed matches between AIs, sudden death sometimes ran for more than 80 seconds.
  Possible fixes: speed the balls up during sudden death, add a ball, or cap sudden death and call a draw.

## Not done yet

Online play (EOS), tutorial, AI personalities, power-ups, skins, music, connection-quality bars, rejoin after a
drop, and the "Away" AI for a guest who backgrounds the app.

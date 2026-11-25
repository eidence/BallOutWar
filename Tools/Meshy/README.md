# Meshy: concept art to 3D models

`Meshy.ps1` sends the concept views in `Docs/Concepts/views/` to [Meshy](https://www.meshy.ai) and saves the
resulting models into `Assets/Art/Models/<job>/` (FBX, GLB and texture maps). Character models are also
rigged, which adds `<job>_rigged.fbx` plus Meshy's walking and running clips.

## One-time setup

1. Create an API key at <https://www.meshy.ai/developers/keys> (a paid Meshy plan is needed to create tasks).
2. Store it for your user, then open a new terminal:

   ```powershell
   setx MESHY_API_KEY "<your key>"
   ```

## Use

Run these from the repository root:

```powershell
.\Tools\Meshy\Meshy.ps1 -List              # jobs and their state
.\Tools\Meshy\Meshy.ps1 -Job bolt -DryRun  # see the request, send nothing
.\Tools\Meshy\Meshy.ps1 -Job bolt          # one job (asks before spending credits)
.\Tools\Meshy\Meshy.ps1 -All               # everything: about 10 models + 4 rigs, roughly 320 credits
```

Start with one character, check it in Unity, then run the rest. Progress is saved in `Tools/Meshy/tasks/`,
so running again picks up where it left off without paying twice. To regenerate a job, delete its
`tasks/<job>.json` and run it again. Preview renders land in `Docs/Concepts/models/`.

## Jobs

`jobs.json` lists the jobs. Each has 1 image (image-to-3d) or 2–4 images (multi-image-to-3d, front view
first). `options` are passed straight to Meshy, overriding `defaults`. Useful ones: `target_polycount`
(mobile-friendly counts are set: 8k characters, 6k car, 2–3k props), `enable_pbr`, `pose_mode`
(`a-pose` helps rigging), `texture_prompt`.

## Making the input views

`SplitSheet.ps1` cuts a concept sheet into clean single images:

```powershell
.\Tools\Meshy\SplitSheet.ps1 -Sheet Docs\Concepts\02-bolt-turnaround-v1.png -Out Docs\Concepts\views\bolt -Mode Turnaround
.\Tools\Meshy\SplitSheet.ps1 -Sheet Docs\Concepts\25-arena-parts-v1.png -Out Docs\Concepts\views\part -Mode Objects -Join 8 -MinFraction 0.15
```

Turnaround mode writes the figures left to right (front, 3/4, side, back); rename them to match `jobs.json`.
When the round-2 turnarounds with goggles and scarves arrive, re-split them over the current views.

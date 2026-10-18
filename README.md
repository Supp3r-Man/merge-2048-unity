# merge-2048-unity

![ci](https://github.com/Supp3r-Man/merge-2048-unity/actions/workflows/ci.yml/badge.svg)

**Merge Up**: a small slide-and-merge number puzzle for **Unity 6** in the style of 2048. Slide every tile with the arrow keys, equal tiles merge, a new tile appears after each move, and you try to build a 2048 tile. It has undo, a "keep playing" choice after the win, a best score, and a replay log that reproduces any game from its seed.

> **What is verified, and what is not.** I wrote this on a machine with **no Unity editor installed**, so **the game has never been opened or played in Unity**. What *was* checked:
> - The rules (sliding, single merge per move, scoring, spawning, move validity, win and continue, game over, undo, replay) live in plain C# under `Assets/Scripts/Core` with no Unity types, and **74 tests pass** on them, including a brute-force comparison of the slide against a separately written reference over every 4-cell row of {0, 2, 4, 8} plus thousands of random rows. I broke the rules on purpose **30 ways** and the tests failed every time; one break first survived (the win threshold, because every win test used 1024 + 1024), which is why the exact-threshold test exists.
> - The Unity-facing scripts compile against **hand-written stubs** of the Unity API, under each input-handling configuration. That catches typos and type mistakes in my code. It does **not** check that I used the real Unity API correctly, and it says nothing about how the game looks or feels.
> - Not checked at all: `Assets/Editor/BuildScript.cs`, the `build-unity` workflow (needs a Unity license, never run), the pinned editor version `6000.0.23f1`, the `com.unity.inputsystem` 1.11.2 package version, and the Unity side of everything. The `Tests/LogicTests` project also built and passed under .NET 8 in GitHub Actions after the first push (locally I only had the old .NET Framework C# 5 compiler); that job covers `Core` only.

## Play it

1. Install **Unity 6** (any 6000.0.x; Hub will offer to upgrade the pinned version if yours differs).
2. Unity Hub → **Add project from disk** → pick this folder. The first open imports everything and generates the `.meta` files (commit them afterwards; Unity expects them in version control).
3. Open any scene (or just the default empty one) and press **Play**. There is no scene to wire up: `Bootstrap.cs` builds the camera, board, tiles and HUD at runtime.

| Key | Action |
|---|---|
| Arrow keys / WASD | Slide all tiles |
| U / Z / Backspace | Undo (up to 10 moves) |
| N / R | New game |
| Enter / Space | "Keep going" after reaching 2048 |

**Rules.** A slide moves every tile as far as it can. Two equal tiles that meet merge into one of double value and add that value to the score; a tile made by a merge cannot merge again in the same move, so `[2,2,2,2]` becomes `[4,4,0,0]` and `[2,2,2,0]` becomes `[4,2,0,0]`. A move that changes nothing is ignored (no new tile, no score). After a move that changed the board, a 2 (90%) or a 4 (10%) appears in a random empty cell. Making a 2048 tile pauses the game with a choice to keep going or start over. The game ends when the board is full and no two neighbours are equal. The board is 4x4 in the game; the core supports any size from 2x2 up.

## Layout

```
Assets/Scripts/Core/      engine-free rules: Board (slide, merge, trace), Game (spawn, win, game over, undo, action log),
                          Replay, Direction, HighScore, Rng (no Unity types anywhere in this folder)
Assets/Scripts/           Unity layer: Bootstrap, GameController, InputReader, SpriteFactory, Palette, Sfx
Assets/Editor/            BuildScript (menu "Merge Up" + batch-mode entry points)
Tests/LogicTests/         console test runner that compiles Core without Unity (dotnet run)
.github/workflows/        ci.yml (runs the logic tests), build-unity.yml (reference, needs a Unity license)
```

Design choices worth knowing:
- **Rules separate from engine.** The Unity layer only draws and animates; it asks `Core` what happened. `Board.Trace` returns where every tile goes for a move, so the slide animation can be driven by the core instead of guessing, and the tests cross-check the trace against the real move.
- **Deterministic.** Spawns use a small xorshift generator, not `UnityEngine.Random`, so a seed always gives the same game on every platform. Undo restores the generator state, so redoing a move gives the same spawn. The game keeps an action log (`L R U D C` for slides and "continue"); `Replay.Run(size, seed, log)` rebuilds the exact final board and score.
- **No assets.** One generated rounded-square sprite tinted per tile value (hue and brightness computed from the value, so any tile has a colour), synthesised sounds, and numbers drawn with IMGUI. Nothing to import, nothing binary in git.
- **No hand-written scene.** Scene files are YAML that is easy to get subtly wrong without the editor, so the scene is empty and the game builds itself at start-up. `Merge Up → Create Scene` makes the empty `Main.unity` for builds.
- **Either input system.** `InputReader` compiles for the old Input Manager, the new Input System, or both.

## Run the tests

```
dotnet run --project Tests/LogicTests -c Release     # needs the .NET 8 SDK
```

It prints `74 passed, 0 failed` plus two `info:` lines with the bot measurements below, and exits non-zero on any failure. (Locally, with no .NET SDK, I compiled the same sources with the old .NET Framework C# 5 compiler, which is why `Core` and the tests avoid newer C# syntax.)

### Measured with the test bots

These are measurements from fixed seeds, not guarantees; the tests only assert the invariants (games end, tiles stay powers of two, the tile sum grows by exactly the spawned tile each move, and the look-ahead bot wins at least once so the win path is exercised).

| Bot | Games | Reached 2048 | Notes |
|---|---|---|---|
| Corner bot (priority Left, Down, Right, Up) | 200 seeds, 4x4 | **0** | Best tiles: 32 in 2 games, 64 in 23, 128 in 81, 256 in 92, 512 in 2 |
| Look-ahead bot (two-move expectimax on a snake-weighted heuristic) | 40 seeds, 4x4 | **32** | Mean best tile 2233 |

A one-move greedy version of the heuristic reached 2048 in 0 of 60 games at every weight I tried (mean best tile 185 to 297), which is why the test bot looks two moves ahead.

## Build and deploy

- **In the editor:** menu **Merge Up → Build WebGL** (output `Builds/WebGL`, compression disabled so any static host works) or **Build Windows**.
- **Batch mode:** `Unity -batchmode -quit -projectPath . -executeMethod MergeUp.Editor.BuildScript.BuildWebGL`.
- **GitHub:** this repo is the source. `build-unity.yml` is a manual (`workflow_dispatch`) game-ci pipeline that needs `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` secrets ([how](https://game.ci/docs/github/activation)). I have not run it. Hosting a playable WebGL build on GitHub Pages is a follow-up: build, then publish the `Builds/WebGL` folder.

## Steps taken

1. **Checked the template and the machine.** Reused the layout of `orb-rush-unity`. No Unity editor and no .NET SDK here; the only compiler is the old .NET Framework `csc` (C# 5), so `Core` and the tests use C# 5 syntax.
2. **Wrote `Core`**: `Board` (single-line slide with a merge-once rule, whole-board move, `HasMoves`, `Trace` for animation), `Game` (spawn, win and continue, game over, undo snapshots including the generator state, action log), `Replay`.
3. **Wrote the tests** (dependency-free runner): known lines, a brute-force reference slide written differently from the real one, direction symmetry (right is mirrored left, up and down are transposed left and right, rotating the board clockwise turns up into right and so on), trace-versus-move consistency, spawning, win and continue, game over, undo, replay, bots.
4. **First run: 71 of 72 passed.** The failure was my own arbitrary threshold (it assumed the corner bot never ends below 64; two games ended at 32). I replaced the assertion with the measurement instead of tuning it to pass. That run also showed the corner bot **never** reaches 2048, so nothing exercised the win-then-continue path through replay.
5. **Wrote a stronger bot.** One-move greedy on a snake heuristic won 0 of 60 games at nine settings; two-move expectimax won 11 of 12 in a trial, so it became the test bot, and a won game is replayed from its log including the "continue".
6. **Fixed my own sloppy tests**: a hand-computed expected board for the downward slide replaced a garbage placeholder, and scenarios that depended on the random spawn were rewritten to be deterministic.
7. **Mutation-checked** the rules with a script that reports "pattern missed" instead of silently passing: 30 deliberate breaks (merge-again, merge score, direction mapping, moved flag, game-over checks, spawn rate and value, spawn on a rejected move, undo not restoring generator, score, log or win flag, undo limit, win threshold, ...). One survived (win at 1024); I added an exact-threshold test and all 30 were then caught.
8. **Wrote the Unity layer**: runtime bootstrap, generated sprite and sounds, slide animation interpolated from `Board.Trace`, IMGUI HUD and overlays, undo and new game, best score in `PlayerPrefs`, dual input path.
9. **Compiled the Unity layer against stubs** with four combinations of `ENABLE_LEGACY_INPUT_MANAGER` / `ENABLE_INPUT_SYSTEM`. All compiled. (Caveat above: that proves my code is self-consistent, not that the Unity API usage is right.)
10. **Wrote the editor build script, workflows and test project.** I dropped warnings-as-errors from the test project because I could not run `dotnet` here to see what the .NET 8 compiler would add; the old compiler's build printed no warnings.

## Known gaps

- Never run in Unity: how it looks and feels, the slide timing (0.12 s) and the colours are guesses. Number labels are positioned by converting tile positions to screen pixels, which I could not see working.
- No `.meta` files yet, no committed scene, no ProjectSettings beyond the version file; Unity fills these in on first open.
- Keyboard only: no swipe, touch or gamepad input. The in-game board is fixed at 4x4 although the rules support other sizes.
- No merge "pop" effect; only the newly spawned tile animates in.
- The bot numbers come from small fixed seed sets (200 and 40 games) and a heuristic I wrote; they say nothing about how a person fares.

## License

MIT

# Operation Phainon: Earth to Deep Space

A first-person spacecraft cockpit simulator for Unity 6. You sit in the command seat of a fictional crew capsule, bring it to life switch by switch, fly it from the launch pad through staging into orbit, and then, if you choose, out of Earth orbit altogether.

The spacecraft and cockpit are original designs inspired by modern crew capsules. No agency logos or insignia are used.

## Download

Playtest builds for Windows and macOS are on this repository's **Releases** page. Setup steps are in `PLAYTEST_GUIDE.txt`, which is also inside each archive.

The 0.1.0 builds are an untested pre-release: they were made before the launch-effects work and have not been run on a Windows or Mac machine.

## Current status

Read this first.

- All C# compiles cleanly against Unity 6000.6.9f1's own assemblies, and the simulation test suite (55 tests, including pad-to-orbit flights with every fault, a coast to a million kilometres and the whole of story Mission 01) passes. Both checks run outside the editor with `Tools/verify_without_unity.sh`.
- **Story Mode (added 2026-11-15) has not been run in the editor at all.** Its simulation is tested end to end, and its Unity layer (menu, introduction, wake-up, Jupiter in the sky shader, log, save file, the four new panel controls) type-checks, but nobody has yet seen it on screen. Expect layout and look to need a pass.
- Played in the Unity editor on 2026-11-14 from power-up through staging to orbital insertion, with a clean Console.
- The Ground Control radio was seen working in the editor on 2026-11-14.
- Faults, the departure from Earth orbit, time acceleration and the long-distance sky are written, tested as simulation and type-checked, but **have not yet been confirmed by eye in the editor**. The same goes for the launch effects (plumes, pad cloud, vapour, ascent trail, chase camera).
- Windows and macOS playtest packages have been built from Linux, but **no player build has been run on any platform**. Art and audio are placeholders.

## Requirements

- Unity **6000.6.9f1** (Unity 6.6) with the build support module for each platform you want to package (Mono). Other Unity 6 versions should work but are untested.
- An activated Unity licence (the free Personal licence is enough).
- No downloads: every package used ships with the editor.

## First run

1. In Unity Hub, sign in and activate a licence if you have not already.
2. **Add** this folder as a project and open it with 6000.6.9f1. The first import takes several minutes.
3. When the import finishes, the project builds itself: it generates the materials, meshes, config assets and `Assets/Phainon/Scenes/Mission.unity`. Watch the Console for a line starting `[Phainon] Layout check`.
4. If the scene did not open on its own, choose **Phainon ▸ Open Mission Scene**.
5. Press **Play**.

The editor regenerates the scene by itself whenever the builder scripts change (see `BuildVersion` in `ProjectBuilder.cs`). To force it, use **Phainon ▸ Build Project (assets + scene)**. Config assets you have tuned in `Assets/Phainon/Data` are kept; the scene is rebuilt from scratch, so hand edits to the scene are lost.

If the Console shows pink-material or shader errors, they come from `Assets/Phainon/Shaders`. The game logic does not depend on them.

## Building a playtest package

In the editor: **Phainon ▸ Build Playtest Package ▸ Windows (.exe)**, **▸ macOS (.app)** or **▸ Linux**. This builds the player, adds `PLAYTEST_GUIDE.txt` as the package's README, and writes a single archive to `Builds/`, ready to send (a `.zip` for Windows, a `.tar.gz` for macOS and Linux so the executable permission survives).

Windows and macOS builds each need their module: Unity Hub ▸ Installs ▸ gear icon on 6000.6.9f1 ▸ Add modules ▸ **Windows Build Support (Mono)** / **Mac Build Support (Mono)**.

The macOS build is Intel-only and unsigned, because Unity on Linux cannot code-sign and macOS refuses unsigned Apple-silicon binaries. It runs on Apple silicon through Rosetta. Testers must clear the quarantine flag once (`xattr -cr OperationPhainon.app`); the playtest guide explains how. For a wider release, build and notarise on a Mac.

From a terminal that is outside any sandbox, with Unity Hub running and signed in:

```bash
~/Unity/Hub/Editor/6000.6.9f1/Editor/Unity -batchmode -quit -projectPath . -executeMethod Phainon.EditorTools.ProjectBuilder.BuildWindowsPlayer -logFile -
```

## Story Mode

**New Story** on the main menu starts Operation Phainon's campaign. Mission 01: you wake in the seat, on batteries, with alarms going and Jupiter filling the window. Nothing tells you why you are there.

1. Silence the alarm, then bring the spacecraft back: APU, cabin loop, nav platform, radio. The guide gives each step.
2. Press `J` and read what the mission computer recovered.
3. NAV MODE to RETRO, FUEL PUMP and TANK PRESS on, GUIDANCE and RCS on. The left display shows time to periapsis and the delta-v needed to stay.
4. Coast in (`.` speeds time up), light the engine half a burn before periapsis, and cut it when ORBIT GO lights. Miss the burn and Jupiter is gone for good.
5. SCIENCE PWR on. Choose with INSTRUMENT, aim the window at Jupiter (GUIDANCE off, RCS on, `W` `S`), press OBSERVE. Three observations are needed; range, pointing, sunlight and instrument health decide the data quality.
6. Press TRANSMIT to send them home.

Earth is 43 light-minutes away, so Ground Control answers an hour and a half after you speak, and warns you about things you have already dealt with. With nothing waiting for a reply, `7` `8` `9` offer calls you can make.

The story saves by itself after each objective, and from the pause menu, to one file under the game's data folder (`Story/save.json`). It will not save while the engine is running or an observation is recording. **Continue Story** picks it up; **New Story** replaces it after asking.

Everything the mission says and every number it uses is in `Scripts/Simulation/Story/Content/Mission01Jupiter.cs`. A new mission is another file like it and one line in `StoryCampaignContent.cs`.

## Controls

| Action | Input |
|---|---|
| Look around | Hold right mouse button and move |
| Jump to a view | `1` window, `2` displays, `3` main panel, `4` left console, `5` right console, `6` overhead panel |
| Zoom | Hold `Z` or middle mouse button |
| Press a button, flip a switch | Left click |
| Guarded button (red frame) | First click lifts the guard, second click presses |
| Turn a dial | Hold left click and drag, or scroll over it |
| Throttle | Drag the lever, scroll over it, or `Shift` / `Ctrl`. `T` full, `X` idle |
| Fly by hand | `W` `S` pitch, `A` `D` yaw, `Q` `E` roll (GUIDANCE off, RCS on) |
| Toggle the objective guide | `H` |
| Answer Ground Control | `7` `8` `9` pick the numbered reply |
| Radio log | `C` |
| Call Ground Control (story, nothing awaiting a reply) | `7` `8` `9` |
| Log (story) | `J` |
| Skip the introduction or the wake-up | `Space` |
| Time rate (in orbit or beyond, engine off) | `.` faster, `,` slower: x1, x10, x100, x1000, x10000 |
| Pause | `Esc` or `P` |

The control you need next pulses amber, and the current objective is shown at the top left. Ground Control speaks on the radio panel at the top right once COMMS and S-BAND XMIT are on. One seeded fault happens each flight (APU trip, fuel pump, battery, nav platform, transmitter or a weak upper engine); the repair is the affected system's own switch, and the Console logs the seed so a flight can be repeated.

## Flying the mission

This is the Earth Launch Mission on the main menu.

1. **Pre-launch.** On the overhead panel: BATT A, BATT B and APU on. Press MAIN POWER. Then FUEL PUMP, TANK PRESS, NAV SYSTEM, NAV MODE to ASCENT, GUIDANCE, S-BAND XMIT, COMMS, CABIN SYS. The centre display shows a GO / NO-GO board. When every row is GO, press LAUNCH SEQ.
2. **Ignition.** Set full throttle. At T-6 lift the guard and press ENGINE IGNITION. The clamps release at T-0 if thrust is high enough; the countdown holds if any system drops out.
3. **Ascent.** Ease the throttle back through max-Q (a red warning tells you if you do not), then again if G load passes 4.
4. **Stage separation.** At booster burnout, lift the guard and press STAGE SEP, then ENGINE IGNITION for the upper stage.
5. **Orbital insertion.** Hold the burn. When ORBIT GO lights, press ENGINE CUTOFF.
6. **Outer space.** The mission is complete, and you can stay: GUIDANCE off and RCS on gives manual attitude control.
7. **Leaving Earth (optional).** Lift the guard and press STAGE SEP to drop the upper stage. Turn NAV MODE to ORBIT (the computer holds the nose along the flight path), with GUIDANCE and RCS on. Full throttle, ENGINE IGNITION, and hold the service module burn until APOAPSIS reads ESCAPE. Then coast: `.` speeds time up. Ground Control calls 10,000 km, 100,000 km, the Moon's distance and a million kilometres, and its answers take longer and longer to arrive. The aft camera looks back at Earth; with GUIDANCE off you can pitch the window round to it.

The mission fails if you run out of propellant short of orbit, fall back into the atmosphere, or press ABORT. Launch to orbit takes about seven minutes.

Limits of the departure: only Earth's gravity is modelled, so beyond about a million kilometres the path is no longer realistic; the Moon is a picture in the sky, not a place; Earth does not turn under you during a long coast.

## Project layout

```
Assets/Phainon/
  Scripts/Simulation/   Spacecraft systems, flight model and mission logic. Pure C#, no Unity types.
  Scripts/Runtime/      Unity layer.
    Core/               Game state, input, config assets, spacecraft host
    Cockpit/            Interaction framework, controls, lamps, camera, lighting
    Displays/           The three display units and their widgets
    Environment/        Sky and Earth driver, ascent effects, hull camera
    Audio/  UI/         Audio manager; HUD and menus
  Scripts/Editor/       Builders that generate assets and the scene
  Tests/EditMode/       Mission tests and a scripted pilot (Window ▸ General ▸ Test Runner)
  Shaders/              Sky/Earth, launch terrain, plume, particles, window glass
  Data/                 Generated config assets: vehicle, systems, mission, theme, environment, audio
  Art/  Audio/  Prefabs/  Scenes/
Tools/
  generate_audio.sh           Re-synthesises the placeholder sounds with ffmpeg
  verify_without_unity.sh     Compile check and simulation tests without the editor
```

## How it fits together

- **`Spacecraft`** (Simulation) owns every subsystem and ticks them in a fixed order. `Spacecraft.HandleControl(ControlId, value)` is the single entry point for crew commands, and `Spacecraft.GetIndicator(IndicatorId)` is the single source for lamp state.
- **Controls** (`InteractiveButton`, `InteractiveSwitch`, `InteractiveDial`, `ThrottleLever`) share the `Interactable` base. A control only knows its `ControlId` and reports a value; `CockpitControlRouter` passes it to the spacecraft.
- **Displays** each have their own world-space canvas and script, built from shared widgets, on the `CockpitDisplay` base that handles power, boot, brightness and refresh rate.
- **Faults** (`FaultSystem`, Simulation): one seeded fault per flight. Each is a latch inside the system it breaks, so the effect is that system's real behaviour and the repair is its own switch. The objective guide gives the procedure, and Ground Control advises only while the fault is still active.
- **Time acceleration** runs the same simulation in one-second slices; Unity's clock is untouched. `Spacecraft.TimeWarpInhibit` and `MaxTimeWarp` hold the rules.
- **Ground Control** (`GroundControl`, Simulation) transmits authored lines when real simulation events happen: mission events, warnings, link changes. A line is only heard with the radio link up and after the signal's travel time, telemetry in the text is read at the moment of delivery, and an instruction the crew have already carried out is dropped. Replies set story flags that change later lines. The characters and lines are data in `Data/GroundControlScript.asset`; `CommsPanel` shows them on the HUD.
- **Launch effects** are in `AscentEffects`: engine plumes, the pad exhaust cloud, condensation, the ascent trail and staging. Each is driven by engine output, altitude, speed and air density, and tuned in `EnvironmentConfig` (`effectsDensity` scales all particle rates). They are seen through the external camera on display 3; CAM SELECT on the overhead panel swaps it between the hull view and a chase view.
- **The cockpit never moves.** `EnvironmentDirector` rotates the world around it to match attitude and feeds altitude and orientation to the `Phainon/SpaceSky` shader, which draws sky, atmosphere, Earth, stars, Sun and Moon in one pass.
- **Tuning lives in data.** Vehicle masses and thrust, guidance gains, system rates, warning limits, mission thresholds and text, colours, interaction feel and sound levels are all in the assets under `Data/`.

### Adding things

- **A control:** call the relevant `ControlFactory` method in `CockpitBuilder`, add a `ControlId`, and handle it in `Spacecraft.HandleControl`. Or drop a prefab from `Prefabs/Controls` under the Cockpit object and set its Control Id; the router and lamps find it automatically.
- **A display:** add a script deriving from `CockpitDisplay` and a `Build…` method in `DisplayBuilder`.
- **A spacecraft system:** add a class under `Scripts/Simulation/Systems`, construct and tick it in `Spacecraft`.
- **A warning:** add a `WarningId` and one line in `Spacecraft.EvaluateWarnings`.

Because the simulation has no Unity dependency, it can run headless on a server, which is the natural seam for co-op or an AI mission controller later.

## Checking the code without the editor

```bash
Tools/verify_without_unity.sh
```

Runs the simulation tests on the editor's bundled Mono and type-checks every assembly against the editor's real DLLs. It cannot import assets, compile shaders or build the scene.

## Known limitations

- The flight model is a deliberate simplification: planar motion over a round, non-rotating Earth. Orbits are stable and apoapsis/periapsis are computed properly, but there is no plane change, no Earth rotation and no re-entry heating.
- Geometry is built from primitives and a few procedural meshes. Cockpit materials use small generated surface textures (grain, brushed metal, quilting), not authored texture maps.
- All audio is synthesised placeholder. Replace any file in `Assets/Phainon/Audio` with a recording of the same name.
- Ground Control is text only (no voice), covers the one mission, and its story flags are not saved between sessions.
- Faults: one per flight, each fixed by a single switch procedure. There is no wear model, no difficulty setting and no multi-step repair.
- Beyond orbit: Earth is the only body with gravity, there is no retrograde hold or RCS translation (nothing yet to brake for or dock with), and time acceleration is limited near Earth.
- One window size and one quality level have been considered. Settings are a master volume and a reduced-effects switch; there is no key rebinding or gamepad support.
- Story Mode: one mission and one save file. Jupiter is the only body with gravity there; its moons are not drawn. The orbit is flat (no inclination), so none is shown. Food, water and crew condition are not modelled. Three of the six instruments in the design exist.

## Credits

- `Assets/Phainon/Art/Textures/EarthDay.jpg`: NASA Blue Marble "Land, Ocean, Ice" composite, public domain (see `CREDITS.txt` beside it).
- `Assets/Phainon/Art/Textures/JupiterMap.jpg`: Cassini map of Jupiter, PIA07782, NASA/JPL/Space Science Institute, public domain.
- TextMesh Pro essential resources and the Liberation Sans font ship with Unity.
- Everything else is generated by this project's code.

# This fork: what it changes and why

Fork of [martijns/martijns-msfs2024-physics-camera](https://github.com/martijns/martijns-msfs2024-physics-camera) (base: upstream commit `dc1bce7`, release v1.7), used with MSFS 2024 on Linux (Steam, Proton). Everything below is on top of upstream; the README describes the original app.

**In short:**
1. **Look into turns.** The head turns towards the inside of a turn you hold, in proportion to the bank, faster after a sharp roll, never for small corrections. Seven sliders.
2. **Head motion pauses** while you walk around outside the aircraft (MSFS 2024 walkaround) and while the aircraft stands still on the ground, so a wreck that keeps jittering after a crash no longer shakes the view.
3. **Diagnostic log**: what the sim reported and what the app did, ten lines a second.
4. **Return-to-center (washout) default 0.7** instead of 0.5.
5. **A new look**: a calm dark theme, readable meters and sliders, built from plain WPF styles.

| Change | Status |
|---|---|
| Look into turns | flown and confirmed 2026-10-19 ("much better and realistic, on crashes too") |
| Pause on foot / standing still, diagnostic log | flown 2026-10-19: the log shows the pauses engaging (on foot at about 15 m from the aircraft, standing still in the cockpit); not separately commented on by the pilot |
| Continuous sweep in reversals (point 5), bounce-proof standstill | flown 2026-10-19 in a Hornet, a mid-speed jet and a Cessna: "I think we got it just right", except the Cessna felt sluggish |
| Looking ahead in a deliberate roll (point 6) | flown 2026-10-19/20: "much better I think". Log of that flight: at level crossings the head reached the centre a median 0.11 s after the wings (20 ordinary rolls, never more than about 0.5 s off; previous flight 0.22 s with cases up to 3 s), and on rolls into a turn it kept pace with the wings (previous flight: a median 2.25 s behind) |
| New look | flown since 2026-10-19 22:41; no problems reported, not separately commented on |

---

## 1. Look into turns

**Why:** in a coordinated turn the sideways force is close to zero, so the original effects never move the head towards the turn. Pilots look into a turn they have committed to, roughly in proportion to the bank, and not for every gust or heading correction.

**Sliders** (panel "Look into turns", saved in `settings.json`):

| Setting | Default | Range | Meaning |
|---|---|---|---|
| `LookIntoTurns` | on | | on/off |
| `TurnLookStart` | 10° | 0 to 30° | bank below this never turns the head |
| `TurnLookReaction` | 1.0 s | 0 to 3 s | the head follows the bank averaged over this time, so short wobbles average out |
| `TurnLookSpeed` | 1.0x | 0.25 to 3x | how fast the head moves to its target (1.0x settles in about 1.5 s) |
| `TurnLookSnap` | 1.0 | 0 to 3 | how much faster everything gets after a sharp roll (0 = always the same speed) |
| `TurnLookStrength` | 1.0x | 0 to 3x | head turn per degree of bank above the start |
| `TurnLookMax` | 25° | 0 to 60° | cap on the head turn |

Default rationale (flight training): a standard-rate turn needs about airspeed/10 + 7 degrees of bank (about 16° at 90 kt) and small heading corrections about half of that, so the head starts turning at 10° of held bank; a 20 to 25° look keeps the instruments in view.

**How it works** (`PhysicsEngine.cs`, after the roll axis):
1. Bank (`PLANE BANK DEGREES`) goes through a first-order low-pass with time constant `TurnLookReaction / urgency`.
2. Bank above the start angle eases in quadratically over `TURN_LOOK_SOFT_ZONE_DEG` (10°), then continues linearly with the same slope, so crossing the start angle gives no kick. Times `TurnLookStrength`, capped at `TurnLookMax`.
3. **Urgency** from the roll rate: the bank's rate of change (smoothed 0.15 s) above a gentle roll-in of 8°/s adds `TurnLookSnap` x 1 per 10°/s. It is held with a 2 s exponential decay, so the head finishes its turn after the wings stop rolling. Capped at 4x.
4. The head yaw is a critically damped spring (mass 1, omega = 2 rad/s x `TurnLookSpeed` x urgency) towards the target, integrated in sub-steps when fast (`c x dt` per step at most 0.5) so it never overshoots.
5. **Fast roll towards level while the head is turned** (`TRANSIT_*`): a roll-out, or the first half of a reversal. Without this, the start angle makes the target flat at zero for small banks, so in a fast reversal the head reached the centre early and waited there while the wings swept through level, and it stayed at the maximum while a steep bank came down (seen in a logged fighter reversal, +73° to -66° at about 100°/s: the head held 23° until the bank was down to 30°, then slowed to about 30°/s at the centre for about a third of a second). Now, when the head has been turned more than 2° within about the last second and the bank moves towards level faster than 6°/s (any deliberate roll, slow aircraft included), the target follows a straight line from the normal target at that bank to the centre exactly at level, using the actual bank rather than the averaged one, and carries on through level until the other side's normal target is larger; it never looks further out than at the start. The sweep ends only after the roll has stayed below 3°/s (or turned back) for 0.25 s, and if the roll picks up again in the same direction while the sweep is still fading, the same sweep continues (only while the bank has not gone back beyond where it started). After it, the normal target takes over again over 1 s.

6. **Looking ahead in a deliberate roll** (`LEAD_*`). Tied to the averaged bank and a gentle head, the head in a slowly rolling aircraft trailed the wings (a Cessna roll-in to 30° at 10°/s: head 90 % there about 2 s after the wings) and moved at 2 to 6°/s; the pilot called it sluggish. A roll counts as deliberate once the bank has moved the same way for 0.6 s and over 12° (longer and larger than a turbulence bump or a small correction; a reversal sweep that has passed level counts at once). Then the head aims at the actual bank plus half a second of roll (at most 5°, and never past where a slowing roll will stop: rate² / 2 x deceleration), skipping the averaging, and moves at least 3x its normal speed; in a sweep (point 5) the straight line uses the same look-ahead. Half a second because the app cannot know where the pilot will stop: with 1 s the head ran about 6° past its final angle in a Cessna roll-in and came back; with 0.5 s it arrives with the wings and settles by about 2°, and on a plain roll-out drifts at most about 1° past the centre.

Two earlier versions of point 5, kept as lessons: (a) a 40°/s threshold left slower jets out, and a jet rolling at about 45°/s kept switching the sweep on and off, each restart anchoring on the lagging head, which held the head at about 21° for two thirds of a second (logged 22:52:19); (b) anchoring on the head instead of the normal target let turbulence in a turn ratchet the head towards the centre (a 25° turn with ±5° turbulence: 2° instead of 10°, simulated).

**Units, learned the hard way:** FreeTrack yaw goes to the sim **1:1 in radians**. The first build used the x16 factor that upstream uses for pitch and roll, and a 15 to 25° look became a 240 to 400° spin: the pilot looked backwards. Fixed, and the output is clamped to 60° either way (`MAX_YAW_RAD` in `MainWindow.xaml.cs`) as a safety net.

**Simulated before flying** (the real `PhysicsEngine.cs` driven by synthetic bank profiles at 60 Hz):

| Scenario | Result |
|---|---|
| gentle roll 6°/s to 30° | head at 90 % of its turn after 7.6 s, ends at 14.8° |
| sharp roll 40°/s to 30° | 1.65 s (4.72 s with `TurnLookSnap` 0) |
| 90°/s to 45°, snap 3, speed 3 | 0.83 s; same with frame times of 0.005 to 0.05 s (stable) |
| wind wobble ±8° at 1 Hz, a 9° correction held 2 s, turbulence ±12° and ±15° | head stays at 0.0 to 0.1° |
| held 25° turn with ±5° turbulence | about 10°, steady |

Reversals with the settings flown on 2026-10-19 (start 10°, reaction 0.6 s, speed 1.1x, quicker 1.3, intensity 0.9x, max 23°), before and after point 5:

| Reversal | Slowest head speed near the centre | Time within 3° of centre |
|---|---|---|
| +60° to -60° at 60°/s | 5.3 → 23.4 °/s | 0.46 → 0.26 s |
| +60° to -60° at 120°/s | 26.6 → 45.2 °/s | 0.14 → 0.13 s |
| +60° to -60° at 200°/s | 50.2 → 67.3 °/s | 0.06 → 0.08 s |
| +30° to -30° at 60°/s | 4.0 → 24.4 °/s | 0.53 → 0.22 s |
| +30° to -30° at 8°/s (slow aircraft) | 0.0 → 3.3 °/s | 4.90 → 1.76 s |

Point 6, on realistic rolls (the aircraft eases into and out of the roll; same settings), before → after:

| Manoeuvre | Head reaches 90 % (wings at) | Peak head speed | Settles back |
|---|---|---|---|
| Cessna roll-in, level to 30° at 10°/s | 4.80 → 2.78 s (2.83 s) | 6.9 → 14.3 °/s | 1.8° |
| Cessna roll-out, 30° to level at 10°/s | 3.65 → 2.65 s (2.83 s) | 4.2 → 8.7 °/s | 0.9° past centre |
| Cessna reversal, 30° to -30° at 12°/s | 5.18 → 4.45 s (4.63 s) | 5.5 → 9.6 °/s | 1.9° |
| jet reversal, -50° to 60° at 45°/s | 2.33 → 2.13 s (2.33 s) | 22.5 → 32.1 °/s | none |
| fighter reversal, 60° to -60° at 120°/s | 1.28 → 1.18 s (1.07 s) | 45.9 → 66.1 °/s | none |

Small corrections and turbulence (level or in a turn) give the same numbers as before; slow wing rocking of ±15° every 2 s from level now moves the head by about ±1° (before ±0.3°). The same with sim data fed every third step: within 0.04 s and 0.1°.

Replaying the two logged reversals (sim data fed every third physics step, like the real app's uneven updates): the jet at about 45°/s now sweeps steadily at about 18 to 20°/s from -23° through the centre (in flight it hung at about -21° for 0.7 s), and the fighter at about 100°/s starts back as the roll begins and speeds up smoothly through the centre (in flight it held 23° until the bank was down to 16°). Turbulence inside a turn: average head angle within about 2° of before, wobble lower (standard deviation 0.99 → 0.50° at 20° with ±5°, 1.21 → 0.73° with ±8°); corrections, roll-ins, gentle turns and the slow cases from level give exactly the earlier numbers.

## 2. Pausing head motion: walking outside, standing still

**Why:**
- **Walkaround.** In MSFS 2024 `CAMERA STATE` stays 2 (cockpit) when the pilot gets out and walks (Shift+C), so upstream's "only in cockpit" check never pauses. Reported on the MSFS developer forum: [Camera & Walkaround](https://devsupport.flightsimulator.com/t/camera-walkaround/10674), [Variable for player (not plane) position when exiting plane](https://devsupport.flightsimulator.com/t/variable-for-player-not-plane-position-when-exiting-plane-in-msfs-2024/16775). The telemetry is always about the aircraft (`SIMCONNECT_OBJECT_ID_USER` = 0 = the user aircraft), so whatever the parked aircraft does reaches the walking pilot's view.
- **After a crash** the sim can keep the wreck jittering on the ground; its accelerations went straight into head shake.

**How:**
- **On foot:** a second data request reads `PLANE LATITUDE/LONGITUDE/ALTITUDE` of `SIMCONNECT_OBJECT_ID_USER_CURRENT` (MSFS 2024, value 0xFFFFFF81 in the managed SimConnect assembly: the aircraft while flying, the avatar on foot). When that position is more than 1 m (plus 0.1 s of travel at the current speed, because the two positions arrive as separate messages) from the aircraft's for 0.3 s, the pilot is out (`MainWindow.xaml.cs`, `DistanceM`).
- **Standing still:** `SIM ON GROUND` and `GROUND VELOCITY` below 1 kt; moving again above 2 kt, or after 0.5 s in the air. Nothing at a standstill should throw the head around; taxi bumps start well above 2 kt. The 0.5 s: a bouncing wreck flickers `SIM ON GROUND` off for a few tenths of a second (logged after a crash on 2026-10-19), while a helicopter lifting into a hover gets its head motion back after half a second.
- **Fade:** in all pause cases (also outside the cockpit view) the forces fade out and back in over 0.3 s (`MOTION_FADE_SEC`) instead of switching, so pausing gives no kick.
- The window shows `Head motion: on` or `paused (...)` with the reason.

**Not used, and why:** `CRASH FLAG` / `CRASH SEQUENCE` are reported to stay 0 for the user aircraft (they are logged, to check); `IS AVATAR` / `IS AIRCRAFT` are reported on the developer forum to flip during loading, and were not found on the SimVar pages checked.

**Simulated** (RMS of the head translation over the window, same harness):

| Scenario | Before | Now |
|---|---|---|
| wreck jitter ±8 ft/s², on the ground at 0.3 kt | 0.023 | 0.000 |
| same jitter, pilot on foot | 0.023 | 0.000 |
| taxi bumps at 15 kt, ±8 ft/s² at 3 Hz | 0.023 | 0.023 (unchanged) |
| braking 20 kt to a stop, peak in the 2 s after stopping | 0.044 | 0.037 (no extra kick) |

**Open:** whether `USER_CURRENT` really reports the avatar on foot and the aircraft when seated, on this sim build. The diagnostic log answers it (`userObjId`, `userDistM`, `onFoot`).

## 3. Diagnostic log

`logs/telemetry-<start time>.csv` next to the program, one file per sim session, ten lines a second, flushed line by line; the newest 10 files are kept. Columns: time, camera state, on ground, ground speed, body accelerations and velocities, bank, crash flag and sequence, the object IDs of the aircraft and current-user data, current-user distance, the pause flags, the fade gain, the head outputs, `turnReversal` (1 while the sweep of point 5 is active, fading to 0 after), `lookTargetDeg` (where look-into-turns steers the head), `urgency` and `turnLead` (1 while the head looks ahead, point 6). On by default; off with `"DiagnosticLog": false` in `settings.json` (no switch in the window; the app reads it at start). About 6 MB per hour of flying. This is how every fix above was checked against real flights; switch it on again to diagnose a new problem.

## 4. A new look

Upstream's window uses unstyled WPF controls; under Wine there is no Aero theme, so WPF falls back to the oldest Windows look. The fork restyles everything with plain WPF, no images, no extra libraries:
- `Theme.xaml`: one dark palette (colours at the top), cards, switches for the on/off settings, thin scrollbars, dark tooltips, a round slider thumb.
- `Meter.cs`: live readings as thin bars that fill from zero, drawn directly in `OnRender` (cheap at 60 updates a second). `MeterRow` = label, bar, value.
- `SettingSlider`: label and value on one line, the slider below, with a `Meter` as its track so ranges that go negative fill from zero.
- Switches and expander chevrons take their state from triggers (correct from the first frame); the switch knob only slides into place.
- **Fonts under Wine:** every `Segoe UI...` name resolves to Times New Roman there (WPF's own font lookup, measured), and WPF stops at the first name that resolves, so the list is `Adwaita Sans, Noto Sans, Segoe UI Variable Text, Segoe UI, Arial`: the GNOME font on Linux, Segoe UI on Windows.
- **Previewing without the sim:** `MsfsPhysicsCamera.exe --snapshot <file.png> [--expand]` renders the window with sample values to a picture and exits (no sim connection, no window shown, software rendering). Run it in any Wine/Proton prefix that has the .NET 10 Desktop Runtime; errors go to `<file.png>.error.txt`.

| Change | Status |
|---|---|
| New look | built and previewed 2026-10-19, in use since 22:41 the same evening; no problems reported |

## Running on Linux (Steam + Proton)

What it took on one machine (GE-Proton 11, Steam Linux Runtime 4):
- **.NET 10 Desktop Runtime** (`windowsdesktop-runtime-10.0.x-win-x64.exe`, `/install /quiet /norestart`) in the **sim's own** Proton prefix, run with the sim's Proton while the sim is closed.
- **Start it with the sim**, from the sim's `EXE.xml` (`Launch.Addon`, path as `Z:\...`). SimConnect is a named pipe that exists only inside the sim's Wine session, so the app must run in it; `EXE.xml` does that.
- **NPClient registry key** `HKCU\Software\NaturalPoint\NATURALPOINT\NPClient Location`, `Path` = the program folder with a trailing `\`, set in the sim's prefix before the sim starts (the app sets it itself too, but the sim reads it only at start).
- **Close it after the sim exits.** Under Proton any program still running in the sim's Wine session keeps Steam's launch alive (Steam shows "Running", the next start hangs). Something has to end it, for example a user service that sends SIGTERM once the sim's process is gone.
- **MangoHud**, if the sim runs with `MANGOHUD=1`: the app inherits it and draws an overlay in its window. A per-app config `~/.config/MangoHud/wine-MsfsPhysicsCamera.conf` with `no_display` hides it.
- **Never restart the app or replace its files while the sim runs.** A live swap (SIGTERM on the app and rewriting every file, `NPClient64.dll` included) crashed MSFS 2024 within the same second (access violation in the sim's own code). Put new files in place by rename and let the sim start them next time; `NPClient64.dll` is loaded into the sim and never needs replacing.

## Building on Linux

```bash
dotnet publish MsfsPhysicsCamera.csproj -c Release -p:Platform=x64 -p:EnableWindowsTargeting=true \
  -r win-x64 --self-contained false -o out
```

.NET 10 SDK for Linux. Without `-r win-x64` the apphost comes out as a Linux ELF. Delete `bin/` and `obj/` first when switching runtime identifiers, or publish fails with MSB3030 (deps.json / runtimeconfig.json "not found").

**Testing the physics without the sim:** a small net10.0 console project that compiles `PhysicsEngine.cs` (linked) plus a stand-in `SimConnectHandler.TelemetryData` struct, and drives `Update()` with synthetic telemetry. That is where every number above comes from.

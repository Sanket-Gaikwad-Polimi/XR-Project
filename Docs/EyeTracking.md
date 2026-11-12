# Eye tracking: Pupil Core + Pupil Capture (parts A, B, E, F)

The real eye tracker replaces the mouse as the gaze source of the driver twin. The zones, the attention buffer and "hazard seen" now run on real gaze. The mouse source stays for home testing.

Platform for now: the 3-monitor simulator with a head-worn Pupil Core tracker. The gaze code is written so that an XR headset can be added later (section 3).

**Status of this document.** Parts A, B, E and F are implemented. C (dropouts), D (accuracy check) and G (3-monitor rendering) are planned and described in section 12. Nothing has been tested with the real tracker yet. Every statement marked **TO VERIFY** must be checked in the lab.

---

## 1. Files

| File | What it does |
|---|---|
| `Scripts/Twin/GazeSample.cs` | One gaze sample for every source: world ray, valid flag, confidence, display, time stamp |
| `Scripts/Twin/GazeSource.cs` | Base class of all gaze sources. Each returns a `GazeSample`. |
| `Scripts/Twin/GazeSourceSwitch.cs` | Inspector choice before Play: **Mouse**, **PupilCapture** or **PupilFake** |
| `Scripts/Twin/PupilGazeSource.cs` | Part A: the Pupil Capture client (connection, surfaces, blinks, clock sync, status) |
| `Scripts/Twin/FakePupilServer.cs` | Part B: a fake Pupil Capture inside Unity, driven by the mouse |
| `Scripts/Twin/PupilMsgPack.cs` | Small msgpack reader/writer (Pupil's message format) |
| `Scripts/Twin/PupilNetMQ.cs` | Start-up and clean shut-down of the NetMQ library |
| `Scripts/Logging/DriveLogger.cs` | Part E: raw gaze file, new log columns, tracker summary in the meta file |
| `Scripts/HMI/DebugOverlay.cs`, `GazeDebugView.cs` | Part F: tracker line in the overlay, tracker gaze shown as a ring |
| `Assets/Plugins/NetMQ/` | `NetMQ.dll`, `AsyncIO.dll`, their licences and a README |
| `Analysis/check_drive.py` | New section "Eye tracker" |

---

## 2. Transport: native C# client (decision a)

Unity talks to Pupil Capture directly over ZeroMQ, using the **NetMQ** library. There is no Python bridge.

**Reference: Pupil Labs hmd-eyes.** This is Pupil's own Unity plugin for headsets, read on 2026-11-12 (last commit Nov 2022, so not actively maintained). Licence: **LGPL-3.0**. We did **not copy** its code. We followed its approach:
- the same **NetMQ 4.0.0.1** and **AsyncIO 0.1.69** DLLs (we checked that they are byte-identical to the NuGet packages),
- `AsyncIO.ForceDotNet.Force()` before the first socket,
- `NetMQConfig.Cleanup(false)` when the last socket is closed,
- reading the sockets on Unity's main thread without waiting,
- the `start_plugin` notification.

**Why NetMQ 4.0.0.1 and not the newest version (4.0.4.5).**
- The newest version needs about five extra .NET packages (System.Collections.Immutable, System.ServiceModel.Primitives, …). These are hard to get working in Unity.
- 4.0.0.1 needs only AsyncIO, and it has been used in Unity by hmd-eyes for years.
- 4.0.0.1 does **not** need **NaCl.Net**: that came with NetMQ 4.0.1 (for encryption, which we don't use). So NaCl.Net is not in the repo, and neither is its licence.

**Unity setting needed:** *Project Settings → Player → Other Settings → Api Compatibility Level = **.NET Framework***. The project was on .NET Standard 2.1.
- Why: this NetMQ build references `System.ServiceModel`. As far as I know, only Unity's .NET Framework profile provides it (**TO VERIFY**: if NetMQ loads fine under .NET Standard 2.1, the setting can go back).
- hmd-eyes also requires ".NET 4.x". Its README gives a MessagePack issue as the reason, and we don't use that library.

**Message format:** msgpack. We use our own ~200-line reader (`PupilMsgPack.cs`) instead of the MessagePack-CSharp library used by hmd-eyes, which is about 100 files.

**Fallback (b):** if NetMQ does not load in Unity 6000.3, the plan is a Python bridge (pyzmq + msgpack) that forwards the newest surface gaze over UDP. Not built.

### Pupil Capture protocol: what was checked in the Pupil source code (github.com/pupil-labs/pupil, master, read 2026-11-12)

| Item | Checked in | Status |
|---|---|---|
| Pupil Remote default port **50020** | `main.py` (`"port": 50020`) | checked |
| Commands `SUB_PORT`, `PUB_PORT`, `t` (time, as text), `v` (version), `T`, `R`/`r`, `C`/`c` | `network_api/controller/pupil_remote_controller.py` | checked |
| A multipart message sent to Pupil Remote is forwarded as a notification (reply "Message forwarded.") | same file | checked |
| Messages: [topic, msgpack payload] | `zmq_tools.py` | checked |
| Surface topic `surfaces.<name>`. Fields: `name`, `gaze_on_surfaces`, `fixations_on_surfaces`, `timestamp` (= world-camera frame). Sent **once per world-camera frame**, and **only while that surface is detected**. | `surface_tracker/surface_tracker.py` | checked |
| Gaze item fields: `topic` (e.g. `gaze.3d.01._on_surface`), `norm_pos`, `confidence`, `on_surf`, `base_data`, `timestamp` (of the gaze datum) | `surface_tracker/surface.py` | checked |
| `norm_pos` origin **bottom-left**, 0..1 on the surface | — | **TO VERIFY** (look at the bottom-left, then the top-right corner) |
| Default marker family **AprilTag tag36h11** | `surface_tracker.py` (`DEFAULT_DETECTOR_MODE`) | checked |
| Blink topic `blinks`: `type` onset/offset, `confidence`, `timestamp`. Plugin class `Blink_Detection`, defaults 0.2 s / 0.5 / 0.5. | `blink_detection.py` | checked |
| Accuracy Visualizer logs "Angular accuracy: X degrees" and "Angular precision: X degrees". Log records go out on `logging.<level>` with the text in `msg`. | `accuracy_visualizer.py`, `zmq_tools.py` | checked in code; **TO VERIFY** that the lines arrive in Unity |
| Calibration notifications `calibration.successful` / `calibration.failed` (topic `notify.…`) | `gaze_mapping/notifications.py` | checked |
| Gaze `timestamp` = time of the eye image | — | **TO VERIFY** in Pupil's docs (affects only what "latency" means) |
| Capture versions before 3.x | — | not checked: tell us your version |

---

## 3. Gaze ray interface

Every source returns a `GazeSample`: `valid`, `onDisplay`, `worldRay` (origin = eye, direction = line of sight), `confidence`, `sourceTime`, `displayIndex`, `displayPos`.

| Source | Ray |
|---|---|
| Mouse | `driverCamera.ViewportPointToRay(pointer)` |
| Scripted test (T) | ray from the driver camera through the target. **The self-test must still PASS 13/13.** |
| Pupil | `surfaces[i].camera.ViewportPointToRay(norm_pos)` for the monitor whose surface has the gaze |
| XR (later) | the headset's eye-gaze ray (OpenXR eye gaze; **TO VERIFY** per headset) |

`DriverTwin` classifies the ray against the zones. The road-ahead window is measured in the driver camera's frame, which is the car's frame (the camera has no pitch). With three monitors this stays the centre camera, so the window stays fixed to the car. New log columns: `gaze_az_deg` and `gaze_el_deg`, relative to straight ahead.

**4th surface (planned, Step 5).** A real tablet next to the driver with its own AprilTags can be a 4th surface. Mark it `isZone` and set `zone = SideDisplay` in `PupilGazeSource.surfaces`, and the gaze on it then counts as SideDisplay directly, without a ray. The fields exist but this is **not built or tested**.

---

## 4. Parameters (all in the Inspector)

| Parameter | Value | Source / reason |
|---|---|---|
| `PupilGazeSource.port` | 50020 | Pupil Capture default (checked in source) |
| `requestTimeout` | 1.0 s | our choice: a local answer takes about 1 ms |
| `retryInterval` | 2 s | our choice |
| `noDataAfter` | 1.0 s | our choice (30 world frames at 30 fps) |
| `confidenceThreshold` | **0.6** | hmd-eyes `GazeData.cs` names "below ~0.6" as the usual filter. Pupil Player's default minimum: **TO VERIFY**. Using 0.6 is **our choice**. |
| `maxSampleAge` | 0.2 s | our choice. Measured from when Unity **received** the sample, so it detects a stalled stream, not the tracker's own delay. 6 world frames at 30 fps. |
| `lowConfidenceShare` | 50 % | our choice (status "Low confidence") |
| `syncRequests` | 10 | our choice; the shortest round trip is used |
| `resyncInterval` | 30 s | our choice (gives the drift) |
| `startBlinkDetector` | on | starts Capture's `Blink_Detection` plugin with Capture's defaults |
| `FakePupilServer.port` | 50021 | our choice: never the same as a real Capture |
| Fake stream: 30 fps world, 120 Hz gaze, confidence 0.9 ± 0.05, noise ± 0.3 %, extra delay 10 ms | | our choices for a plausible test stream, not tracker properties |

Meta-file fields typed in by the experimenter on `PupilGazeSource` ("Lab notes"): tracker model, expected eyes (mono/binocular), world-camera resolution and fps, calibration method.

---

## 5. Choice of the sample, mono vs binocular, blinks

- **Newest sample per physics step.** Each step Unity reads everything that has arrived and uses the gaze point with the newest Pupil time stamp. The same point arrives once per detected surface: the one where `on_surf` is true wins. If it is on two surfaces (where they overlap at a bezel), the one further inside its surface wins (our choice).
- **Invalid** (= OffScreen, drains the buffer as before) when there is no sample, the newest one was received more than `maxSampleAge` ago, or its confidence is below the threshold. **Holding the last zone through short gaps is part C** (team decision, not built yet).
- **Mono / binocular.** The gaze topic says which eyes were used: `gaze.3d.01.` means both eyes, `gaze.3d.0.` / `gaze.3d.1.` mean one eye. A binocular headset also sends one-eye gaze when one pupil is poorly detected.
  - Each sample's topic goes to the raw file.
  - The meta file gets the share per kind (`tracker_eyes_observed`) next to what you typed in (`tracker_eyes_expected`).
  - The overlay shows the eyes of the newest sample.
- **Blinks.** Unity subscribes to `blinks` and logs every onset/offset as an event (`PupilBlink`). The `blink` column is 1 between onset and offset. Part C will use this to tell blinks (short, with onset/offset) from lost tracking (no blink event). For now blinks are only recorded.

---

## 6. Clock sync and latency

- **Sync.** Unity sends `t` 10 times. For the reply with the shortest round trip: offset = Pupil time − local time at the middle of the round trip. "Local time" is .NET's `Stopwatch` clock, the same clock as `local_time_s` in the raw file.
  - The sync is done at connect and then every 30 s. Each one is logged as an event.
  - The meta file has the first sync, the last sync and the drift (ms per minute).
  - The waiting is short (about 1 ms on the same PC) and is only done right after Capture has answered.
- **Latency of each sample** = Unity receive time − (gaze time stamp − offset). It includes:
  - Pupil's own processing,
  - waiting for the next **world-camera frame** (surface data is sent once per world frame, so up to 33 ms at 30 fps),
  - the network,
  - up to one Unity frame, because sockets are read in Update and in the physics step.

  The raw file has it per sample. The meta file has the median and the 95th percentile. Its meaning depends on "gaze time stamp = eye image time" (**TO VERIFY**).
- **Second PC.** The same method works over the network, with a larger round trip. Set `captureHost` on the Gaze Source Switch to that PC's IP address, and allow TCP port 50020 plus Capture's data ports in that PC's firewall.

### How to test 30 vs 60 fps world camera (marker detection and latency)
Pupil Core's world camera offers several resolution / frame-rate pairs. Which ones exactly is **TO VERIFY** in Capture: *World window → Video Source → frame rate / resolution*.
1. Keep the same resolution, the same seat, lighting and calibration.
2. Set 30 fps in Capture, and type `worldCameraFps = 30` and the resolution on `PupilGazeSource` (Lab notes).
3. Drive 2 minutes and look at all three monitors (including turning your head).
4. Repeat at 60 fps (type 60).
5. Compare in `check_drive.py`, section "Eye tracker":
   - **measured world fps** (if it is lower than set, Capture cannot keep up),
   - **on a surface %** and **valid gaze %** (marker detection),
   - **latency median / p95**,
   - number of **NoData** events.
6. Also watch Capture's own FPS and CPU graphs during the drive.

Expected (my reasoning, not measured): at 60 fps surface data comes twice as often, so the median latency should drop by about 8 ms (half of a 33 → 17 ms frame wait). Detection could get worse if 60 fps forces a lower resolution or more CPU load. This is exactly what the test shows.

---

## 7. Logs (part E)

**`drive_…_gaze.csv`** (only when a Pupil source is used): one row per received gaze point **per surface**.

| Column | Meaning |
|---|---|
| local_time_s | Unity receive time, Stopwatch clock (s) |
| drive_time_s | drive time at receive |
| pupil_time_s | gaze time stamp (Pupil clock) |
| world_frame_time_s | time stamp of the world-camera frame the surface was found in |
| surface, display | surface name and its index in Unity (-1 = not configured) |
| norm_x, norm_y | position on the surface (0..1, origin bottom-left) |
| confidence, on_surf | from Pupil |
| gaze_topic | e.g. `gaze.3d.01._on_surface` (eyes used) |
| latency_ms_est | estimate, see section 6 (empty before the first sync) |

**Main CSV (50 Hz), new columns at the end:** `gaze_source`, `gaze_valid`, `gaze_confidence`, `gaze_az_deg`, `gaze_el_deg`, `gaze_display`, `gaze_on_display`, `tracker_status`, `blink` (meanings in `DriverTwin.md` 7b).

**Events:**
- `TrackerStatus` (new status, from, reason),
- `PupilClockSync` (offset, round trip),
- `PupilBlink` (onset/offset),
- `PupilAccuracy` (accuracy/precision from Capture),
- `PupilCalibration` (successful/failed).

**Meta:**

| Group | Keys |
|---|---|
| Tracker and connection | `gaze_tracker`, `pupil_capture_version`, `pupil_endpoint` |
| Eyes | `tracker_eyes_expected`, `tracker_eyes_observed` |
| Surfaces | `pupil_surfaces`, `pupil_unknown_surfaces` |
| Sample rules | `confidence_threshold` (+ note), `max_sample_age_s`, `gaze_invalid_rule` |
| Blinks | `blink_detector`, `blink_count` |
| World camera | `world_camera_resolution_entered`, `world_camera_fps_entered`, `world_camera_fps_measured` |
| Calibration and accuracy | `calibration_method`, `pupil_accuracy_deg`, `pupil_precision_deg` (+ note) |
| Clock | `clock_sync_first`, `clock_sync_last`, `clock_sync_count`, `clock_drift_ms_per_min` |
| Latency | `latency_ms_median`, `latency_ms_p95`, `latency_samples` |
| Count | `gaze_samples_received` |

The meta file is rewritten when this information changes and at the end of the road. All logs stay in `DriveLogs` (git-ignored, never pushed).

---

## 8. Overlay (part F, test mode only)

- **COMPACT:** one tracker line, e.g. `Pupil  Connected  97 %  binocular`. The % is the share of samples at or above the threshold over the last 5 s; `blink` is added during a blink. Colours:
  - green: Connected
  - orange: Low confidence
  - red: NO DATA or NOT CONNECTED
  - yellow: connecting

  With the fake stream the line starts with `Fake`.
- **FULL:** a Tracker block with:
  - the version, address, valid %, samples per second, eyes and blinks,
  - the clock offset, round trip and drift, and the latency median and p95,
  - the measured world fps, and the accuracy and precision,
  - surfaces that Unity doesn't know, and the fake stream's state.
- **Gaze:** the tracker gaze is a **ring** in the zone colour (the mouse is a filled dot). The label adds the confidence. With the fake stream a small grey cross marks the mouse, so the delay and noise are visible.

---

## 9. Lab setup checklist

**Once (or when the setup changes)**
1. **Markers.**
   - Print AprilTag **tag36h11**: 4 per monitor, on the bezel corners, with **different IDs on every monitor** (12 IDs). Optionally 1–2 more on each top bezel.
   - Start at about **5 cm** (black square) with a white margin. This is our estimate; enlarge the markers if detection flickers from the seat.
   - Use matte paper on rigid card, flat on the bezel.
2. **Surfaces.** In Capture, Surface Tracker plugin, from the driver seat:
   - look at a monitor, **Add surface**,
   - drag the corners onto the **lit display area** (not the bezel),
   - name it exactly `monitor_left`, `monitor_center` or `monitor_right`.

   Unity currently knows only `monitor_center` (the other two come with part G). Back up Capture's surface definition file; its name and folder are **TO VERIFY**.
3. **Unity.** On `DriveSystems`:
   - Gaze Source Switch: Source = **PupilCapture**, and `captureHost` (127.0.0.1 if Capture runs on the same PC).
   - PupilGazeSource Lab notes: model, eyes, world-camera resolution and fps, calibration method.

**Per participant**
1. Fit the headset. In Capture check the eye images: the pupil is detected (red ellipse) over the whole range of gaze, and the confidence graph is mostly high.
2. Calibrate at about screen distance (the method for three monitors is **TO VERIFY** in the lab: screen marker on the centre monitor vs. single marker moved over all three). Note the Accuracy Visualizer result: Unity logs it too if the log line arrives.
3. Validate: ask the participant to look at the four corners and the centre of the centre monitor and check the gaze in Capture.

**Before each drive (Unity, test mode, F1 COMPACT)**
1. Tracker line **green "Connected"**, valid % above about 90 % (our rule of thumb) while looking at the road.
2. Look at the mirror, the speedometer, the side display and the road: the ring lands there and the zone label matches.
3. Look away from all monitors: OffScreen. Blink: `blink` appears briefly.
4. Then switch to study mode for the real drive.

---

## 10. Home test with the fake stream (no tracker)
See `Docs/eyetracking_test_steps.md`, part 1.

## 11. Lab test (real tracker)
See `Docs/eyetracking_test_steps.md`, part 2.

---

## 12. Planned parts

- **C. Dropouts.**
  - Hold the last zone for gaps shorter than X, and treat longer gaps as invalid (drain) or freeze the buffer.
  - X and drain/freeze stay placeholders until the team has read AttenD (Kircher & Ahlström).
  - Blink events (already logged) will tell blinks from lost tracking.
- **D. Accuracy check.**
  - Targets on each monitor (3×3) plus the typical lead-car positions; error in degrees and pixels per monitor, saved to the meta file.
  - The lead-car angular margin rule is a team decision.
  - Capture's own accuracy/precision is already logged (section 7).
- **G. Three monitors.** Three cameras with off-axis projection from measured screen corners. Measure first:
  - each monitor's active area (mm), bezel width and angle, and the gap between screens,
  - the eye position relative to the centre screen,
  - the resolution, refresh rate, GPU and outputs.

## 13. Limitations

- Not tested with the real tracker yet. The NetMQ DLL loading in Unity 6000.3 is the main risk (section 2).
- Only the centre monitor is mapped until part G. Gaze on the side monitors counts as OffScreen (surfaces not configured) or is ignored.
- Surface data exists only while that monitor's markers are detected in the world camera. Looking down at the real wheel loses the markers, so gaze is invalid there (OffScreen), which is correct for "eyes off road" but cannot tell the wheel from other places.
- Latency includes up to one Unity frame and the world-camera wait (section 6).
- Head-mounted tracker parallax: calibrate at screen distance.
- The virtual ray matches the real line of sight only if the camera view matches the physical monitor seen from the eye (part G).

## 14. Third-party code
See `THIRD_PARTY_NOTICES.md` in the repository root and `Assets/Plugins/NetMQ/README.md`.

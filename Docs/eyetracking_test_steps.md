# Eye tracker parts A, B, E, F: test steps

## What changed (all in D:\XR Project)

**New files**
- `Adaptive_ADAS/Assets/_Project/Scripts/Twin/`:
  - `GazeSample.cs`
  - `GazeSourceSwitch.cs`
  - `PupilGazeSource.cs`
  - `FakePupilServer.cs`
  - `PupilMsgPack.cs`
  - `PupilNetMQ.cs`
- `Adaptive_ADAS/Assets/Plugins/NetMQ/`:
  - `NetMQ.dll` (181 KB) and `AsyncIO.dll` (30 KB)
  - their licences: `LICENSE-NetMQ-LGPL-3.0.txt`, `LICENSE-GPL-3.0.txt`, `LICENSE-AsyncIO-MPL-2.0.txt`
  - `README.md` (versions, download source, checksums)

  The DLLs are small, so no Git LFS is needed. Unity creates the `.meta` files: commit them too.
- `THIRD_PARTY_NOTICES.md` (repository root).
- `Docs/EyeTracking.md` (design, parameters, lab checklist) and this file.

**Changed files**
- `Scripts/Twin/`:
  - `GazeSource.cs`: every source now returns a GazeSample (world ray).
  - `MouseGazeSource.cs`, `ScriptedGazeSource.cs`: moved over to GazeSample.
  - `EyeTrackerGazeSource.cs`: old stub, kept so older scenes don't miss a script.
  - `DriverTwin.cs`: classifies the ray; new outputs Sample, GazeAzimuthDeg, GazeElevationDeg.
- `Scripts/Logging/DriveLogger.cs`: raw `_gaze.csv`, 9 new columns, eye-tracker keys in the meta file.
- `Scripts/HMI/DebugOverlay.cs`: tracker line (COMPACT) and Tracker block (FULL).
- `Scripts/HMI/GazeDebugView.cs`: tracker gaze drawn as a ring, plus a mouse cross with the fake stream.
- `Editor/BaseSceneBuilder.cs`: adds PupilGazeSource, FakePupilServer and GazeSourceSwitch. It no longer adds the old stub.
- `Analysis/check_drive.py`: new "Eye tracker" section. It ignores `_gaze.csv` when it looks for the newest drive.
- `Docs/DriverTwin.md`: sections 2 and 7b.

## What to click
1. Close any open .cs tabs in Visual Studio.
2. **Edit → Project Settings → Player → Other Settings → Configuration → Api Compatibility Level → `.NET Framework`** (it was .NET Standard 2.1). Why: `Docs/EyeTracking.md` section 2.
3. Wait for compiling. The Console must show **no red errors**. I could not compile here. If you see "Unable to resolve reference", "NetMQ could not be found" or similar, send me the exact text.
4. **ADAS → Build Base Scene**, then **File → Save Project**.

## What you should see

### 0. Mouse (default): nothing should have changed
- Press Play. Gaze Source Switch → Source is `Mouse`.
- The dot, zones, buffer and overlay are as before. The COMPACT panel has **no** tracker line.
- **T**: still `PASS … 13/13 OK`.
- Console: `[GazeSourceSwitch] Gaze source: Mouse`.
- Main CSV, new columns at the end: `gaze_source` = Mouse, `gaze_valid` = 1, `gaze_confidence` = 1.000, `gaze_az_deg` / `gaze_el_deg` = the angles of the pointer (about 0 / 0 at screen centre), `gaze_display` = 0. There is no `_gaze.csv`. The meta file has `gaze_tracker,none`.

### 1. Fake Pupil stream (home test of the whole eye-tracker path)
1. Stop Play. Select **DriveSystems → Gaze Source Switch → Source = `PupilFake`**. Press Play.
2. Console, within about a second:
   - `[FakePupil] Fake Pupil Capture on 127.0.0.1:50021 (data port …)`
   - `[GazeSourceSwitch] Gaze source: PupilFake (127.0.0.1:50021)`
   - `[Pupil] Connected to Pupil Capture fake-1.0 (Unity FakePupilServer) at 127.0.0.1:50021 …`
   - `[Pupil] first 'surfaces.monitor_center' message. Fields: topic, name, gaze_on_surfaces, fixations_on_surfaces, timestamp | gaze item: topic, norm_pos, confidence, on_surf, base_data, timestamp | topic 'gaze.3d.0._on_surface'`
3. **COMPACT** shows the line `Fake  Connected  100 %  mono eye0` in green.
4. **Gaze:** a **ring** (zone colour) follows the mouse, a little behind it and with a slight jitter. A small **grey cross** marks the mouse itself. Zones, buffer and states behave as with the mouse.
5. **Keys** (only for the fake):
   - **B**: blink. The ring disappears for 0.15 s and `blink` flashes in the tracker line. The buffer drops by about 0.15 s, because for now an invalid sample counts as OffScreen; part C will change that. The events get `PupilBlink,onset` and `PupilBlink,offset`.
   - **N**: low confidence (0.3, below 0.6). The ring disappears, the % falls, and after about 2–3 s the line turns orange: `Low confidence`. Press N again to go back.
   - **M**: markers lost. After 1 s the line shows red **NO DATA**, the ring disappears, the zone is OffScreen and the buffer drains. Press M again: back to Connected.
   - **K**: fake calibration. The Console says `Sent a fake calibration result`, and FULL then shows accuracy 1.50°, precision 0.10°.
6. **F1 → FULL**, Tracker block, roughly:
   - `PupilFake fake-1.0 … Connected  valid 100 %  120 samples/s  eyes mono eye0 3d`
   - `clock offset 1000.0… s  round trip` below 1 ms, latency median about **25–50 ms**, world camera **30 fps** measured.

   My latency estimate is 10 ms of fake delay + 0–33 ms waiting for the next world frame + 0–17 ms until Unity reads the socket.
7. **T** (self-test): still `PASS … 13/13 OK`. The test takes over the gaze and gives it back afterwards.
8. **Robustness:** while playing, untick the **FakePupilServer** component.
   - After about 1 s: `NO DATA`.
   - About 1–2 s later: red `NOT CONNECTED`, and a Console warning `Not connected (…). Retrying every 2 s.`
   - The ring disappears; Unity does not freeze and does not show an old position.
   - Tick it again: back to `Connected` within a few seconds.
9. **Variants** (Stop, change, Play):
   - FakePupilServer **Binocular** on: the line shows `binocular`.
   - **World Fps** 60: the measured fps is 60 and the median latency is about 8 ms lower.
10. **Logs:**
    - `drive_…_gaze.csv` with about 120 rows per second (columns in `EyeTracking.md` section 7).
    - Main CSV: `gaze_source` = PupilFake, `tracker_status`, `blink`.
    - Events: `TrackerStatus`, `PupilClockSync` (every 30 s), `PupilBlink`, `PupilAccuracy`, `PupilCalibration`.
    - Meta: `gaze_tracker,FakePupilServer (home test  NOT real gaze)`, the clock, latency and world-camera keys.
    - `python Analysis/check_drive.py` prints an **Eye tracker** section and the warning that the gaze was fake.
11. **Stop Play:** Unity must not hang. If the editor freezes when stopping, tell me: that is the known NetMQ shutdown issue.

### 2. In the lab (real tracker)
1. **Pupil Capture:**
   - Pupil Remote on (port 50020).
   - Surface Tracker with a surface named exactly **`monitor_center`** on the centre monitor's lit area.
   - Calibrate. Unity starts the Blink Detection plugin by itself.
2. **Unity:**
   - Source = **`PupilCapture`**.
   - `captureHost` = 127.0.0.1, or the Capture PC's IP address if it runs on a second PC.
   - On PupilGazeSource, fill in the **Lab notes** (model, eyes, world-camera resolution and fps, calibration method).
   - Press Play.
3. **Console:** `[Pupil] Connected to Pupil Capture <version>` with the real version number. Send it to me, together with the `first '…' message` lines.
4. **Corners:** look at the bottom-left corner of the centre monitor, then the top-right. The ring must go to the same corners. If it is mirrored up/down, tell me (then `norm_pos` is top-left based).
5. **Then:** the lab checklist in `Docs/EyeTracking.md` section 9, and the 30 vs 60 fps test in section 6.
6. **Capture's accuracy:** after a calibration in Capture, does the FULL Tracker block show the accuracy? If not, Capture's log lines are not reaching Unity. Tell me, and we read the value off Capture's screen instead.

## Things I am not sure about (please report)
- **Compiling, and NetMQ loading in Unity 6000.3 with the .NET Framework level.** This is the biggest risk. Send any red error. The fallback is the Python bridge.
- **The editor hanging when Play stops** (NetMQ shutdown). We use the same clean-up as hmd-eyes.
- **Port 50021 already in use:** the Console shows `[FakePupil] Pupil Remote thread stopped: … (port 50021 in use?)`. Then change the FakePupilServer port.
- The answers marked TO VERIFY in `EyeTracking.md` section 2: the `norm_pos` origin, whether the accuracy log lines arrive, and the meaning of the gaze time stamp.

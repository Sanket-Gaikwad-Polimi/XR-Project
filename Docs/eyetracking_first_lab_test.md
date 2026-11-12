# First test with the real Pupil tracker (same PC as Unity)

Goal: see real gaze move the ring in Unity and land in the right zones. About 30–45 minutes.
Things marked (check) are not yet confirmed and are what this test finds out.

## A. Unity first (5 min, can be done before the tracker is on)
1. Close the .cs tabs in Visual Studio. In Unity:
   **Edit → Project Settings → Player → Other Settings → Api Compatibility Level → .NET Framework**.
2. Wait for compiling. **No red errors?**
   - If there are red errors, stop here and send them to me. Most likely they are about NetMQ.
3. **ADAS → Build Base Scene**, then **File → Save Project**.
4. Quick check without the tracker:
   - Gaze Source Switch → Source = **PupilFake** → Play.
   - The ring should follow the mouse and the overlay should show `Fake  Connected`.
   - This proves the network code works in your Unity. Stop Play.

## B. Pupil Capture (10 min)
1. Put the headset on and start **Pupil Capture**. Note the version (window title or Help → About).
2. **Eye cameras.** In the eye window(s), the pupil must have a red ellipse wherever you look, and the confidence graph should be high (above about 0.6).
   - **One eye window = monocular; two = binocular.** Note which you have.
3. **World window → Plugin Manager**, switch on:
   - **Network API** (in Capture 3.x this is where Pupil Remote lives; older versions call the plugin "Pupil Remote").
     Open its menu in the right-hand side bar. It shows a line `Connect locally: "tcp://127.0.0.1:50020"` and a **Port** field.
     The number after the colon is the port Unity must use (default **50020**). If Capture says the port is in use, it picks another one: use that one.
   - **Surface Tracker**
   - **Blink Detection**: optional, Unity starts it by itself.
   - Quick connection test (Windows PowerShell, with Capture running): `Test-NetConnection 127.0.0.1 -Port 50020`.
     `TcpTestSucceeded : True` means Unity can reach Pupil Remote. `False` means Capture is not listening on that port.
4. **World camera settings** (Video Source menu): note the **resolution and frame rate**. Start with 30 fps.

## C. Markers and the surface (15 min)
1. Print **4 AprilTag tag36h11 markers**: `Docs/markers/apriltag_tag36h11_50mm.pdf`, page 1 (IDs 0-3). Print at **100 % / Actual size**, then measure: the black square must be 50 mm. Page 2 (IDs 4-7) is spare, for example for the tablet surface later.
   - Size: black square about 5 cm.
   - Use 4 **different** markers.
   - Leave a white border around each.
2. Tape one on each **corner of the monitor bezel** where Unity will run, flat and not covering the screen.
3. In Unity, make the picture fill the monitor:
   - Game view → **Play Maximized** (or "Play Focused" + maximize), with no toolbars over the picture if possible.
   - The surface must match the **rendered picture**, not just the monitor.
4. In Capture, **Surface Tracker** (side-bar icon after you switch it on in Plugin Manager):
   - In its menu set **Marker type = AprilTag**, family **tag36h11** (the default), and switch on **Show Marker IDs**.
   - Look at the monitor from the seat: in the world window the 4 markers get a coloured outline with their ID (detected).
   - Click **Add surface** in the menu, or the round **A** button on the left quickbar, or press the **a** key.
     A new surface appears over the markers. Wait until its build-up percentage reaches 100 % (move your head a little).
   - Click the surface's **edit surface** label in the world window. Drag its 4 corner handles onto the **corners of the Unity picture** (Game view image edges). If Unity is fullscreen, that is the lit area of the screen. Click **edit surface** again to finish.
   - In the surface's own submenu (it appears in the Surface Tracker menu), type the name exactly **`monitor_center`** into the name field.
   - Check: **Open Surface in Window** shows the monitor picture straight-on, with the gaze dot where you look.

## D. Calibration (5 min)
1. In Capture, choose **Screen Marker** calibration on this monitor, and press **C** (or the Calibrate button). Keep your head still and follow the markers.
2. Note the **accuracy / precision** that Capture shows after calibration (Accuracy Visualizer).
3. Look around the screen: Capture's world view should show the gaze dot where you look, and the surface view the gaze on the surface.

## E. Unity with the real tracker
1. DriveSystems → **Gaze Source Switch → Source = PupilCapture**, Capture Host **127.0.0.1**, port **50020**.
2. On **PupilGazeSource → Lab notes**, fill in:
   - Expected Eyes (Monocular/Binocular)
   - World Camera Resolution and Fps
   - Calibration Method = Screen Marker
3. Press **Play** (maximized). In the **Console** you should see:
   - `[Pupil] Connected to Pupil Capture <version> at 127.0.0.1:50020 …`
   - `[Pupil] first 'surfaces.monitor_center' message. Fields: …` (copy these lines to me)
4. **Overlay** (F1 COMPACT): `Pupil  Connected  9x %  mono eye0` (or `binocular`), in green.
   - `NO DATA` (red): markers are not seen, or the surface is not named `monitor_center`.
   - `NOT CONNECTED`: Unity gets no answer from Pupil Remote. Check, in this order:
     1. Capture is running and Network API is on (section B.3).
     2. The port in its `Connect locally` line equals **Capture Port** in Gaze Source Switch.
     3. `Test-NetConnection` (section B.3) gives True.
     4. Capture runs on this same PC. If it runs on another PC, set Capture Host to that PC's IP (shown in the Network API menu when "Use primary network interface" is on) and allow port 50020 through its firewall.
     5. PupilFake still works (section A.4). If it does not, the problem is in Unity, not in Capture.
5. **The checks that matter:**
   - **Corners:** look at the bottom-left corner of the picture, then the top-right. The ring must go there. If it goes up when you look down, tell me (the y direction is flipped).
   - **Zones:** look at the road, the mirror, the speedometer and the side display. The zone label and colour should match.
   - **Off the screen:** look at the desk or the wall. The ring disappears and the zone is OffScreen.
   - **Blink:** `blink` flashes in the tracker line.
   - **Accuracy from Capture:** F1 → FULL, Tracker block. Recalibrate in Capture during Play (press C in Capture); the accuracy should appear there (check).
6. Drive for 2 minutes, stop, and run `python Analysis/check_drive.py`. Its **Eye tracker** section shows:
   - valid %,
   - measured world fps,
   - latency,
   - mono or binocular.

## Send me
- The Capture version, mono or binocular, and the world-camera resolution and fps.
- The two `[Pupil]` Console lines, and any red error.
- The result of the corner check, and a screenshot of the FULL overlay.
- The `check_drive.py` "Eye tracker" output.

If the Unity window and Capture compete for the screen: move Capture's windows to a second monitor, or to the laptop screen if you have one.

# Step 3B–D: gaze source, gaze zones, attention buffer — test steps

## What changed (all in D:\XR Project)
**New files**
- `Scripts/Twin/GazeSource.cs`: the gaze-source "interface". It is an abstract class so it can be set in the Inspector.
- `Scripts/Twin/MouseGazeSource.cs`: the mouse pointer is the gaze point, and the cursor is hidden while driving.
- `Scripts/Twin/EyeTrackerGazeSource.cs`: stub that always returns "no data". Its comments explain how Pupil Labs or a screen-based tracker would plug in. Those product details are from memory and marked TO CHECK.
- `Scripts/Twin/GazeZone.cs`: zone types plus a marker component on each invisible zone collider.
- `Scripts/Twin/DriverTwin.cs`: zone raycast, attention buffer, states, glance duration, eyes-off-road time, hazard seen / first fixation, and events.
- `Scripts/HMI/GazeDebugView.cs`: test mode only. It draws the gaze dot (G) and the coloured zones (Z).
- `Docs/DriverTwin.md`: the algorithm, every parameter with its basis, our interpretation choices, and the expected numbers.

**Changed files**
- `Editor/BaseSceneBuilder.cs`: creates the **GazeZones** layer and the zone colliders (mirror, speedometer, side display, interior parts, lead car), adds DriverTwin, MouseGazeSource, the EyeTrackerGazeSource stub and GazeDebugView, and adds 5 semi-transparent zone materials.
- `ProjectSettings/TagManager.asset` is changed **by Unity** when the builder adds the layer. Commit it too.

## What to click
1. Close any open .cs tabs of the project in Visual Studio.
2. In Unity, wait for compiling. The Console should show no red errors.
3. **ADAS → Build Base Scene.** The Console should say `Created layer 'GazeZones' (index …)` the first time.
4. Game view at 16:9, FOV left on Manual 55°. Press **Play**.
5. Drive with the **keyboard** (left hand) and "look" with the **mouse** (right hand). The cursor is hidden. If you need it, **Ctrl+P** stops Play.

## What you should see on Play
1. **Gaze dot.** A small dot follows the mouse, coloured by zone (green Road, orange LeadCar, cyan Mirror, yellow Speedometer, magenta SideDisplay, lavender Interior). Next to it is a label such as `Road · buffer 2.00 s · Attentive`.
2. **Speedometer** (hold the dot on it for 3 s): the buffer **stays at 2.00 for 1 s**, then falls by 1 per second and reaches **0.00 after 3 s**. Back on the road it stays at 0.00 for 0.1 s, then rises by 1 per second and is back at **2.00 after 2.1 s**.
3. **Mirror**: same as the speedometer, with a 1 s free glance.
4. **Side display, dashboard or A-pillar**: the buffer falls **immediately**. The state changes to `Reduced` below 1.50 and `Distracted` below 0.50.
5. **Lead car**: orange `LeadCar`, which refills like Road.
6. **Mouse off the Game window**: the zone should become `OffScreen` and the dot disappears off the edge. I am **not sure** Unity reports the mouse position outside the Game view. If the zone instead freezes on the last zone, tell me.
7. **Z**: the zones appear as coloured semi-transparent boxes: an orange box around the lead car, cyan on the mirror, yellow on the cluster, magenta on the side display, lavender on the dash, pillars, header and door trims. They must **not** appear in the mirror image. Press Z again to hide them. **G** hides or shows the dot.
8. **I** (interior off): the interior zones go too, and everything on screen is `Road`.
9. **Hazard**: after the HAZARD marker, put the dot on the lead car for more than 0.1 s. The label adds `hazard seen`.
10. **Events.** After stopping, the newest `drive_*_events.csv` has `GlanceStart` / `GlanceEnd` (with `duration_s`), `AttentionStateChanged` (with `from` and `buffer_s`) and `HazardFirstFixation` (with `since_onset_s`).
11. **Driving feel unchanged.** The interior zones hang under their own kinematic Rigidbody, so they should not affect the car. If steering or braking feels different from before, tell me.

The per-row log columns, the overlay buffer bar, the check_drive.py gaze measures and the scripted test come with **E–F**.

## Things I am not sure about (please report)
- **Zone colours opaque instead of see-through.** I set URP's transparency properties from code. If the boxes are solid, select a material `_Project/Materials/GazeZone_*`, set Surface Type to Transparent, and tell me.
- **Layer creation.** This uses the standard Editor API on TagManager. If the Console shows `Layer 'GazeZones' not found`, add the layer by hand under Project Settings → Tags and Layers (any empty user layer) and rebuild.
- **Mouse outside the Game view**, as described in point 6.

## Our interpretation choices to discuss (details in DriverTwin.md)
1. Road = anything seen through the windows (road, sky, side windows). AttenD's "field relevant for driving" may be narrower: check the paper.
2. No gaze sample = OffScreen, so the buffer drains. Blink handling for the eye tracker: TO DECIDE.
3. Mirror → speedometer directly starts a new 1 s latency (latency per glance).
4. The refill delay is not restarted by moving between Road and LeadCar.
5. The lead-car zone has no angular margin. With an eye tracker it should follow the tracker's accuracy.
6. State limits 1.5 / 0.5 are **our design choice**.

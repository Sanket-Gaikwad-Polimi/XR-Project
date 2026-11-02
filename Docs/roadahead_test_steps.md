# Road-ahead window (sky, barrier and roadside are no longer "Road"): test steps

## What changed (all in D:\XR Project)
- `Scripts/Twin/GazeZone.cs`: new zone **OffRoad**. It is added at the end of the list, so the zone numbers stored in the scene do not change.
- `Scripts/Twin/DriverTwin.cs`: a gaze out of the windows is now **Road only inside the road-ahead window** around straight ahead: 12° left and right, 8° down, 4° up (Inspector: `roadAheadHalfWidthDeg`, `roadAheadDownDeg`, `roadAheadUpDeg`). Outside the window the zone is **OffRoad**, which drains the buffer at once (like the side display). Everything else (lead car, mirror, speedometer, side display, interior, buffer rules) is unchanged.
- `Scripts/HMI/GazeDebugView.cs`: with **Z** the window is drawn as a **green rectangle**. OffRoad has a grey gaze dot.
- `Scripts/Twin/ScriptedGazeSource.cs`: can also aim at OffRoad (for custom test sequences). The default T sequence is unchanged.
- `Scripts/Logging/DriveLogger.cs`: the meta file gains `road_ahead_window_deg` and a note.
- `Analysis/check_drive.py`: prints the window. Logs from before this change get a warning that their "Road" includes sky, barrier and roadside.
- `Docs/DriverTwin.md`: new section **3b** (road-ahead window) and updated zone table, interpretation choice 1, buffer rule and log column. `Docs/BaseScene_Parameters.md`: zone rows.
- `Docs/overlay_check/view_roadahead_1170x658.png`: drawing of where the window is on your laptop view.

## What to click
1. Switch to Unity and wait for compiling. There should be **no red errors**.
2. You do not need to rebuild the scene: the new fields have defaults. Rebuilding (ADAS → Build Base Scene) is fine too.
3. Press Play. Keep the interior on and the FOV on Manual 55°.

## What you should see
1. **Z**: a green rectangle around the road straight ahead. On a 16:9 view it spans about 38 %–62 % of the width and from about 43 % to 64 % of the height (top to bottom). Its bottom edge is just above the instrument cluster. Compare with `view_roadahead_1170x658.png`.
2. **Sky** (mouse above the rectangle): the dot turns **grey**, the label says `OffRoad`, and the buffer falls at once by 1 per second: `Reduced` below 1.50 after 0.5 s, then `Distracted` below 0.50 after 1.5 s, and 0 after 2 s.
3. **Median barrier on the left, or the roadside / grass on the right**, near the car: `OffRoad`, same drain.
4. **Road on the left, low down near the car** (left lane in front of the A-pillar): `OffRoad`.
5. **Road ahead inside the rectangle**: `Road` (green dot). The buffer refills after 0.1 s, as before.
6. **Lead car**: `LeadCar` (orange), as before, also when it is close and sticks out of the rectangle.
7. **Mirror, speedometer, side display, A-pillars and dashboard**: unchanged (Mirror, Speedometer, SideDisplay, Interior).
8. **Interior off (I)**: inside the rectangle Road, outside it OffRoad.
9. **Scripted test (T)**: still `PASS … 13/13 OK`, because the road target 12 m ahead is 5.7° down, inside the window.
10. **Logs**: `gaze_zone` can now be `OffRoad`. The events have `GlanceStart,OffRoad`. The meta has `road_ahead_window_deg,left/right 12;down 8;up 4`. `check_drive.py` prints `Road-ahead window: left/right 12, down 8, up 4 deg (outside it = OffRoad)`, and OffRoad appears in the glances-per-zone table.

## What still counts as Road, on purpose
Far ahead, things converge towards straight ahead, so they are inside the window:
- the median barrier beyond about 30 m,
- the right road edge beyond about 22 m,
- the inner part of the left lane further ahead,
- the speed signs from about 30 m away.

These are part of the forward driving scene. An eye tracker with 1–2° accuracy could not separate them from the road ahead anyway. If you want the window smaller or larger, change the three values on **DriveSystems → DriverTwin**.

## Our design choice, to check
The window size (12° / 8° / 4°) is **our choice**, explained in `DriverTwin.md` section 3b. AttenD's "field relevant for driving" (Kircher & Ahlström) and the "road centre" areas used in other gaze measures are **TO FIND/CHECK**. When someone has read the definition, set the three values from it and replace "Our choice" with the reference.

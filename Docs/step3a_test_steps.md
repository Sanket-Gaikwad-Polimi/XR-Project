# Step 3A: driver's-seat view and interior, test steps

## What changed (all in D:\XR Project)
**New files**
- `Adaptive_ADAS/Assets/_Project/Scripts/HMI/DriverViewSettings.cs`: FOV from screen size and distance (Geometric), or a manual value. It shows the computed numbers in the Inspector.
- `Scripts/HMI/RearViewMirror.cs`: mirror camera, then a 320 × 100 render texture, then the mirror glass (flipped left to right).
- `Scripts/HMI/Speedometer.cs`: digital km/h on the instrument cluster.
- `Scripts/HMI/FitTextHeight.cs`: gives the 3D text its size in metres.
- `Scripts/HMI/InteriorToggle.cs`: switches the whole interior on or off (Inspector, or the I key in test mode) and logs `InteriorOn` / `InteriorOff`.
- `Docs/DriverView.md`: eye point, the FOV calculation, the interior layout with gaze angles, and what is our choice versus what is literature TO FIND.

**Changed files**
- `Editor/BaseSceneBuilder.cs`: builds `EgoCar/Interior` (dashboard, cluster + speedometer, mirror + mirror camera, A-pillars, header, roof liner, door trims, side display), adds four materials (InteriorDark, InteriorTrim, Screen, MirrorGlass), and adds DriverViewSettings on the DriverCamera. The cabin box of both cars is now 0.25 m longer at the front.
- `Scripts/Logging/DriveLogger.cs`: the meta file now has `driver_eye_local_m`, `fov_mode`, the screen values, `fov_geometric_vertical_deg`, `fov_applied_vertical_deg`, `fov_applied_horizontal_deg`, `render_resolution_px` and `interior_visible_at_start`. The main CSV is unchanged.
- `Scripts/HMI/DebugOverlay.cs`: a new "View" line (FOV and interior on/off) and "I interior" in the key help.
- `Docs/BaseScene_Parameters.md`: a new section, "Driver's view and interior".

## What to click
1. In Visual Studio, **close any open .cs tabs** of this project. Last time VS overwrote a fixed file.
2. In Unity, wait for the scripts to compile. The Console should have **no red errors**.
3. Menu **ADAS → Build Base Scene**, and save the old scene if Unity asks. The menu rebuilds BaseScene.unity and creates 4 new materials in `_Project/Materials`.
4. In the Game view toolbar, set the aspect to **16:9** or **Full HD (1920×1080)**, so that the horizontal FOV is the one in the docs.
5. Press **Play**.

## What you should see on Play
1. **Driver's view from the left seat.** At the bottom there is a dark dashboard. You see the bonnet (blue) between the dashboard edge and about 7° below the horizon, and the road from about 10 m ahead. The left A-pillar is about a quarter of the screen width left of centre, and the header bar runs along the top of the screen.
2. **Speedometer** straight ahead below the road: large light digits showing **90** at the start (km/h), with "km/h" below. It follows the car's speed: brake and it counts down. The overlay's "Ego speed" should agree within 1 km/h.
3. **Rear-view mirror**, upper right of centre: the road behind you, lane lines and posts moving **away** from you. Check the flip: the median barrier (on your **left** in the windshield) must also appear on the **left** side of the mirror. If it appears on the right, the flip is wrong, so tell me.
4. **Side display** on the dash top, right of the cluster: a dark screen with "SIDE DISPLAY (task in Step 5)".
5. **I key**: the whole interior disappears (dash, mirror, pillars, displays) and the camera stays in the same place. Press I again and it comes back. The overlay "View" line says interior on/off, and "Last event" shows InteriorOn/InteriorOff.
6. **FOV.** Select `EgoCar/DriverCamera`. Under **Driver View Settings**, the computed read-only fields show Screen Height 19.43 cm, Screen Width 34.54 cm, Geometric Vertical 18.39°, Geometric Horizontal 32.11°, and Applied 55.
   - Switch **Fov Mode** to **Geometric From Screen** (this works during Play). The view zooms in to the true-size view: the lead car looks about 3 times bigger, and the mirror, speedometer and side display leave the screen. Switch back to Manual.
7. **Chase camera (C)** still works. The car looks the same as before from outside, with a slightly longer cabin.
8. After stopping Play, the newest `drive_*_meta.csv` in DriveLogs has the new `fov_*` and `interior_visible_at_start` rows.

## Things I am not sure about (please tell me what you see)
- **Mirror camera with URP in Unity 6000.3.** I used the documented URP calls (`GetUniversalAdditionalCameraData`, `renderShadows`, `renderPostProcessing`, `antialiasing`), but I could not compile here. If the Console shows an error in RearViewMirror.cs, send it to me.
- **3D text drawn on top.** Unity's built-in font material may ignore depth. If it does, in the **chase camera** you would see the speedometer digits through the car's cabin. This does not affect the driver's view. If it happens, it is cosmetic for now, and we will switch to TextMeshPro in the visual pass.
- Before Play, the text in the Scene view may be a little off in size. It is corrected when Play starts.

## Decision for the team (not urgent)
The default FOV is **Manual 55°** so the mirror and displays are visible on the laptop. That **minifies** the scene about 3.2 times compared to the true-size 18.4°. For the study we should use the geometric FOV of the **lab screens** (measure screen size and eye distance), or justify the deviation. Details are in `Docs/DriverView.md`, section 2.2.

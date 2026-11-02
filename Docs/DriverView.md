# Driver's view: eye point, field of view, greybox interior (Step 3A)

What this covers: where the virtual driver's eyes are, how the camera's field of view (FOV) is chosen, and what the greybox interior consists of. Every value says whether it is **our choice** (with the reason), a **definition**, or **literature (source: TO FIND)**, which means nobody in the team has checked a source yet and it must not be cited until someone has.

Coordinates are **car coordinates** in metres: x to the right, y up from the ground, z forward from the centre of the car. The car is 4.5 m long (z −2.25 … +2.25) and 1.8 m wide.

Code: `Scripts/HMI/DriverViewSettings.cs` (FOV), `RearViewMirror.cs`, `Speedometer.cs`, `FitTextHeight.cs`, `InteriorToggle.cs`; geometry in `Editor/BaseSceneBuilder.cs` (`BuildInterior`).

---

## 1. Eye point

| Value | Basis |
|---|---|
| x = −0.37 m | Our choice. Left-hand drive: the driver sits left of the centre line. In a 1.8 m wide car, the seat centre is roughly halfway between the centre line and the left door, which is about 0.35 to 0.40 m. Not measured on a real car. |
| y = 1.20 m above the ground | Our choice, an estimate for a seated adult in a mid-size saloon. Real eye heights vary with driver size and seat height. Literature (source: TO FIND; candidate to check: SAE J941, which describes driver eye locations as an "eyellipse"). |
| z = −0.20 m (0.2 m behind the car centre) | Our choice, made to fit our greybox car: the windshield base is at z 0.95, so the eye is about 1.15 m behind it horizontally and about 0.5 m behind the windshield top. |
| Camera pitch 0° (level, straight ahead) | Our choice. The horizon sits in the middle of the screen. |

If the lab has a physical seat and cockpit, the eye point must be matched to the real seat (TO DO in the lab). This matters more than the exact numbers above.

---

## 2. Field of view

### 2.1 Geometric FOV: "the screen is a window"
If the virtual camera's FOV equals the angle that the physical screen covers at the driver's eye, objects appear at their true angular size. For example, a car 50 m away looks exactly as large as a real car 50 m away would look through a window of that size.

Definition:

```
screen height  H = diagonal × h / √(w² + h²)       (w:h = aspect ratio)
screen width   W = diagonal × w / √(w² + h²)
vertical FOV   = 2 · atan( (H/2) / d )             (d = eye-to-screen distance)
horizontal FOV = 2 · atan( (W/2) / d )
```

Sanket's laptop: 15.6" diagonal, 16:9, eyes about 60 cm from the screen (the distance is an estimate and should be measured).

```
diagonal       = 15.6 × 2.54          = 39.62 cm
√(16² + 9²)    = 18.36
H              = 39.62 × 9 / 18.36    = 19.43 cm
W              = 39.62 × 16 / 18.36   = 34.54 cm
vertical FOV   = 2 · atan(9.71 / 60)  = 18.4°
horizontal FOV = 2 · atan(17.27 / 60) = 32.1°
```

Unity's `Camera.fieldOfView` is the **vertical** FOV. The horizontal FOV follows from the shape of the Game window, so set the Game view to 16:9 (or 1920 × 1080).

### 2.2 Why the default is Manual 55° (our choice, for development only)
At 18.4° × 32.1° only the road straight ahead fits on the screen. From the eye point, the speedometer is 16° down, the mirror 31° right and the side display 29° right (see the table in section 3). All three would be off-screen, and so would most of the gaze zones in Part C.

So `DriverViewSettings.fovMode` defaults to **Manual** with `manualVerticalFov = 55°` (≈ 85.6° horizontal at 16:9). This keeps the speedometer, mirror and side display on screen with some margin. At 50° the mirror's right edge already touches the screen edge.

Cost of this choice: the scene is **minified**. In the centre of the screen everything looks about tan(27.5°) / tan(9.2°) ≈ **3.2 times smaller** than with the geometric FOV, so the lead car looks farther away than it is. That is fine for building and testing at home. It is **not** a neutral choice for the study, because how FOV mismatch changes speed and distance perception is a known simulator issue (literature: source TO FIND).

**For the study (team decision):** measure the lab screen(s) and the participant's eye distance, enter them in `DriverViewSettings`, and switch to `GeometricFromScreen`. If the lab screens are large or multiple, the geometric FOV may already be wide enough to show the mirror and displays. Otherwise we have to choose between true size and seeing the interior, and justify that choice.

The meta file of every drive records `fov_mode`, the screen values, the geometric FOV, and the applied vertical and horizontal FOV.

---

## 3. Greybox interior

All parts are boxes with dark matte materials and have **no colliders**. Any collider under the ego car's Rigidbody would become part of the car's physics body. The whole interior is one object, `EgoCar/Interior`, switched on or off as a whole (section 4).

Gaze angles are measured from the eye point. Yaw is + to the right and pitch is + upwards.

| Part | Position / size (m) | Gaze angle from the eye | Notes (all: our choice, greybox, not measured from a real car) |
|---|---|---|---|
| Windshield line | from base (y 0.98, z 0.95) to top (y 1.41, z 0.32), ≈ 34° from horizontal | header bottom ≈ 21° up | Only the frame is modelled (no glass) |
| Dashboard | centre (0, 0.86, 0.60), size 1.56 × 0.24 × 0.70; top at y 0.98 | front edge (cowl) 10.8° down; rear edge 26° down | Hides the bonnet nearer than ~1.4 m |
| Bonnet | top of the car body, y 0.90, visible from z ≈ 1.4 to 2.25 | 10.8° to 7.0° down | Road visible from ≈ 9.8 m ahead of the eye |
| Instrument cluster + digital speedometer | binnacle centre (−0.37, 1.04, 0.42), 0.40 × 0.14 × 0.18; digits 4.5 cm tall | **0° yaw, 16° down**, 0.54 m; binnacle 9.6° to 23.5° down | Straight ahead, below the road view. Digits ≈ 4.7°, about 85 px tall at 1080p with 55° FOV |
| Rear-view mirror | centre (0, 1.31, 0.41), glass 0.26 × 0.08, turned 16° towards the driver; top touches the windshield | **31° right, 9° up**, 0.72 m; edges 21.5° to 39.3° right | See 3.1 |
| Side display (centre screen) | centre (0.05, 1.03, 0.55), 0.26 × 0.16 on the dash top, turned to face the eye | **29° right, 11° down**, 0.88 m; right edge 37.8°; top edge 6° down | Placeholder for the Step 5 task. Recommended display positions: literature (source: TO FIND; candidate to check: NHTSA visual-manual distraction guidelines, 2013) |
| A-pillars | x ±0.74 along the windshield line, 7 × 10 cm section | left 24° left; right 53° right | ≈ 4° wide at the left pillar. At 55° FOV the right pillar is just outside the view. Real A-pillar obstruction angles: literature (source: TO FIND) |
| Windshield header | (0, 1.41, 0.32), 1.56 × 0.06 × 0.12 | ≈ 21° up straight ahead | Top edge of the view at 55° FOV |
| Roof liner | (0, 1.43, −0.15), 1.56 × 0.03 × 0.82 | above the view | Kept short so the mirror camera does not see it |
| Door trims (belt line) | x ±0.76, top at y 0.97, z −1.5 … 0.25 | lower left / lower right corners | Frame the side windows |

Intentionally **left out**:
- **Steering wheel.** A greybox wheel would hide the speedometer. In real cars the cluster is seen through the wheel rim, which needs a proper model (visual pass). At home we drive with a gamepad, so there is no physical wheel to match either.
- **Windshield glass.** Transparent glass adds sorting and reflection issues and brings no benefit in the greybox.

The **cabin box** of both cars now runs from z −1.5 to +0.95 (it was −1.5 to +0.7), so its front reaches the windshield base and the dashboard is hidden from outside, for example in the chase camera. The lead car's rear, and so its brake-lamp positions, did not change.

### 3.1 Rear-view mirror (render texture)
- A second camera (`Interior/MirrorCamera`) sits just in front of the mirror and looks **straight back**. It renders into a 320 × 100 pixel texture, which is shown on the mirror glass **flipped left to right**, as a real mirror shows it.
- The vertical FOV is 12°, so the horizontal FOV is about 37° (texture aspect 3.2). The far clip is 600 m. Our choice. The real field of view of an interior mirror: literature (source: TO FIND; candidate to check: UN ECE Regulation 46, devices for indirect vision).
- This is an **approximation**: a real mirror shows the view reflected from the driver's eye, which starts about 0.7 m further forward. The mirror is turned 16° towards the driver, about half of the 31° eye-to-mirror angle, so the reflected line of sight points straight back. The image and the mirror's angle are therefore consistent.
- Cost: the scene is drawn a second time at 320 × 100 px, without shadows, post-processing or anti-aliasing. It is small on an RTX 2050.
- There is no traffic behind us yet, so the mirror shows the road, the lane lines, the posts and the barrier moving away.

### 3.2 Speedometer
Digital whole km/h, computed from the car's forward speed (`EgoCarController.ForwardSpeed × 3.6`). We chose digital because a large number is easy to read in the greybox. An analog gauge can come in the visual pass.

---

## 4. Interior on / off
`InteriorToggle` on the `EgoCar` object shows or hides the whole interior. The lab may have a physical cockpit, and then the virtual one must be off.
- Inspector: tick or untick **Interior Visible** (this also works during Play).
- Test mode: the **I** key toggles it. In study mode only the Inspector can change it.
- Each change is logged as an event, `InteriorOn` or `InteriorOff`. The meta file records `interior_visible_at_start`. A per-row `interior_on` column comes with Step 3E.
- The camera position does not change when the interior is off. The mirror camera is part of the interior, so it stops rendering too.

---

## 5. Known limitations and open points
1. **3D text may draw on top of other objects.** Unity's built-in font material might ignore depth. If it does, the speedometer digits would show through the cabin in the **chase camera**. We have not confirmed this. The driver's view is not affected, because nothing sits between the eye and the displays. Fix in the visual pass by switching to TextMeshPro.
2. Text gets its exact size when Play starts (`FitTextHeight`), so it can look slightly different in the Scene view before Play.
3. The mirror is a camera at the mirror, not a true reflection (3.1).
4. All interior dimensions are greybox estimates. If the team wants a real reference car, its package drawing would replace them (TO DO, optional).
5. The FOV choice for the study is open: team decision, see 2.2.
6. Part C will place invisible **gaze-zone colliders** over the speedometer, mirror and side display, and over the road and the lead car, using exactly these positions.

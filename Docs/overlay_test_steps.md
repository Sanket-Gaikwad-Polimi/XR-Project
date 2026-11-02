# Debug overlay redesign + speed-limit sign: test steps

## What changed (all in D:\XR Project)
**Changed files**
- `Scripts/HMI/DebugOverlay.cs`: rewritten. Three modes on F1 (COMPACT → FULL → OFF), fonts scaled to the screen height and shrunk until the text fits, attention bar inside the panel (the wide bar under the box is gone), HAZARD marker smaller and placed right of the panel.
- `Scripts/HMI/GazeDebugView.cs`: smaller gaze label with a faint dark background. It sits below-right of the dot and moves right of the overlay panel if it would overlap it.
- `Scripts/Twin/GazeBufferTest.cs`: a short status (RUNNING / PASS / FAIL) for the COMPACT panel. The test itself is unchanged.
- `Scripts/Scenario/SpeedLimitSigns.cs`: digits drawn from a texture **with mipmaps**, bold font, sign 1.5 m (was 1.0 m), size parameters in the Inspector. Logs an error if the signs have no DriveSession (they would then show the fallback 90).
- `Editor/BaseSceneBuilder.cs`: makes the `SignDigits` material and sets the texture's import settings (mipmaps, trilinear, clamp), then gives the material to the signs.
- `Docs/BaseScene_Parameters.md`: sign rows, a new "Speed-limit sign legibility" section, overlay rows and a new "Debug overlay: what the COMPACT panel covers" section. `Docs/DriverTwin.md` section 7c (overlay).

**New files**
- `Adaptive_ADAS/Assets/_Project/Textures/SpeedSignDigits.png` (55 KB, digits 0–9). Small, so no Git LFS needed. Unity creates `Textures.meta` and `SpeedSignDigits.png.meta`, and the builder creates `Materials/SignDigits.mat`: commit those too.
- `Docs/overlay_check/`: drawings of the view at 1920×1080, 1366×768 and 1170×658 with the COMPACT panel, plus the sign legibility test (4 PNGs, about 440 KB).
- `Docs/tools/`: the small scripts that made the texture and the drawings (`make_sign_digits.js`, `overlay_view.html`, `sign_legibility_test.html`). You don't need to run them.
- `Docs/overlay_test_steps.md` (this file).

## What to click
1. Close any open .cs tabs in Visual Studio, switch to Unity and wait for compiling. The Console should show **no red errors** (I could not compile here).
2. **ADAS → Build Base Scene.** The Console must **not** show `SpeedSignDigits.png not found`.
3. **File → Save Project.**
4. Game view: choose **1920x1080** in the resolution dropdown (add it with "+" as a Fixed Resolution if it isn't listed), press Play, then repeat with **1366x768**. Keep the FOV on Manual 55° and the interior on.

## What you should see
### 1. COMPACT (default at Play)
A dark, see-through panel in the top-left corner with about 6 lines, for example:
```
92.3 km/h   gear D   cruise off
Lead 90.0 km/h  Cruising
Gap 40.0 m   TTC --   THW 1.6 s
[green bar | |]  2.00 s  Attentive  ·  Road
Hazard seen  no
Gamepad not detected                      (red, only when no gamepad is connected)
```
- Extra lines appear only when relevant: `Gaze test RUNNING 4.2 / 10.0 s`, then `PASS` (green) or `FAIL (F1 = details)` (red). `Drive finished: R = new drive` appears at the end of the road.
- At braking: `Lead … Braking  HAZARD ACTIVE` (orange). TTC turns red below 3 s. THW = time headway. Very large values show as `>99 s`.
- The bar is green, then yellow below the first white mark (1.5) and red below the second (0.5).
- **Size** (from my drawing with an Arial-like font; Unity's own font may differ by a pixel or two, but the code measures with Unity's font, so the limits always hold):

| Game view | Font | Panel | Limit (25 % × 20 %) |
|---|---|---|---|
| 1920×1080 | ~21 px | ~453 × 168 px | 480 × 216 px |
| 1366×768 | ~15 px | ~325 × 122 px | 342 × 154 px |
| 1170×658 (your laptop view) | ~13 px | ~282 × 102 px | 293 × 132 px |

- **What it covers**: only the roof liner, the windshield header and the top of the left A-pillar. The road ahead, the lead car, the mirror (upper right), the speedometer (bottom centre) and the side display (lower right) are all clear. The windshield header's lower edge is at almost exactly the panel's lower limit. See `Docs/overlay_check/view_1920x1080.png` and `view_1366x768.png` for how it should look.
- The wide attention bar across the screen is **gone**.

### 2. F1 → FULL
- The old detailed content in the top-left, **at most 40 % of the width** (768 px at 1080p, 546 px at 768). Long lines wrap.
- The attention bar is inside the panel: `Attention 2.00 s Attentive · Road (buffer max 2.0 s)`.
- The **key help line** is at the bottom, in grey. **H** hides and shows it (only in FULL).
- FULL does cover part of the road on the left: that is expected, it is the debugging mode.

### 3. F1 → OFF, then F1 → COMPACT again
- OFF: no panel. The gaze dot stays (G toggles it). The HAZARD marker still flashes for 1 s at onset.

### 4. Gaze-dot label
- Smaller: about 12 px at 1080p and 10 px at 768 (it was 14 px everywhere). Shorter text, e.g. `Road · 2.00 s · Attentive`, on a faint dark background, below-right of the dot.
- Move the mouse into the panel area (top left): the dot stays visible on top of the panel and the **label jumps to the right of the panel**. It never covers the panel.
- Near the right or bottom edge of the screen the label flips to the left of or above the dot.

### 5. HAZARD marker
- At onset: red `HAZARD (t = …)`, smaller than before (about 65 px at 1080, 46 px at 768; it was 96 px). It is centred in the space right of the panel, at the height of the windshield header, above the mirror.

### 6. Speed-limit sign
- **Value check**: during Play, in the Hierarchy open `Road → SpeedLimitSigns (runtime)`. The signs are named **`SpeedLimit_90`**. Their `Number` child uses the mesh `SpeedLimitDigits_90`. The number comes from **DriveSystems → DriveSession → Speed Limit Kmh** (90), the same value as in the meta file. To test: set it to 110 before Play. The sign then shows 110, a little smaller so that it fits.
- **Look**: the sign is bigger (1.5 m instead of 1.0 m) and the digits are bold. Far away it is a soft grey blur, not broken strokes. It becomes readable as "90" at about **100 m at 1920×1080**, **70 m at 1366×768** and **60 m on your 1170×658 view**. The first sign is 70 m ahead at the start, so at 1080p it is readable straight away and on your laptop view within about half a second.
- If the digits are solid black rectangles or missing, tell me. Select `Materials/SignDigits`: Surface Type should be Transparent and Base Map should be SpeedSignDigits.

## Why the sign looked like "50" (my explanation, please check)
The code always showed `DriveSession.speedLimitKmh`, which is 90. Your drive meta also says `speed_limit_kmh,90`. The problem was size and rendering. The old digits were 0.4 m tall, which is **2.5 px at 100 m on your 658-px view** (4 px at 1080p). They were also drawn without mipmaps, so a far-away number is built from a few scattered texels of the big glyphs. In my test (`Docs/overlay_check/sign_legibility_test.png`), a "90" drawn that way at 5–7 px looks like "5c", "50" or random marks. With mipmaps and a bold font, "90" and "50" can be told apart from about 8 px. That threshold is my own judgement from the test, not a published value.

## Things I am not sure about (please report)
- **Compiling**: I could not compile here. Send me any red Console error.
- **Dot on top of the panel**: this uses GUI.depth (lower draws on top). If the dot hides under the panel, tell me.
- **Texture settings**: the builder sets them through Unity's TextureImporter. If the sign digits look blurry even up close, select `Textures/SpeedSignDigits` and check that Generate Mipmaps is on and Filter Mode is Trilinear.

## Team decision: sign size
1.5 m is **our choice for legibility on a screen**, not a real size: the official Italian sign size is TO FIND and probably smaller. On a 658-px-high view at 55°, no realistic size reaches 100 m. The table in `BaseScene_Parameters.md` ("Speed-limit sign legibility") gives the reading distance for 1.0 / 1.5 / 2.5 m at each resolution. It is one Inspector value: `Road → SpeedLimitSigns → Sign Diameter`.

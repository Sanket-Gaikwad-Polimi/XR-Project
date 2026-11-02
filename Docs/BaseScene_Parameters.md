# Base scene: parameters and where they come from

Every tunable value, its default, and its basis. **Basis** is one of:
- **Our choice (reason)**: a design decision we made ourselves; the reason is given; to be tuned in pilots.
- **Literature (source: TO FIND)**: we believe a published value exists, but **nobody in the team has checked a source yet**. Do not cite it or present it as literature until someone has, then replace "TO FIND" with the reference.
- **Unity default**: Unity's built-in default value, kept unchanged.
- **Definition**: standard formula or measure definition.
- **Brief / agreed**: from the course brief or agreed in the team.

## Road (`Scripts/Scenario/RoadLayout.cs`, `RoadMarkings.cs`, `SpeedLimitSigns.cs`)
| Parameter | Default | Basis |
|---|---|---|
| laneWidth | 3.5 m | Literature (source: TO FIND, motorway design standard) |
| lanesPerDirection | 2 | Brief |
| medianWidth | 3 m, concrete barrier 0.5 m in the middle | Our choice (simple separation of the carriageways) |
| shoulderWidth | 2.5 m | Our choice (placeholder; compare with Italian motorway values: TO FIND) |
| roadLength | 5000 m | Our choice (≈3.3 min at 90 km/h; brief asked for 2–3 min) |
| lineWidth | 0.15 m | Our choice (placeholder; real value: TO FIND) |
| dashLength / dashGap | 4.5 m / 7.5 m | Literature (source: TO FIND, Italian lane-divider pattern) |
| postSpacing | 50 m | Our choice (roadside posts give a sense of speed / optic flow) |
| Markings and posts | Generated at runtime, all lane lines in one mesh | Our choice (keeps `BaseScene.unity` small; was 2.8 MB with ~1000 objects) |
| Speed-limit signs | Every 1000 m, first at 120 m, centre 2.4 m high, 1.5 m outside the road edge. Number = `DriveSession.speedLimitKmh` (object name `SpeedLimit_<value>`) | Our choice (guidance only) |
| signDiameter | **1.5 m** | **Our choice for on-screen legibility**: digits about 8 px tall at 100 m on a 1080-px-high view (55° FOV), see below. Real Italian sign sizes: TO FIND (smaller, so this sign is oversized) |
| digitHeightFraction / digitWidthScale / maxNumberWidthFraction | 0.5 / 0.85 / 0.68 | Our choice by eye: digits half the diameter tall, slightly condensed so two digits fit inside the white centre (0.76 of the diameter); 3-digit numbers are made smaller to fit |
| Sign digits | `Textures/SpeedSignDigits.png`: Liberation Sans Bold digits, 2048×256 px, **with mipmaps**, trilinear filtering (made by `Docs/tools/make_sign_digits.js`) | Our choice. Mipmaps stop far-away digits from breaking up (see below). The font is not the official Italian sign alphabet (TO FIND if it matters) |

### Speed-limit sign legibility (our calculation, not literature)
On screen, an object of height h at distance d is h / d × (H / 2) / tan(FOV / 2) pixels tall (H = Game-view height in px, FOV = vertical field of view). With the 55° FOV this is h / d × 1037 at 1080 px, × 738 at 768 px and × 632 at 658 px (Sanket's laptop Game view).

How many pixels are needed to read the number: in our own rendering test (`Docs/overlay_check/sign_legibility_test.png`, made with `Docs/tools/sign_legibility_test.html`: "90" and "50" shrunk to 3–12 px digit height), 90 and 50 can be told apart from about **8 px** with mipmaps and a bold font. Without mipmaps (the old TextMesh digits) the strokes break up below about 10 px and a "90" can look like "50" or random marks. This threshold is our judgement from that test, not a published value.

| Sign | Digits | 1920×1080 | 1366×768 | 1170×658 |
|---|---|---|---|---|
| Old: 1.0 m, regular font, no mipmaps | 0.40 m | 4.1 px at 100 m | 3.0 px | 2.5 px |
| 1.0 m, new digits | 0.50 m | readable from ~65 m | ~46 m | ~40 m |
| **1.5 m (default)** | 0.75 m | **readable from ~97 m** (7.8 px at 100 m) | ~69 m | ~59 m |
| 2.5 m | 1.25 m | ~160 m | ~115 m | ~100 m |

So "readable from 100 m" depends on the screen: a sign of a realistic size cannot reach it on a 658-px-high view at 55°. A narrower FOV (e.g. the geometric one) or a taller screen helps more than a bigger sign.

## Driver guidance (`Scripts/Experiment/DriveSession.cs`)
| Parameter | Default | Basis |
|---|---|---|
| speedLimitKmh | 90 km/h | Our choice (placeholder = lead car speed, so following and the limit agree) |
| driverInstruction | "Follow the car ahead" | Brief / agreed (Sanket). No limiter, no automation |
| studyMode | off | Our choice. ON for participants: hides the debug overlay and the HAZARD marker, disables cruise, reset, reverse and chase camera |

## Ego car (`Scripts/Vehicle/EgoCarController.cs`). Physics: Rigidbody + 4 WheelColliders
| Parameter | Default | Basis |
|---|---|---|
| massKg | 1500 kg | Our choice (mid-size car) |
| centreOfMass | (0, 0.45, 0.1) m | Our choice (low, so the greybox does not roll over) |
| initialSpeedKmh | 90 km/h | Our choice (same as the lead car, so the drive starts while following) |
| maxDriveForce | 4500 N (= 3 m/s² max) | Our choice |
| enginePower | 90 kW (force = P / v) | Our choice (typical mid-size car) |
| maxSpeedKmh | 160 km/h | Our choice |
| engineBrakeForce | 600 N (≈0.4 m/s²) when the pedal is released | Our choice |
| maxBrakeDecel | 8 m/s² (~0.8 g), **brake only** | Literature (source: TO FIND, emergency braking on dry road). Total deceleration is higher because engine braking (~0.4 m/s²) and drag (~0.17 m/s² at 90 km/h) add to it: ≈8.5 m/s² at 90 km/h, matching the logs |
| brakeBuildUpTime | 0 s (instant) | Our choice for now (keeps current behaviour). Real brake systems need time to build pressure: value TO FIND in the literature, then set here. Logged in meta (`ego_brake_build_up_s`). Release is always instant |
| frontBrakeBias | 0.65 | Our choice (front axle does most of the braking) |
| holdBrakeTorque | 500 N·m per wheel | Our choice (keeps the car still when stopped) |
| maxSteerAngle | 30° | Our choice (typical max road-wheel angle; TO FIND) |
| maxLateralAccel | 5 m/s² (~0.5 g) | Our choice. Max wheel angle = atan(L·a/v²), so full input gives at most this lateral acceleration (≈1.2° at 90 km/h). The first version allowed ~10° at 90 km/h → ~4 g demand → tyres slid and the car spun (seen in our logs). Not an assist: it never steers by itself |
| forwardGrip / sidewaysGrip | 1 / 1 | Unity default friction curves (multipliers for tuning) |
| Wheel radius, wheelbase, track | 0.33 m, 2.7 m, 1.6 m | Our choice (mid-size car) |
| Suspension | spring 35000 N/m, damper 4500 N·s/m, travel 0.3 m | Unity default |
| dragCoefficient, frontalArea | 0.3, 2.2 m² | Literature (source: TO FIND, typical passenger car values) |
| allowReverse, reverseEngageDelay | on, 1.0 s | Our choice. Test mode only: hold the brake 1 s while stopped → R, release and press again to move backwards. Off in study mode (participants keep braking after stopping and must not roll backwards) |
| maxReverseSpeedKmh / maxReverseForce | 15 km/h / 2500 N | Our choice |
| collisionDebounceSeconds | 1.5 s | Our choice (contacts with the same kind of object within 1.5 s = one collision event) |

## Driver's view and interior (Step 3A: `Scripts/HMI/DriverViewSettings.cs`, `RearViewMirror.cs`, `Speedometer.cs`, `InteriorToggle.cs`, scene builder)
Full explanation, formulas and gaze angles: `Docs/DriverView.md`.

| Parameter | Default | Basis |
|---|---|---|
| Driver eye point (car coordinates) | x −0.37 m (left of centre), y 1.20 m (above ground), z −0.20 m (behind the car centre) | Our choice (left-hand drive, typical mid-size car). Real seated eye positions: literature (source: TO FIND; candidate to check: SAE J941 "eyellipse") |
| Camera pitch | 0° (looks level, straight ahead) | Our choice |
| fovMode | **Manual** | Our choice for development on the laptop: the geometric FOV (below) hides the mirror, speedometer and side display. For the study: geometric FOV of the lab screens (team decision) |
| screenDiagonalInch / aspect / viewingDistanceCm | 15.6" / 16:9 / 60 cm | Sanket's home setup (laptop spec; 60 cm is an estimate, to measure) |
| Geometric vertical / horizontal FOV | 18.4° / 32.1° | Definition: 2·atan((size/2)/distance) |
| manualVerticalFov | 55° (≈ 85.6° horizontal at 16:9) | Our choice (keeps speedometer, mirror and side display on screen with some margin; at 50° the mirror's right edge touches the screen edge). Minifies the scene ≈ 3.2× compared to geometric. Effect of FOV mismatch on speed/distance perception: literature (source: TO FIND) |
| Interior geometry (dashboard, binnacle, mirror, A-pillars, header, roof liner, door trims, side display) | see `DriverView.md` | Our choice (greybox, not measured from a real car) |
| Steering wheel | none | Our choice (a greybox wheel would hide the speedometer; visual pass) |
| Windshield glass | none (frame only) | Our choice (transparent glass adds sorting/reflection issues, no benefit in the greybox) |
| Speedometer | Digital, whole km/h, digits 4.5 cm tall (≈ 4.7° at 0.54 m) | Our choice (easy to read in the greybox) |
| Mirror texture | 320 × 100 px, no shadows / post-processing / anti-aliasing | Our choice (cheap on the RTX 2050) |
| Mirror camera | At the mirror, looking straight back, vertical FOV 12° (≈ 37° horizontal), near clip 0.3 m, far clip 600 m | Our choice (approximation: a real mirror shows the view reflected from the eye). Real interior-mirror field of view: literature (source: TO FIND; candidate to check: UN ECE Regulation 46) |
| Side display | 0.26 × 0.16 m on the dash top, ≈ 29° right and 11° down from the eye, 0.88 m away | Our choice (placeholder for the Step 5 task). Recommended display positions: literature (source: TO FIND; candidate to check: NHTSA visual-manual distraction guidelines, 2013) |
| Cabin box (both cars) | z −1.5 … +0.95 m (was −1.5 … +0.7) | Our choice (front reaches the windshield base so the dashboard is hidden from outside) |

## Driver twin: gaze and attention (Step 3B–D: `Scripts/Twin/DriverTwin.cs`, gaze sources, `GazeZone.cs`)
Full algorithm, interpretation choices and expected values: `Docs/DriverTwin.md`.

| Parameter | Default | Basis |
|---|---|---|
| Gaze source | MouseGazeSource (home); EyeTrackerGazeSource is a stub | Our choice (home testing without a tracker) |
| Gaze zones | Road, LeadCar, Mirror, Speedometer, SideDisplay, Interior, OffScreen; plus OffRoad | Brief; OffRoad = our addition |
| Road / OffRoad | Gaze ray hits no zone: Road inside the road-ahead window, OffRoad outside it (sky, barrier, roadside, near side lanes) | Our choice (AttenD's "field relevant for driving": TO VERIFY) |
| roadAheadHalfWidthDeg / roadAheadDownDeg / roadAheadUpDeg | 12° / 8° / 4° around the car's straight-ahead direction | **Our choice** (DriverTwin.md section 3b: own lane from 8.5 m, barrier only beyond ~30 m, sky above 4°). Literature value TO FIND |
| Zone margins | +0.5 cm per side for interior zones; lead car = its bounding box, no angular margin | Our choice (margin for the eye tracker: TO DECIDE from its accuracy) |
| No gaze sample | Counted as OffScreen | Our choice (blink handling: TO DECIDE) |
| bufferMax / bufferStart | 2.0 s / 2.0 s (starts full) | Brief, TO VERIFY (AttenD, Kircher & Ahlström) |
| refillRate / refillDelay | 1 s per s / 0.1 s (Road, LeadCar) | Brief, TO VERIFY (AttenD) |
| drainRate | 1 s per s | Brief, TO VERIFY (AttenD) |
| mirrorSpeedometerLatency | 1.0 s per glance | Brief, TO VERIFY (AttenD); "per glance" is our interpretation |
| attentiveMin / distractedBelow | 1.5 s / 0.5 s | **Our design choice** |
| hazardDwellMin | 0.1 s uninterrupted on the lead car after onset | Brief (minimum fixation duration: literature, source TO FIND) |
| Gaze dot / zone view | G / Z keys, test mode only | Our choice (debug) |

## Driver input (`Scripts/Vehicle/DriverInput.cs`). Gamepad = main home-testing device
| Parameter | Default | Basis |
|---|---|---|
| Gamepad pedals | Triggers used directly, no smoothing | Our choice (closest to real pedals; no artificial delay in brake reaction time) |
| triggerDeadzone | 0.03 | Our choice (ignores trigger noise) |
| stickDeadzone | 0.08 | Our choice (removes stick drift) |
| stickResponseExponent | 1.5 | Our choice (finer control near centre; full deflection unchanged; the lab wheel will use 1) |
| steerRate | 2.5 /s (centre → full in 0.4 s) | Our choice (a steering wheel cannot jump instantly; all devices) |
| steerReturnRate | 3.5 /s | Our choice (self-centering when released) |
| keyPedalRiseRate / FallRate | 3 /s / 5 /s | Our choice (keyboard keys are on/off) |
| Lane-keeping / steering help | None | Our choice (would confound the study) |
| Gamepad detection | Xbox (XInput), PlayStation 4/5, Switch Pro = "Gamepad"; generic DirectInput pads = "Joystick", not used | Unity Input System behaviour. Connect/disconnect logged as events |

## Test-only helpers (`Scripts/Vehicle/TestAssists.cs`, `Scripts/HMI/*`). All off in study mode
| Parameter | Default | Basis |
|---|---|---|
| Cruise control | Optional (E / gamepad X): holds the speed at activation (≥30 km/h), throttle only, cancelled by the brake, gain 0.5 per m/s | Our choice (keyboard testing) |
| Reset | Backspace / gamepad Y: nearest lane centre, forward, 50 km/h, ≥30 m behind the lead | Our choice. Event `Reset` |
| Chase camera | C / gamepad Select | Our choice (debug) |
| HAZARD marker | Red "HAZARD (t = …)" for 1.0 s at onset, font 6 % of the screen height, 12 % from the top, centred in the area right of the overlay panel | Our choice (lets us check onset timing by eye; never over the panel). Never in study mode |
| Debug overlay | F1 cycles **COMPACT** (default) → **FULL** → **OFF**; **hidden completely in study mode** | Our choice (it shows the lead state and "Braking", which would give the hazard away) |
| COMPACT panel | Top-left, inside 25 % of the width × 20 % of the height; ~6 lines (speed/gear/cruise, lead speed + state + HAZARD ACTIVE, gap/TTC/THW, attention bar + buffer + state + gaze zone, hazard seen; only when relevant: gamepad not detected, gaze test status, drive finished); dark background 55 % opaque | **Size limits: Sanket's requirement.** Font 2.1 % of the screen height, made smaller until the worst-case lines fit (our choice). THW = time headway |
| FULL panel | All details (old overlay) + attention bar, top-left, at most 40 % of the width, font 1.6 % of the screen height; key help line only here (H toggles it) | Width limit: Sanket's requirement. Font: our choice |
| Gaze-dot label | Zone · buffer · state, font 1.1 % of the screen height (min 10 px), below-right of the dot, faint dark background; moves right of the overlay panel if it would overlap it | Our choice (Sanket: smaller, never over the panel) |
| Interior on/off | I key (test mode) or `InteriorToggle.interiorVisible` in the Inspector | Our choice. Events `InteriorOn` / `InteriorOff` |

### Debug overlay: what the COMPACT panel covers (our calculation)
Driver camera: eye point (−0.37, 1.20, −0.20) m, no pitch, 55° vertical FOV, 16:9 (1920×1080, 1366×768 and 1170×658 are all 16:9, so the fractions below are the same). The panel stays inside x < 0.25 W, y < 0.20 H (GUI coordinates from the top-left), i.e. more than **17.3° above** the horizon and more than **24.8° left** of straight ahead. That area shows the roof liner, the windshield header (its lower edge is at 17.2°, almost exactly the panel's lower limit) and the top of the left A-pillar.

| Element (from the scene builder's geometry) | Screen area (fraction of width W / height H) |
|---|---|
| Road ahead | below the horizon, y > 0.50 H |
| Lead car at 40 / 10 / 5 m gap | x 0.49–0.52 / 0.48–0.56 / 0.46–0.59 W, y 0.49–0.53 / 0.48–0.59 / 0.47–0.66 H |
| Rear-view mirror glass | x 0.71–0.97 W, y 0.24–0.39 H |
| Instrument cluster (speedometer) | x 0.30–0.70 W, y 0.66–0.92 H |
| Side display with bezel | x 0.70–0.93 W, y 0.59–0.86 H |
| Speed sign at 100 m | x 0.53–0.54 W, y 0.48–0.50 H |
| HAZARD marker (1 s at onset) | y 0.12–0.21 H, right of the panel (ends above the mirror) |

None of the listed elements reaches the panel area. With the geometric FOV (18.4°) the panel starts 5.6° above the horizon and 8.2° left, still above and left of the lead car. Drawings of the three Game-view sizes: `Docs/overlay_check/view_*.png` (made with `Docs/tools/overlay_view.html`) (drawn from the geometry, not Unity screenshots).

## Lead car and hazard (`Scripts/Vehicle/LeadCarController.cs`)
| Parameter | Default | Basis |
|---|---|---|
| Model | Scripted (kinematic), not tyre physics | Our choice (identical hazard for every participant) |
| cruiseSpeedKmh | 90 km/h | Our choice |
| Initial gap | 40 m bumper to bumper (≈1.6 s at 90 km/h) | Our choice (scene builder) |
| brakeWindowStart / End | 20 s / 40 s, uniform random | Our choice (planned time after which the trigger conditions are checked) |
| Trigger conditions | same lane; time headway 1.0–3.0 s; ego speed 70–110 km/h; closing-speed check **off** (can be switched on: `limitClosingSpeed`, `maxAbsClosingSpeed` = 2 m/s); hold 1.0 s | **Placeholders = option B, team decision pending** (`Docs/HazardTriggerOptions.md`). Headway, not distance, so criticality doesn't depend on the driver's speed |
| maxWaitSeconds | 60 s | Our choice (placeholder). Not met in time → `HazardSkipped`, no hazard |
| brakeDecel | 6 m/s² (~0.6 g) | Literature (source: TO FIND, "hard braking" of the lead vehicle) |
| decelRampTime | 0 s (instant) | Our choice for now; real build-up TO FIND |
| useFixedSeed / seed | off / 12345 | Our choice (random by default; the seed used is in the meta file) |

## Lead car brake lights (scene builder + `LeadCarController.cs`)
| Parameter | Default | Basis |
|---|---|---|
| Lamps | Left + right (0.45 × 0.22 m) and a **centre high-mounted lamp** (0.60 × 0.08 m) at the top of the rear window | Our choice for size (greybox lamps were 0.35 × 0.15 m and hard to see at 50–100 m: at 100 m a 0.35 m lamp is only ~3 px wide on a 1080p screen with a 60° FOV). Real cars have a centre high-mounted lamp; its effect on noticing braking: literature (source: TO FIND) |
| Lit material | Emissive red, HDR intensity 6 | Our choice (glows independent of sun angle and shadows; HDR > 1 lets bloom pick it up) |
| Unlit material | Dark red, not emissive | Our choice |
| Bloom | Threshold 1.5, intensity 1.0, scatter 0.6; global Volume "PostProcessing (Bloom)" | Our choice (only pixels brighter than white glow, i.e. practically only the lit lamps; cheap on an RTX 2050; switch off by disabling the object). State logged in meta (`bloom_volume_active`) |
| Logged | `lead_brake_lights_on` column (0/1) | Our choice (proves when the visual cue was on) |

## Twin, logger and analysis
| Parameter | Default | Basis |
|---|---|---|
| Update rate | 50 Hz (Fixed Timestep 0.02 s) | Brief (~50 Hz) |
| Lead relevant | Lead car centre ahead of ego centre AND lateral overlap of the bodies | Our choice (only then can the cars collide) |
| Gap, closing speed, TTC, headway | Only when the lead is relevant; otherwise empty (never negative) | Our choice |
| TTC | gap / closing speed, only if closing > 0.05 m/s | Definition (TTC); 0.05 m/s threshold = our choice (avoids huge values from noise) |
| Time headway | gap / ego speed | Definition |
| Hazard active (`hazard_id`) | Onset → lead stopped AND ego stopped behind / passed / changed lane | Our choice |
| Log folder | `D:\XR Project\DriveLogs\` (relative to the Unity project), git-ignored | Agreed (Sanket) |
| Flush interval | 1 s | Our choice (a crash loses at most 1 s) |
| Events | `events` column + `drive_..._events.csv` | Our choice. DriveStart/End, HazardOnset (with criticality), HazardSkipped, HazardEnd, Collision (Lead/Barrier/Post/Sign/Ground/Road), Reset, CruiseOn/Off, ReverseOn/Off, Gamepad/Joystick Connected/Disconnected/Missing, InteriorOn/Off, GlanceStart/GlanceEnd (open glance closed at DriveEnd), AttentionStateChanged, HazardFirstFixation, GazeTestStart/End |
| Gaze columns (Step 3E) | gaze_x, gaze_y (viewport 0..1, origin bottom-left, empty = no sample), gaze_zone, glance_duration_s, buffer_s, attention_state, eyes_off_road_s, hazard_seen, interior_on | Brief |
| Meta file | Rewritten at hazard onset/skip and at the first fixation on the lead car | Our choice (contains outcome, criticality at onset, first-fixation result) |
| Brake reaction time (`check_drive.py`) | First press of `brake_raw` ≥ 0.05 after onset | Our choice of threshold (raw pedal, not the keyboard ramp or brake build-up) |
| Pre-onset headway (`check_drive.py`) | Median time headway in the 20 s before onset | Our choice (input for the team's trigger decision) |
| Edge-of-window warning | Onset headway within 0.05 s, or speed within 1 km/h, of a window limit | Our choice |
| Eyes-off-road window (`check_drive.py`) | 10 s before onset; mirror and speedometer count as off-road | Brief (window); counting mirror/speedometer as off-road = our choice |
| Time to first fixation (`check_drive.py`) | From onset to the start of the first ≥ 100 ms look at the lead car; shown next to the brake RT, warning if the brake came first | Brief (definition); warning = agreed (Sanket) |
| Glances per zone (`check_drive.py`) | Runs of equal `gaze_zone` rows; scripted-test rows excluded | Our choice |
| Scripted gaze test | T key; default 3 s Road, 1.5 s Mirror, 2.5 s SideDisplay, 3 s Road; tolerance 0.001 s | Brief (sequence); tolerance = our choice |

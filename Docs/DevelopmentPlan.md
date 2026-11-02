# Development plan, up to the 25 November review

Dates are a proposal (today: 31 Oct). Each step ends with test drives and a log check before the next one.

| # | Step | Content | Target |
|---|---|---|---|
| 1 | Base scene ✓ | Road, ego car (WheelColliders), lead car hazard, TTC, 50 Hz logger, scene builder | done 31 Oct |
| 2 | Driving feel + data validity (this delivery) | Speed-sensitive steering, gamepad-first input, self-centering, reverse/reset/cruise (test only); gap/TTC only when the lead is relevant; collision events with target; `hazard_id` + events log; lighter scene file | 1–2 Nov, then 2–3 gamepad test drives |
| 3 | Driver twin + driver's seat | Proper driver's-seat camera (eye point of a normal car, left-hand drive) + switchable greybox interior: dashboard with working speedometer, render-texture rear-view mirror, A-pillars, windshield frame, side display. Mouse = gaze stand-in → raycast into gaze zones (Road, Hazard, Mirror, Dashboard, SideTask, OffScreen); attention buffer (AttenD values, all in Inspector); `hazardSeen` (≥100 ms); all in overlay + log. FOV choice with reasoning | ~3–9 Nov |
| — | **Git LFS setup** | Must be done BEFORE the first binary asset enters the repo (sound files, textures, 3D models). Recommended: right after Step 3 is committed, so it is ready for Step 4 | ~9 Nov |
| 4 | Warning HMI | Static FCW first (TTC 3.0 s, visual + sound), then the adaptive rule table (TTC 2/3/4 s, modality by buffer, suppressed when braking), gamepad vibration; warnings logged as events | ~10–16 Nov |
| 5 | **Visual pass** | Only what matters for the research (see below) | ~17–22 Nov |
| — | Pilot + review prep | 2–3 pilot drives by the team, quick analysis, slides | 23–24 Nov |
| 6 | After the review | Scenario + experiment manager (3–4 hazards per drive: lead braking, pedestrian, cut-in; distraction task; Static/Adaptive switch; participant ID; practice drive); Python analysis (can start earlier, in parallel) | from 26 Nov |

## Visual pass (Step 5), research-driven
| Item | Why it matters for the study |
|---|---|
| Driver's-seat interior (dashboard, bonnet, A-pillars, rear-view mirror): the Step 3 greybox replaced by a model | Realistic view and blind spots. The interior parts are gaze zones (Mirror, Dashboard) |
| Recognisable lead car with clearly visible brake lights (bright emissive + light bloom) | The brake lights are the main visual cue of the hazard: their visibility directly affects reaction time |
| Less empty environment: guard rails, road signs, some trees/buildings, lane markings as a road texture | Realistic visual load; prevents an artificially "easy" scene where the only object is the hazard |
| Lane markings as a texture on the road | Looks better than the runtime mesh and costs nothing per object |

**Git LFS must be set up before any of these assets are imported.** Asset sources must allow use in a university project (e.g. CC0 sources). We check and note each licence.

## Scene file size (2.8 MB)
Yes, it came from the ~830 lane-dash objects and ~200 posts that the builder saved into `BaseScene.unity`. Each object is several hundred bytes of text. **Done in Step 2:** markings and posts are now generated when Play starts (`RoadMarkings.cs`). All lane lines are one mesh, so the scene file only stores the road settings. The scene should now be a few tens of KB (to confirm after your rebuild). In the visual pass the markings become a road texture.

## Open hardware questions (professor)
Simulator setup, eye tracker model and whether it streams live, screen size and viewing distance (needed for the camera FOV). Steps 2–5 don't depend on the answers.

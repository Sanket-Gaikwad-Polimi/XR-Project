# Driver twin: gaze zones and attention buffer (Step 3B–D)

The driver part of the digital twin. Code: `Scripts/Twin/DriverTwin.cs`, `GazeSource.cs`, `MouseGazeSource.cs`, `EyeTrackerGazeSource.cs`, `GazeZone.cs`; debug view `Scripts/HMI/GazeDebugView.cs`; zones built in `Editor/BaseSceneBuilder.cs`.

**Basis** labels as in `BaseScene_Parameters.md`:
- **Brief**: given in our project brief.
- **Brief, TO VERIFY**: from the brief and attributed to a paper, but not yet checked against the paper by us.
- **Our choice (reason)**: a decision we made, with the reason.
- **Literature (source: TO FIND)**: nobody has checked a source yet.

---

## 1. Data flow (every physics step, 50 Hz)

```
gaze source ──► gaze point (viewport x, y) ──► gaze zone ──► attention buffer ──► attention state
                                                   │
                                                   └──► glance duration, eyes-off-road time, hazard seen
```

`DriverTwin` runs after the cars and `VehicleTwin` and before the logger (execution order 150), on the same 0.02 s steps.

## 2. Gaze source
`DriverTwin.gazeSource` can be any of these. They are swappable in the Inspector.

| Source | What it does | Use |
|---|---|---|
| `MouseGazeSource` | The mouse pointer is the gaze point. The system cursor is hidden while driving. | Home testing (drive with the keyboard, "look" with the mouse) |
| `EyeTrackerGazeSource` | **Stub**: always "no data". Its comments describe how a tracker (Pupil Labs Core via Surface Tracker + network API, or a screen-based tracker) would plug in. The product details there are **from memory, TO CHECK** in the manufacturer's docs. | Lab, once the hardware is known |
| `ScriptedGazeSource` | Fixed sequence of zones, run by `GazeBufferTest` (T key) | Verifying the buffer rules (section 10) |

Coordinates are Unity **viewport** coordinates of the driver camera: x 0 (left) to 1 (right), y 0 (bottom) to 1 (top). Outside 0..1 means off-screen.

Why an abstract class rather than a C# interface: Unity's Inspector cannot hold interface-typed fields, so `GazeSource` is an abstract `MonoBehaviour` that does the same job.

## 3. Gaze zones
| Zone | How it is detected | Geometry (car coordinates; see `DriverView.md`) |
|---|---|---|
| **OffScreen** | Gaze point outside the screen, **or no valid gaze sample** | none |
| **Mirror** | Ray hits the mirror zone collider | Mirror housing + glass, plus 0.5 cm per side |
| **Speedometer** | Ray hits the cluster zone | Whole instrument cluster (binnacle), reaching 1 cm in front of the digits |
| **SideDisplay** | Ray hits the side-display zone | Bezel + screen + text |
| **Interior** | Ray hits any other interior part | Copies of dashboard, A-pillars, header, roof liner and door trims, 0.5 cm larger per side |
| **LeadCar** | Ray hits the lead-car zone | Lead car's full bounding box: 1.8 × 1.45 × 4.5 m, wheels to roof |
| **Road** | Ray hits **no** zone (it leaves the car through a window) **and** is inside the **road-ahead window** (section 3b) | the road scene straight ahead |
| **OffRoad** | Ray hits **no** zone and is **outside** the road-ahead window | sky, median barrier and roadside nearby, near parts of the side lanes, side windows |

Rules:
- The ray goes from the driver camera through the gaze point (`Camera.ViewportPointToRay`). It is tested only against the **GazeZones** layer, and the **nearest** hit wins. Interior zones are always nearer than the lead car, so a look at the mirror is never counted as a look at the lead car behind it.
- The zone colliders are **triggers** on their own layer. All collisions of that layer are switched off at start, so they never touch the cars or the road. They are invisible: their renderers are off unless the debug view is on.
- The interior zones sit under their own **kinematic Rigidbody** (`EgoCar/Interior/GazeZones`). Colliders directly under the ego car's Rigidbody would become part of the car's physics body. Before each ray, `Physics.SyncTransforms()` makes sure the zones are exactly where the car is now.
- **Interior switched off:** the interior zones switch off with it, so every on-screen gaze counts as Road or OffRoad (by the road-ahead window). That is consistent with a physical cockpit, where a look at the real dashboard is off-screen. With a physical cockpit and an eye tracker, OffScreen would then include the physical speedometer and mirror. Mapping those needs a separate solution (TO DECIDE in the lab).

### 3b. Road-ahead window (OUR DESIGN CHOICE)
Until this change, Road was everything seen through the windows, so looking at the sky, the median barrier or the roadside kept the driver "Attentive" (found by Sanket in testing). Now a gaze out of the windows counts as **Road only inside a window around the straight-ahead direction**. Outside it, the zone is **OffRoad**, which drains the buffer like the side display.

| Parameter (Inspector, DriverTwin) | Default | Basis |
|---|---|---|
| roadAheadHalfWidthDeg | 12° left and right | Our choice |
| roadAheadDownDeg | 8° below straight ahead: road surface from about 8.5 m ahead (eye 1.2 m high). The dashboard edge is at about 10.8° down | Our choice |
| roadAheadUpDeg | 4° above: covers the top of the lead car even at a 2 m gap (about 3°); above is sky | Our choice |

- **Reference direction**: the driver camera's forward direction, i.e. the car's heading (the camera has no pitch). On our straight road this equals the road direction, except during lane changes (a few degrees). An eye tracker would also give gaze in the car's frame.
- **Shape**: a rectangle on the camera's image plane (|x/z| ≤ tan 12°, −tan 8° ≤ y/z ≤ tan 4°), so it is a rectangle on the screen. With Z (test mode) it is drawn as a green outline.
- **What falls inside**, with our road (lanes 3.5 m, ego in the right lane, eye 6.38 m from the median barrier): our own lane from 8.5 m ahead, the inner part of the left lane further ahead, the median barrier only beyond about 30 m (6.38 m / tan 12°), the right road edge beyond about 22 m, and the speed signs from about 30 m (they are driving-relevant).
- **Literature**: AttenD's "field relevant for driving" and the "road centre" areas used in other gaze measures are defined in their papers. **TO FIND/CHECK** which definition applies, then set the three parameters from it and change "Our choice" to the reference.

Interpretation choices (ours, **to check against the AttenD paper**):
1. **Road = gaze out of the windows inside the road-ahead window** (section 3b). Outside it = OffRoad (drains). The window size is our choice until AttenD's "field relevant for driving" is checked (Kircher & Ahlström, TO VERIFY).
2. **No gaze data counts as OffScreen** (the buffer drains). With a real eye tracker, blinks and short tracking losses need a rule, for example "hold the last zone for up to X ms". TO DECIDE, and check how AttenD treats blinks.
3. The **lead-car zone has no angular margin**. That is fine for the mouse. With an eye tracker it should be enlarged by the tracker's accuracy (the angle at the lead car's distance), otherwise hazard detection depends on calibration quality. TO DECIDE when the hardware is known.

## 4. Attention buffer
| Parameter (Inspector) | Default | Basis |
|---|---|---|
| bufferMax | 2.0 s | Brief, TO VERIFY (AttenD) |
| bufferStart | 2.0 s (starts full) | Brief |
| refillRate | 1 s per s | Brief, TO VERIFY (AttenD) |
| refillDelay | 0.1 s | Brief, TO VERIFY (AttenD) |
| drainRate | 1 s per s | Brief, TO VERIFY (AttenD) |
| mirrorSpeedometerLatency | 1.0 s | Brief, TO VERIFY (AttenD) |
| Clamp | 0 … bufferMax | Brief |

Rules per step (dt = 0.02 s; the zone of this step is assumed for the whole interval (t − dt, t]):

```
on Road or LeadCar:            buffer += refillRate × Portion(time back on the road, refillDelay)
on Mirror or Speedometer:      buffer −= drainRate  × Portion(time in this glance, mirrorSpeedometerLatency)
on SideDisplay/Interior/OffRoad/OffScreen:  buffer −= drainRate × dt
buffer = clamp(buffer, 0, bufferMax)

Portion(held, delay) = clamp(held − delay, 0, dt)     (held includes this step)
```

`Portion` applies the delay exactly within a step, so the result does not depend on whether dt divides the delay evenly. For example, with a delay of 0.1 s, steps 1 to 5 back on the road add nothing and step 6 adds 0.02 s.

Interpretation choices (ours, **to check against the AttenD paper**):
4. **The refill delay counts from the return to the road group.** Moving between Road and LeadCar does not restart it, because both are "eyes on the road".
5. **The 1.0 s mirror/speedometer latency is per glance.** Looking directly from the mirror to the speedometer starts a new glance with a new 1 s latency. This is a possible loophole, though rare in practice. The alternative is to count the latency from leaving the road.

## 5. Attention state (OUR DESIGN CHOICE, not from the literature)
| State | Buffer | Basis |
|---|---|---|
| Attentive | ≥ 1.5 s | **Our design choice** |
| Reduced | 0.5 s ≤ buffer < 1.5 s | **Our design choice** |
| Distracted | < 0.5 s | **Our design choice** |

The lower limit of each band belongs to the higher state: exactly 1.5 is Attentive and exactly 0.5 is Reduced. Comparisons use a tolerance of 10⁻⁶ s, so float rounding of dt cannot flip a state.

## 6. Other outputs
| Output | Definition | Basis |
|---|---|---|
| Zone, GlanceDuration | Current zone, and the continuous time in it so far, including this step | Definition (ours) |
| EyesOffRoad | Continuous time since the gaze left Road/LeadCar. Mirror and speedometer count as off-road. 0 while on the road. | Definition (ours) |
| HazardSeen | After the hazard onset, gaze on the LeadCar zone for ≥ **100 ms** without interruption. Latched for the rest of the drive. | Brief. Whether 100 ms matches the usual minimum fixation duration: literature (source: TO FIND) |
| HazardFirstFixationTime | Drive time when that look **started**. If the driver was already looking at the lead car at onset, the look counts from the onset, so the time since onset is 0. | Our choice (definition) |
| HazardFixationSinceOnset | First fixation time − onset time | Definition |

This is a **dwell in the zone**, not a fixation detected by an eye-movement algorithm (velocity/dispersion). With the mouse there are no fixations in that sense. With an eye tracker, a fixation filter could be added later (TO DECIDE).

## 7. Events (events CSV)
| Event | Target | Detail |
|---|---|---|
| GlanceStart | zone | |
| GlanceEnd | zone | `duration_s`; at the end of the drive also `reason=drive_end` (the open glance is closed at DriveEnd, so every GlanceStart has a GlanceEnd) |
| AttentionStateChanged | new state | `from`, `buffer_s` |
| HazardFirstFixation | hazard id | `since_onset_s`, `fixation_start_s`, `confirmed_at_s` (= start + 100 ms), `looking_at_lead_at_onset` |

The test events `GazeTestStart` and `GazeTestEnd` mark the scripted test (section 10). `check_drive.py` leaves those rows out of the gaze measures.

## 7b. Log columns (main CSV, one row per physics step, added at the end)
| Column | Meaning |
|---|---|
| gaze_x, gaze_y | Gaze point in viewport coordinates: 0..1, origin **bottom-left**, values outside 0..1 are off-screen. **Empty** = no valid gaze sample. |
| gaze_zone | Road, LeadCar, Mirror, Speedometer, SideDisplay, Interior, OffRoad or OffScreen (OffRoad only in logs made after the road-ahead window was added; in older logs that gaze was Road) |
| glance_duration_s | Time in the current zone so far |
| buffer_s | Attention buffer after this step |
| attention_state | Attentive, Reduced or Distracted |
| eyes_off_road_s | Continuous time off Road/LeadCar (0 on the road) |
| hazard_seen | 0 until the first ≥ 100 ms look at the lead car after onset, then 1 |
| interior_on | 1 if the virtual interior is visible |

The meta file adds `gaze_source`, every buffer parameter, the state limits (marked "our design choice"), `hazard_dwell_min_s`, and the result `hazard_seen` / `hazard_first_fixation_time_s` / `hazard_first_fixation_since_onset_s`. The meta file is rewritten when the first fixation happens.

## 7c. Overlay (test mode only, F1)
The overlay has three modes (F1: COMPACT → FULL → OFF). Both panels contain a small **attention buffer bar**: full width is 2.0 s and the colour is the state (green, yellow or red); white marks show the 0.5 and 1.5 limits. Next to it: the buffer value, the state and the current gaze zone. COMPACT also shows "Hazard seen" (with the time after onset) and the gaze-test result. FULL adds the **Gaze** line: zone and glance time, eyes-off-road, hazard seen and the gaze source. (Until the overlay redesign the bar was a wide bar under the box.)

## 7d. `check_drive.py` gaze measures
- **Glances per zone** over the whole drive: count, total time, share of time, mean and max duration. The first and last glance are cut by the drive start and end.
- **Eyes off road in the 10 s before onset**: share of rows not on Road/LeadCar. The 10 s window is from the brief.
- **Minimum attention buffer before onset**, over the whole drive and over the last 10 s, plus the buffer, state and zone at onset.
- **Time to first fixation on the lead car after onset**, from the `HazardFirstFixation` event. It is cross-checked from the rows with the same definition, and a warning is printed if the two differ by more than one step.
- **Response timing**: time to first fixation and brake reaction time side by side, plus fixation → brake. A **warning** is printed when the brake was pressed **before** the first fixation. With mouse gaze this means the mouse was not where the driver actually looked, as in drive 21:33 (brake +0.70 s, fixation +2.28 s). With an eye tracker it could mean braking from peripheral vision, or a look the tracker or zone missed.
- Logs from Step 3 B–D (gaze events but no per-row gaze columns) still get the first fixation and the response timing, from the events.
- Warnings: mouse gaze (not eye data), interior switched off during the drive, scripted test rows excluded, and lead car never looked at after onset.

## 8. Expected values for the scripted test sequence (Part F)
Sequence: 3 s Road → 1.5 s Mirror → 2.5 s SideDisplay → 3 s Road, starting with a full buffer. These values follow from the rules above. A simulation of the same rules at 50 Hz reproduces them exactly.

| Time (s) | Gaze | Buffer (s) | State | Why |
|---|---|---|---|---|
| 0 – 3.0 | Road | 2.0 | Attentive | Full, refill has nothing to add |
| 3.0 – 4.0 | Mirror | 2.0 | Attentive | First 1.0 s of a mirror glance: no drain |
| 4.5 | Mirror (end) | **1.5** | Attentive | 0.5 s of drain after the latency. 1.5 is still Attentive (band limit inclusive) |
| 4.52 | SideDisplay | 1.48 | **Reduced** | Immediate drain |
| 5.5 | SideDisplay | 0.5 | Reduced | |
| 5.52 | SideDisplay | 0.48 | **Distracted** | |
| 6.0 | SideDisplay | **0.0** | Distracted | 1.5 s drained in 1.5 s, then clamped at 0 |
| 7.0 | SideDisplay (end) | 0.0 | Distracted | |
| 7.1 | Road | 0.0 | Distracted | 0.1 s refill delay |
| 7.6 | Road | 0.5 | **Reduced** | |
| 8.6 | Road | 1.5 | **Attentive** | |
| 9.1 | Road | **2.0** | Attentive | Full again: 0.1 s delay + 2.0 s refill |
| 10.0 | Road (end) | 2.0 | Attentive | |

Eyes-off-road reaches 4.0 s at t = 7.0 (1.5 s mirror + 2.5 s side display) and drops to 0 at the first Road step.

## 10. Scripted gaze test (Part F)
**T** in test mode, or tick `GazeBufferTest.runAtDriveStart`, runs the sequence in section 8:
1. The twin is reset to a full buffer and its gaze source is switched to `ScriptedGazeSource`.
2. For each zone, the scripted source produces a real **screen point**. For zones with colliders it is the zone's centre projected onto the screen, checked by casting the gaze ray, with a grid search if something is in front. For Road it is the road surface 12 m ahead (5.7° down, inside the road-ahead window), for OffRoad the sky 10° up (or 18° left/right at the horizon), and for OffScreen a point left of the screen. So the whole chain is tested: point, ray, zone, buffer. Segments are whole physics steps (round(seconds / 0.02)).
3. Every step, the twin's buffer, state, zone and eyes-off-road time are compared with **expected values**. These come from a separate continuous-time calculation of the rules, written independently of the twin's step-by-step code. Tolerance is 0.001 s.
3b. If the sequence and all parameters are the defaults, the buffer and state are **also** checked against the hand-calculated table in **section 8** (13 checkpoints). The report lists them as `doc … measured … OK`. If anything was changed in the Inspector, this part is skipped and the report says so.
4. Afterwards the gaze source switches back. A report goes to the Console and to `DriveLogs/drive_…_gazetest.txt`: PASS/FAIL, the mismatch counts, and a table every 0.5 s and at every segment end. The overlay shows the result too.

Before writing the C#, we checked the same two models against each other in Python on the default sequence and 200 random sequences. They agreed at every step (max difference 2·10⁻¹⁶ s).

A **zone mismatch** means a planned zone could not be hit on screen. For example the interior is off, or the FOV is too narrow to show the mirror (Geometric 18.4°).

## 9. Known limitations
- Mouse gaze is not eye gaze. It moves only when you move it, so its glances are longer and cleaner than real ones. It is good for testing the logic, not for estimating real glance behaviour.
- The 50 Hz update means every time is resolved to 20 ms.
- When the chase camera (C) is on, the ray still starts from the driver camera, because the twin always describes the driver. The gaze dot is drawn on the chase view, so the dot and the zone do not match there. Test only.

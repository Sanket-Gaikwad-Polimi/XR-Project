# Hazard trigger: options for the team decision

**Problem:** with a fixed 80 m trigger distance, criticality depended on the driver's speed. In Sanket's drive the hazard started at 151 km/h after 94 s of waiting (time headway 1.9 s, TTC 4.7 s). Another participant at 90 km/h would get a 3.2 s headway at the same distance, which is a much easier event. Reaction times and collisions would then reflect the participant's speed choice, not the warning condition.

**What the code does now:** after the random planned time (20–40 s), the lead car brakes only once ALL enabled conditions have held continuously for `conditionsHoldTime`:
- ego car behind the lead car in the same lane (`requireSameLane`)
- time headway (gap / ego speed) inside `[minTimeHeadway, maxTimeHeadway]`
- ego speed inside `[minEgoSpeedKmh, maxEgoSpeedKmh]`
- optional: |ego speed − lead speed| ≤ `maxAbsClosingSpeed` (steady following)

If that doesn't happen within `maxWaitSeconds`, the hazard is skipped and logged as `HazardSkipped`, including the share of waiting time each condition was met (useful for tuning). The criticality at onset (ego speed, lead speed, gap, time headway, TTC, closing speed) is logged in the `HazardOnset` event and the meta file.

All values below are **placeholders**. We have not checked any of them against literature yet. The current Inspector defaults are option B, chosen only so that test drives trigger reliably.

| | A. Narrow window | B. Wide window (current test default) | C. Lead car regulates the headway |
|---|---|---|---|
| Idea | Tight headway and speed band, steady following required | Broad band, no steadiness check | Before the hazard, the lead car adapts its speed so the driver's headway converges to a target; then it brakes |
| Example values | THW 1.5–2.0 s, speed 80–100 km/h, closing ≤ 1.5 m/s, hold 1 s | THW 1.0–3.0 s, speed 70–110 km/h, hold 1 s | target THW e.g. 1.5 s ± 0.1 s |
| Pros | Very comparable criticality between participants and conditions | Rarely skipped; natural driving | Almost identical criticality every time; no skipped hazards |
| Cons | Drivers who keep a longer gap may never trigger → skips or long waits; needs good speed guidance | Criticality still varies → more variance in reaction time; onset criticality must go into the analysis as a covariate | Lead car behaviour reacts to the participant (less natural, possibly noticeable); more code; we must check whether this is accepted practice |
| Code status | Ready (Inspector values only) | Ready (current defaults) | Not built; ~1 day of work |

**To decide as a team (after the literature):** which option; the headway/speed values; the hold time; the max wait; and how a skipped hazard is handled in the study (e.g. repeat it later in the drive, or count it as missing data).

**Related guidance (built):** speed-limit signs (value = `DriveSession.speedLimitKmh`, placeholder 90 km/h = lead car speed), and the instruction "Follow the car ahead", currently shown in the debug overlay. Nothing limits or controls the speed.

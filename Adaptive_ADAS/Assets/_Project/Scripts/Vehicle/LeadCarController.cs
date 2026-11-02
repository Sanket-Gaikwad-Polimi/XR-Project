// LeadCarController.cs
// The car in front of the driver, in the same lane. It is SCRIPTED (not driven by
// tyre physics) so that the hazard is exactly the same for every participant:
//   1. Cruising: drives at a constant speed.
//   2. After a random "planned time" inside [brakeWindowStart, brakeWindowEnd] it
//      WAITS until the driving situation meets the trigger conditions below, then
//      brakes hard with a set deceleration until it stops.
//   3. If the conditions are not met within maxWaitSeconds, the hazard is SKIPPED
//      (event "HazardSkipped") and the lead car just keeps cruising.
//
// WHY CONDITIONS ON TIME HEADWAY AND SPEED (not a fixed distance)?
//   How critical a lead-car braking event is depends on time, not distance: the same
//   40 m gap is 1.6 s at 90 km/h but only 1.0 s at 150 km/h. Triggering at a time
//   headway window and a speed window makes the hazard comparably critical for every
//   participant. The VALUES are placeholders: the team decides them after reading the
//   literature (see Docs/HazardTriggerOptions.md).
//
// The criticality at onset (ego speed, lead speed, gap, time headway, TTC, closing
// speed) is recorded in the HazardOnset event and in the meta file.
//
// It uses a kinematic Rigidbody: it moves where we tell it, but the ego car still
// collides with it (counted by EgoCarController as a "Lead" collision event).

using System.Globalization;
using AdaptiveADAS.Experiment;
using AdaptiveADAS.Logging;
using AdaptiveADAS.Twin;
using UnityEngine;

namespace AdaptiveADAS.Vehicle
{
    [RequireComponent(typeof(Rigidbody))]
    public class LeadCarController : MonoBehaviour
    {
        public enum LeadState { Cruising, WaitingForConditions, Braking, Stopped, Skipped }

        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public VehicleTwin twin;
        public DriveEvents events;
        [Tooltip("Brake light objects: left, right and centre high-mounted lamp (switched between the two materials below).")]
        public Renderer[] brakeLights;
        public Material brakeLightOff;
        public Material brakeLightOn;

        [Header("Identification")]
        [Tooltip("Name of this hazard in the logs (hazard_id column and events).")]
        public string hazardId = "H1_LeadBrake";

        [Header("Cruising")]
        [Tooltip("Constant speed before the hazard (km/h). Our choice: 90 km/h.")]
        public float cruiseSpeedKmh = 90f;

        [Header("Planned time (random, uniform)")]
        [Tooltip("Earliest drive time (s) at which the trigger conditions start being checked.")]
        public float brakeWindowStart = 20f;
        [Tooltip("Latest drive time (s) at which the trigger conditions start being checked.")]
        public float brakeWindowEnd = 40f;

        [Header("Trigger conditions (PLACEHOLDER values - team decision pending)")]
        [Tooltip("Ego car must be behind the lead car in the same lane (lateral overlap).")]
        public bool requireSameLane = true;
        [Tooltip("Time headway window (s): gap / ego speed.")]
        public float minTimeHeadway = 1.0f;
        public float maxTimeHeadway = 3.0f;
        [Tooltip("Ego speed window (km/h).")]
        public float minEgoSpeedKmh = 70f;
        public float maxEgoSpeedKmh = 110f;
        [Tooltip("Optional: the ego car must be following steadily (|ego speed - lead speed| below the limit).")]
        public bool limitClosingSpeed = false;
        [Tooltip("Max |closing speed| (m/s) when limitClosingSpeed is on.")]
        public float maxAbsClosingSpeed = 2f;
        [Tooltip("All conditions must hold continuously for this long (s) before braking starts (avoids triggering on a brief pass-through).")]
        public float conditionsHoldTime = 1.0f;
        [Tooltip("If the conditions are not met within this time after the planned time (s), the hazard is skipped.")]
        public float maxWaitSeconds = 60f;

        [Header("Hard braking")]
        [Tooltip("Deceleration during hard braking (m/s^2). 6 m/s^2 ~ 0.6 g, TO VERIFY in literature.")]
        public float brakeDecel = 6f;
        [Tooltip("Time to build up to full deceleration (s). 0 = instant. Real brakes need a few tenths of a second (TO VERIFY).")]
        public float decelRampTime = 0f;

        [Header("Randomness")]
        [Tooltip("On = the same planned time every drive (useful for testing). Off = new random time each drive.")]
        public bool useFixedSeed = false;
        public int seed = 12345;

        // ---- Outputs ----
        public LeadState State { get; private set; } = LeadState.Cruising;
        public float Speed { get; private set; }               // m/s
        public Rigidbody Body { get; private set; }
        public double PlannedBrakeTime { get; private set; }
        /// <summary>Drive time at which braking actually started (s). NaN until it happens.</summary>
        public double BrakeOnsetTime { get; private set; } = double.NaN;
        public int UsedSeed { get; private set; }
        /// <summary>True from hazard onset until the ego car has stopped behind, passed, or changed lane.</summary>
        public bool HazardActive { get; private set; }
        /// <summary>"pending", "triggered" or "skipped" (written to the meta file).</summary>
        public string Outcome { get; private set; } = "pending";
        /// <summary>True while the brake lights are lit (logged as lead_brake_lights_on).</summary>
        public bool BrakeLightsOn { get; private set; }
        /// <summary>Live description of the trigger check, for the debug overlay.</summary>
        public string TriggerStatus { get; private set; } = "";

        // Criticality at onset (NaN until onset).
        public float OnsetEgoSpeedKmh { get; private set; } = float.NaN;
        public float OnsetLeadSpeedKmh { get; private set; } = float.NaN;
        public float OnsetGap { get; private set; } = float.NaN;
        public float OnsetTimeHeadway { get; private set; } = float.NaN;
        public float OnsetTTC { get; private set; } = float.NaN;
        public float OnsetClosingSpeed { get; private set; } = float.NaN;
        public float WaitedSeconds { get; private set; }

        private float currentDecel, holdTimer;
        private float laneOkTime, thwOkTime, speedOkTime, closingOkTime;   // for the skip report
        private bool hazardEnded;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.Interpolate;

            Speed = cruiseSpeedKmh / 3.6f;

            UsedSeed = useFixedSeed ? seed : System.Environment.TickCount;
            var rng = new System.Random(UsedSeed);
            PlannedBrakeTime = brakeWindowStart + rng.NextDouble() * (brakeWindowEnd - brakeWindowStart);
            Debug.Log($"[LeadCar] Planned time: {PlannedBrakeTime:F2} s (seed {UsedSeed}).");

            SetBrakeLights(false);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            double now = session != null ? session.DriveTime : Time.fixedTimeAsDouble;

            switch (State)
            {
                case LeadState.Cruising:
                    TriggerStatus = $"planned at {PlannedBrakeTime:F1} s";
                    if (now >= PlannedBrakeTime) State = LeadState.WaitingForConditions;
                    break;

                case LeadState.WaitingForConditions:
                    CheckTriggerConditions(now, dt);
                    break;

                case LeadState.Braking:
                    currentDecel = decelRampTime > 0f
                        ? Mathf.MoveTowards(currentDecel, brakeDecel, brakeDecel / decelRampTime * dt)
                        : brakeDecel;
                    Speed = Mathf.Max(0f, Speed - currentDecel * dt);
                    if (Speed <= 0f) State = LeadState.Stopped;
                    break;

                case LeadState.Stopped:
                    Speed = 0f;
                    if (HazardActive && !hazardEnded && twin != null &&
                        (!twin.LeadRelevant || Mathf.Abs(twin.EgoSpeed) < 0.5f))
                    {
                        HazardActive = false;
                        hazardEnded = true;
                        if (events != null)
                            events.Record("HazardEnd", hazardId,
                                twin.LeadRelevant ? "reason=ego_stopped" : "reason=ego_passed_or_changed_lane");
                    }
                    break;

                case LeadState.Skipped:
                    break;   // keeps cruising, no hazard in this drive
            }

            Body.MovePosition(Body.position + transform.forward * (Speed * dt));
        }

        private void CheckTriggerConditions(double now, float dt)
        {
            WaitedSeconds += dt;

            // Current situation (computed here so it works in any lane).
            float egoSpeed = twin.EgoSpeed;
            float egoKmh = egoSpeed * 3.6f;
            float thw = egoSpeed > 0.5f && twin.LongitudinalGap > 0f ? twin.LongitudinalGap / egoSpeed : float.NaN;
            float closing = egoSpeed - Speed;

            bool laneOk = !requireSameLane || twin.LeadRelevant;
            bool thwOk = !float.IsNaN(thw) && thw >= minTimeHeadway && thw <= maxTimeHeadway;
            bool speedOk = egoKmh >= minEgoSpeedKmh && egoKmh <= maxEgoSpeedKmh;
            bool closingOk = !limitClosingSpeed || Mathf.Abs(closing) <= maxAbsClosingSpeed;

            if (laneOk) laneOkTime += dt;
            if (thwOk) thwOkTime += dt;
            if (speedOk) speedOkTime += dt;
            if (closingOk) closingOkTime += dt;

            bool allOk = laneOk && thwOk && speedOk && closingOk;
            holdTimer = allOk ? holdTimer + dt : 0f;

            TriggerStatus =
                $"waiting {WaitedSeconds:F0}/{maxWaitSeconds:F0} s: " +
                $"lane {(laneOk ? "OK" : "no")}, headway {(thwOk ? "OK" : "no")} ({(float.IsNaN(thw) ? "--" : thw.ToString("F1"))} s), " +
                $"speed {(speedOk ? "OK" : "no")}" + (limitClosingSpeed ? $", closing {(closingOk ? "OK" : "no")}" : "");

            if (holdTimer >= conditionsHoldTime)
            {
                StartBraking(now, egoKmh, thw);
            }
            else if (WaitedSeconds >= maxWaitSeconds)
            {
                State = LeadState.Skipped;
                Outcome = "skipped";
                TriggerStatus = "SKIPPED (conditions not met in time)";
                if (events != null)
                    events.Record("HazardSkipped", hazardId,
                        "waited_s=" + F(WaitedSeconds) +
                        ";last_ego_speed_kmh=" + F(egoKmh) + ";last_thw_s=" + F(thw) +
                        ";lane_ok_pct=" + Pct(laneOkTime) + ";thw_ok_pct=" + Pct(thwOkTime) +
                        ";speed_ok_pct=" + Pct(speedOkTime) + ";closing_ok_pct=" + Pct(closingOkTime));
            }
        }

        private void StartBraking(double now, float egoKmh, float thw)
        {
            State = LeadState.Braking;
            Outcome = "triggered";
            BrakeOnsetTime = now;
            HazardActive = true;
            SetBrakeLights(true);
            TriggerStatus = $"BRAKING since {now:F2} s";

            // Criticality at onset.
            OnsetEgoSpeedKmh = egoKmh;
            OnsetLeadSpeedKmh = Speed * 3.6f;
            OnsetGap = twin.LongitudinalGap;
            OnsetTimeHeadway = thw;
            OnsetClosingSpeed = twin.EgoSpeed - Speed;
            OnsetTTC = OnsetClosingSpeed > 0.05f && OnsetGap > 0f ? OnsetGap / OnsetClosingSpeed : float.NaN;

            if (events != null)
                events.Record("HazardOnset", hazardId,
                    "ego_speed_kmh=" + F(OnsetEgoSpeedKmh) + ";lead_speed_kmh=" + F(OnsetLeadSpeedKmh) +
                    ";gap_m=" + F(OnsetGap) + ";thw_s=" + F(OnsetTimeHeadway, "F2") + ";ttc_s=" + F(OnsetTTC, "F2") +
                    ";closing_mps=" + F(OnsetClosingSpeed, "F2") + ";waited_s=" + F(WaitedSeconds) +
                    ";decel_mps2=" + F(brakeDecel));
        }

        /// <summary>Number as text with a dot; empty if not defined.</summary>
        private static string F(float v, string format = "F1") =>
            float.IsNaN(v) || float.IsInfinity(v) ? "" : v.ToString(format, Inv);

        private string Pct(float t) => WaitedSeconds > 0f ? (100f * t / WaitedSeconds).ToString("F0", Inv) : "";

        private void SetBrakeLights(bool on)
        {
            BrakeLightsOn = on;
            if (brakeLights == null) return;
            Material m = on ? brakeLightOn : brakeLightOff;
            if (m == null) return;
            foreach (Renderer r in brakeLights)
                if (r != null) r.sharedMaterial = m;
        }
    }
}

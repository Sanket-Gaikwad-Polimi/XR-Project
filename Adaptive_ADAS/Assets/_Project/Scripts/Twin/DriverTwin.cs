// DriverTwin.cs  (on DriveSystems, next to VehicleTwin)
// The DRIVER part of the digital twin: where the driver looks and how attentive they are.
// Updated once per physics step (50 Hz), like VehicleTwin.
//
// Every step:
//   1. Gaze sample from the gaze source (mouse, Pupil eye tracker, fake Pupil stream, or the
//      scripted test sequence): a WORLD RAY (eye + line of sight), valid flag, confidence.
//   2. Gaze ZONE: off-screen check, then the gaze ray against the invisible zone colliders
//      (layer "GazeZones"). Nearest hit wins.
//      No hit = the ray leaves the car through a window: Road if it is inside the ROAD-AHEAD
//      WINDOW around the straight-ahead direction (default 12 deg left/right, 8 deg down,
//      4 deg up; OUR DESIGN CHOICE), otherwise OffRoad (sky, barrier, roadside).
//   3. ATTENTION BUFFER (AttenD-style, Kircher & Ahlström; all values from the project
//      brief and TO VERIFY against the paper by us):
//        - buffer in seconds, 0 .. 2.0, starts full (2.0)
//        - Road / LeadCar:        +1 s per s, but only after 0.1 s back on the road
//        - Mirror / Speedometer:  no change for the first 1.0 s of the glance, then -1 s per s
//        - SideDisplay / Interior / OffRoad / OffScreen: -1 s per s immediately
//   4. ATTENTION STATE from the buffer (band limits are OUR DESIGN CHOICE):
//        Attentive >= 1.5 s,  Reduced 0.5 .. 1.5 s,  Distracted < 0.5 s
//   5. HAZARD SEEN: after the hazard onset, gaze on the LeadCar zone for >= 100 ms without
//      interruption. The time this dwell started is the "first fixation" time.
//      (A dwell in the zone, not a fixation detected from eye movements.)
//
// Timing: each step covers the interval (t - dt, t], dt = 0.02 s. The zone found at
// step t is assumed for that whole interval. Delays/latencies are applied exactly
// within a step (see Portion), so results do not depend on dt dividing them evenly.
// Full description and the basis of each parameter: Docs/DriverTwin.md.
//
// [DefaultExecutionOrder(150)] = after the cars (0) and VehicleTwin (100), before the logger (200).

using System;
using System.Globalization;
using AdaptiveADAS.Experiment;
using AdaptiveADAS.Logging;
using AdaptiveADAS.Vehicle;
using UnityEngine;

namespace AdaptiveADAS.Twin
{
    public enum AttentionState { Attentive, Reduced, Distracted }

    [DefaultExecutionOrder(150)]
    public class DriverTwin : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public DriveEvents events;
        public LeadCarController leadCar;
        [Tooltip("The driver camera: the gaze ray starts here.")]
        public Camera driverCamera;
        [Tooltip("Where the gaze comes from: MouseGazeSource (home), PupilGazeSource (eye tracker or fake stream), " +
                 "ScriptedGazeSource (self-test). Set at Play by GazeSourceSwitch.")]
        public GazeSource gazeSource;

        [Header("Gaze zones")]
        [Tooltip("Layer of the zone colliders (created by the scene builder).")]
        public string gazeLayerName = "GazeZones";
        [Tooltip("Longest gaze ray (m). The lead car must be closer than this to be detected.")]
        public float maxRayDistance = 1000f;

        [Header("Road-ahead window (OUR DESIGN CHOICE, see Docs/DriverTwin.md section 3b)")]
        [Tooltip("Gaze out of the windows counts as Road only inside this window around the driver's straight-ahead " +
                 "direction (camera forward, i.e. the car's heading). Outside it = OffRoad, which drains the buffer. " +
                 "Half width, left and right (deg).")]
        public float roadAheadHalfWidthDeg = 12f;
        [Tooltip("Lower edge, below straight ahead (deg). 8 deg = road surface about 8.5 m ahead (eye 1.2 m high).")]
        public float roadAheadDownDeg = 8f;
        [Tooltip("Upper edge, above straight ahead (deg). Above it is sky.")]
        public float roadAheadUpDeg = 4f;

        [Header("Attention buffer (brief values, AttenD-style; TO VERIFY against Kircher & Ahlström)")]
        [Tooltip("Maximum buffer (s).")]
        public float bufferMax = 2.0f;
        [Tooltip("Buffer at the start of the drive (s). Starts full.")]
        public float bufferStart = 2.0f;
        [Tooltip("Refill rate on Road / LeadCar (s of buffer per s).")]
        public float refillRate = 1.0f;
        [Tooltip("Time back on the road before the refill starts (s).")]
        public float refillDelay = 0.1f;
        [Tooltip("Drain rate when looking away (s of buffer per s).")]
        public float drainRate = 1.0f;
        [Tooltip("Mirror / Speedometer: drain starts only after this glance time (s).")]
        public float mirrorSpeedometerLatency = 1.0f;

        [Header("Attention states (OUR DESIGN CHOICE, not from the literature)")]
        [Tooltip("Buffer >= this: Attentive.")]
        public float attentiveMin = 1.5f;
        [Tooltip("Buffer < this: Distracted. In between: Reduced.")]
        public float distractedBelow = 0.5f;

        [Header("Hazard detection")]
        [Tooltip("Uninterrupted gaze on the lead car after the onset needed for 'hazard seen' (s). Brief value.")]
        public float hazardDwellMin = 0.1f;

        // ---------------- Outputs (read by logger and overlay) ----------------
        public bool GazeValid { get; private set; }
        /// <summary>The gaze sample of this step (as returned by the gaze source).</summary>
        public GazeSample Sample { get; private set; } = GazeSample.Invalid;
        /// <summary>Gaze point in viewport coordinates of the DRIVER camera (0..1, origin bottom-left):
        /// the gaze ray projected into that camera. NaN when not valid.</summary>
        public Vector2 GazeViewport { get; private set; } = new Vector2(float.NaN, float.NaN);
        /// <summary>Gaze direction relative to the driver's straight-ahead direction (deg):
        /// azimuth positive = right, elevation positive = up. NaN when not valid.
        /// Comparable across mouse, eye tracker and (later) headset.</summary>
        public float GazeAzimuthDeg { get; private set; } = float.NaN;
        public float GazeElevationDeg { get; private set; } = float.NaN;
        public GazeZoneType Zone { get; private set; } = GazeZoneType.Road;
        /// <summary>Time in the current zone so far (s), including this step.</summary>
        public double GlanceDuration { get; private set; }
        /// <summary>Continuous time with the eyes off Road/LeadCar (s); 0 while on the road.</summary>
        public double EyesOffRoad { get; private set; }
        public double Buffer { get; private set; }
        public AttentionState State { get; private set; } = AttentionState.Attentive;
        public bool HazardSeen { get; private set; }
        /// <summary>Drive time when the first qualifying look at the lead car started (s), NaN until seen.</summary>
        public double HazardFirstFixationTime { get; private set; } = double.NaN;
        /// <summary>HazardFirstFixationTime minus the hazard onset time (s), NaN until seen.</summary>
        public double HazardFixationSinceOnset { get; private set; } = double.NaN;
        public string GazeSourceName => gazeSource != null ? gazeSource.SourceName : "none";

        // ---------------- Internal state ----------------
        private int gazeMask;
        private bool firstStep = true;
        private bool glanceClosed;               // the last glance was closed at the end of the drive
        private double onRoadTime;               // continuous time on Road/LeadCar (s)
        private double leadDwellStart = double.NaN;
        private const double Eps = 1e-6;         // tolerance for comparisons (float rounding of dt)
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private void Awake()
        {
            int layer = LayerMask.NameToLayer(gazeLayerName);
            if (layer < 0)
            {
                Debug.LogError($"[DriverTwin] Layer '{gazeLayerName}' not found. Rebuild the scene (ADAS > Build Base Scene).");
                gazeMask = 0;
            }
            else
            {
                gazeMask = 1 << layer;
                // The zone colliders must never touch anything (they are only for the gaze ray).
                for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(layer, i, true);
            }
            Buffer = Mathf.Clamp(bufferStart, 0f, bufferMax);
            State = StateFor(Buffer);
        }

        private void FixedUpdate()
        {
            if (session == null || glanceClosed) return;   // nothing more after the drive has ended
            double dt = Time.fixedDeltaTime;
            double now = session.DriveTime;      // end of this step's interval

            // ---- 1. Gaze sample ----
            GazeSample sample = GazeSample.Invalid;
            GazeValid = gazeSource != null && gazeSource.TryGetGazeSample(out sample) && sample.valid;
            Sample = GazeValid ? sample : GazeSample.Invalid;
            UpdateGazeDirection();

            // ---- 2. Zone ----
            GazeZoneType zone = GazeValid ? Classify(sample) : GazeZoneType.OffScreen;   // no data = treated as off-screen (our choice)
            bool onRoad = GazeZone.IsOnRoad(zone);

            if (firstStep || zone != Zone)
            {
                if (!firstStep)
                    Rec("GlanceEnd", Zone.ToString(), "duration_s=" + GlanceDuration.ToString("F3", Inv));
                Rec("GlanceStart", zone.ToString());
                GlanceDuration = dt;
            }
            else
            {
                GlanceDuration += dt;
            }

            bool wasOnRoad = !firstStep && GazeZone.IsOnRoad(Zone);
            if (onRoad)
            {
                onRoadTime = wasOnRoad || firstStep ? onRoadTime + dt : dt;
                EyesOffRoad = 0;
            }
            else
            {
                EyesOffRoad = wasOnRoad || firstStep ? dt : EyesOffRoad + dt;
                onRoadTime = 0;
            }
            Zone = zone;

            // ---- 3. Attention buffer ----
            double change;
            if (onRoad)
                change = +refillRate * Portion(onRoadTime, refillDelay, dt);
            else if (zone == GazeZoneType.Mirror || zone == GazeZoneType.Speedometer)
                change = -drainRate * Portion(GlanceDuration, mirrorSpeedometerLatency, dt);
            else
                change = -drainRate * dt;
            Buffer = Math.Max(0.0, Math.Min(bufferMax, Buffer + change));

            // ---- 4. State ----
            AttentionState newState = StateFor(Buffer);
            if (newState != State)
            {
                Rec("AttentionStateChanged", newState.ToString(),
                    $"from={State};buffer_s={Buffer.ToString("F3", Inv)}");
                State = newState;
            }

            // ---- 5. Hazard seen (first look at the lead car after the onset) ----
            UpdateHazardSeen(now, dt);

            firstStep = false;

            // ---- 6. End of the road: close the open glance (GlanceEnd), so every
            // GlanceStart in the events file has a matching GlanceEnd ----
            if (session.DriveFinished && TryCloseGlance(out GazeZoneType z, out double d))
                Rec("GlanceEnd", z.ToString(), "duration_s=" + d.ToString("F3", Inv) + ";reason=drive_end");
        }

        /// <summary>
        /// Ends the current glance because the drive ends (end of the road, R, or Play stopped).
        /// Returns false if there is no open glance (nothing logged yet, or already closed).
        /// After this the twin stops updating. Called by the twin itself at the end of the road and
        /// by DriveLogger when the drive is stopped early (the logger then writes the GlanceEnd
        /// line itself, because other objects may already be shutting down).
        /// </summary>
        public bool TryCloseGlance(out GazeZoneType zone, out double duration)
        {
            zone = Zone;
            duration = GlanceDuration;
            if (firstStep || glanceClosed) return false;
            glanceClosed = true;
            return true;
        }

        /// <summary>
        /// Starts the attention model again as at the beginning of a drive: buffer back to
        /// bufferStart, glance timers cleared. Used by the scripted gaze test (Part F) so its
        /// expected values do not depend on what happened before. HazardSeen is NOT reset.
        /// </summary>
        public void ResetAttention()
        {
            if (!firstStep) Rec("GlanceEnd", Zone.ToString(), "duration_s=" + GlanceDuration.ToString("F3", Inv));
            firstStep = true;
            GlanceDuration = 0;
            EyesOffRoad = 0;
            onRoadTime = 0;
            Buffer = Mathf.Clamp(bufferStart, 0f, bufferMax);
            AttentionState s = StateFor(Buffer);
            if (s != State) Rec("AttentionStateChanged", s.ToString(), $"from={State};buffer_s={Buffer.ToString("F3", Inv)};reason=reset");
            State = s;
        }

        private void Rec(string type, string target, string detail = "")
        {
            if (events != null) events.Record(type, target, detail);
        }

        /// <summary>
        /// Part of this step that lies AFTER the delay: the zone has been held for 'heldTime'
        /// (including this step); only the time beyond 'delay' counts, at most dt.
        /// Example: delay 0.1 s, dt 0.02 s: steps 1-5 give 0, step 6 gives 0.02.
        /// </summary>
        public static double Portion(double heldTime, double delay, double dt) =>
            Math.Max(0.0, Math.Min(dt, heldTime - delay));

        private AttentionState StateFor(double buffer)
        {
            if (buffer >= attentiveMin - Eps) return AttentionState.Attentive;
            if (buffer >= distractedBelow - Eps) return AttentionState.Reduced;
            return AttentionState.Distracted;
        }

        /// <summary>Gaze sample -> zone.</summary>
        public GazeZoneType Classify(GazeSample s)
        {
            if (!s.valid || !s.onDisplay) return GazeZoneType.OffScreen;
            if (s.hasDirectZone) return s.directZone;      // a display that is itself a zone (planned, Step 5)
            return ClassifyRay(s.worldRay);
        }

        /// <summary>Gaze point in the DRIVER camera's viewport -> zone (used by the scripted test).</summary>
        public GazeZoneType Classify(Vector2 vp)
        {
            if (vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return GazeZoneType.OffScreen;
            if (driverCamera == null) return GazeZoneType.Road;
            return ClassifyRay(driverCamera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f)));
        }

        /// <summary>Gaze ray (world) -> zone. The ray may come from any camera that shares the driver's eye.</summary>
        public GazeZoneType ClassifyRay(Ray ray)
        {
            if (driverCamera == null) return GazeZoneType.Road;
            // The zone colliders move with the cars. Make sure the physics engine knows their
            // current positions before casting the ray (cheap: only a few colliders moved).
            Physics.SyncTransforms();
            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, gazeMask, QueryTriggerInteraction.Collide))
            {
                GazeZone z = hit.collider.GetComponent<GazeZone>();
                if (z != null) return z.zone;
            }
            // The ray leaves the car through a window and hits no zone: road ahead or not?
            // Measured in the driver camera's frame = the car's frame (the camera has no pitch or yaw
            // relative to the car). With three monitors this stays the CENTRE camera, so the window
            // stays fixed to the car whichever monitor the ray comes from.
            Vector3 local = driverCamera.transform.InverseTransformDirection(ray.direction);
            return InRoadAheadWindow(local) ? GazeZoneType.Road : GazeZoneType.OffRoad;
        }

        /// <summary>
        /// True if a direction in the driver camera's own frame (x right, y up, z forward) is inside
        /// the road-ahead window. The window is a rectangle on the image plane: |x/z| up to
        /// tan(half width), y/z from -tan(down) to +tan(up). With the current camera that is a
        /// rectangle on the screen (GazeDebugView draws it when the zones are shown, Z key).
        /// </summary>
        public bool InRoadAheadWindow(Vector3 d)
        {
            if (d.z <= 0f) return false;
            float x = d.x / d.z, y = d.y / d.z;
            return Mathf.Abs(x) <= Mathf.Tan(roadAheadHalfWidthDeg * Mathf.Deg2Rad)
                && y >= -Mathf.Tan(roadAheadDownDeg * Mathf.Deg2Rad)
                && y <= Mathf.Tan(roadAheadUpDeg * Mathf.Deg2Rad);
        }

        /// <summary>GazeViewport (driver camera) and azimuth / elevation from this step's gaze ray.</summary>
        private void UpdateGazeDirection()
        {
            if (!GazeValid || driverCamera == null)
            {
                GazeViewport = new Vector2(float.NaN, float.NaN);
                GazeAzimuthDeg = GazeElevationDeg = float.NaN;
                return;
            }
            Ray ray = Sample.worldRay;
            Vector3 d = driverCamera.transform.InverseTransformDirection(ray.direction);
            GazeAzimuthDeg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            GazeElevationDeg = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            // Viewport of the driver camera: a point 10 m along the ray, projected. Behind the camera
            // (impossible with the current view) = far outside the screen.
            Vector3 v = driverCamera.WorldToViewportPoint(ray.origin + ray.direction * 10f);
            GazeViewport = v.z > 0f ? new Vector2(v.x, v.y) : new Vector2(-9f, -9f);
        }

        private void UpdateHazardSeen(double now, double dt)
        {
            if (HazardSeen || leadCar == null) return;
            double onset = leadCar.BrakeOnsetTime;
            if (double.IsNaN(onset) || now < onset - Eps) return;

            if (Zone != GazeZoneType.LeadCar)
            {
                leadDwellStart = double.NaN;     // interrupted: start again on the next look
                return;
            }
            if (double.IsNaN(leadDwellStart))
            {
                double glanceStart = now - GlanceDuration;       // when this look at the lead car began
                leadDwellStart = Math.Max(glanceStart, onset);   // count only from the onset
            }
            if (now - leadDwellStart >= hazardDwellMin - Eps)
            {
                HazardSeen = true;
                HazardFirstFixationTime = leadDwellStart;
                HazardFixationSinceOnset = leadDwellStart - onset;
                bool alreadyOnLead = now - GlanceDuration < onset - Eps;
                Rec("HazardFirstFixation", leadCar.hazardId,
                    $"since_onset_s={HazardFixationSinceOnset.ToString("F3", Inv)};" +
                    $"fixation_start_s={leadDwellStart.ToString("F3", Inv)};" +
                    $"confirmed_at_s={now.ToString("F3", Inv)};" +
                    $"looking_at_lead_at_onset={(alreadyOnLead ? "True" : "False")}");
            }
        }
    }
}

// VehicleTwin.cs
// The VEHICLE part of the driver-vehicle digital twin (the driver part, with gaze
// and the attention buffer, comes in Step 3).
// Updated every physics step (50 Hz). Everything else (overlay, logger, later the
// warning logic) READS these values instead of computing its own, so all parts
// of the system agree on one set of numbers.
//
// Definitions (straight road along +z, so "along the road" = z):
//   Longitudinal gap = lead rear bumper z - ego front bumper z, ALWAYS computed,
//                      any lane (used only to trigger the hazard).               [m]
//   Lead relevant    = the lead car is AHEAD of the ego car AND the two cars overlap
//                      sideways (same lane). Only then can they collide.
//   Gap              = longitudinal gap, but ONLY when the lead is relevant.      [m]
//   Closing speed    = ego speed - lead speed, only when relevant. > 0 = catching up. [m/s]
//   TTC              = Gap / Closing speed, only when relevant AND closing.        [s]
//   Time headway     = Gap / ego speed, only when relevant.                        [s]
//   When a value is not defined it is NaN here and an EMPTY cell in the CSV
//   (never negative, never a fake large number).
//
// [DefaultExecutionOrder(100)] = runs AFTER the cars in each physics step.

using AdaptiveADAS.Scenario;
using AdaptiveADAS.Vehicle;
using UnityEngine;

namespace AdaptiveADAS.Twin
{
    [DefaultExecutionOrder(100)]
    public class VehicleTwin : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public EgoCarController egoCar;
        [Tooltip("Body collider of the ego car (gives its length and width).")]
        public BoxCollider egoBodyCollider;
        public LeadCarController leadCar;
        [Tooltip("Body collider of the lead car.")]
        public BoxCollider leadBodyCollider;
        public RoadLayout road;

        [Header("Settings")]
        [Tooltip("Closing speeds below this (m/s) count as 'not closing' (avoids huge TTC values from noise). Our choice.")]
        public float minClosingSpeed = 0.05f;

        // ---- Ego vehicle state ----
        public float EgoSpeed { get; private set; }        // m/s along the road
        public float EgoAccel { get; private set; }        // m/s^2 (from speed change per step)
        public float EgoX { get; private set; }
        public float EgoZ { get; private set; }
        public float EgoYawDeg { get; private set; }       // heading relative to the road, + = right
        public int LaneIndex { get; private set; }         // 0 left lane, 1 right lane, -1 not on our lanes
        public float LaneOffset { get; private set; }      // m from the centre of the current lane, + = right

        // ---- Lead vehicle state ----
        public float LeadZ { get; private set; }
        public float LeadSpeed { get; private set; }

        // ---- Relation ego <-> lead ----
        public float LongitudinalGap { get; private set; }
        public bool LateralOverlap { get; private set; }
        public bool LeadRelevant { get; private set; }
        public float Gap { get; private set; } = float.NaN;
        public float ClosingSpeed { get; private set; } = float.NaN;
        public float TTC { get; private set; } = float.NaN;
        public float TimeHeadway { get; private set; } = float.NaN;
        /// <summary>Smallest TTC seen in this drive (NaN if none yet).</summary>
        public float MinTTC { get; private set; } = float.NaN;

        // Bumper positions in each car's own (local) coordinates, measured once at start.
        private Vector3 egoFrontLocal, leadRearLocal;
        private float egoHalfWidth, leadHalfWidth;
        private float previousEgoSpeed;
        private bool hasPrevious;

        private void Start()
        {
            // At start both cars point along +z, so the world-space bounds of the
            // body colliders give the true length and width.
            Bounds eb = egoBodyCollider.bounds;
            Bounds lb = leadBodyCollider.bounds;
            egoFrontLocal = egoCar.transform.InverseTransformPoint(new Vector3(eb.center.x, eb.center.y, eb.max.z));
            leadRearLocal = leadCar.transform.InverseTransformPoint(new Vector3(lb.center.x, lb.center.y, lb.min.z));
            egoHalfWidth = eb.extents.x;
            leadHalfWidth = lb.extents.x;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Rigidbody egoRb = egoCar.Body;
            Rigidbody leadRb = leadCar.Body;

            // ---- Ego ----
            EgoX = egoRb.position.x;
            EgoZ = egoRb.position.z;
            EgoSpeed = egoRb.linearVelocity.z;
            EgoAccel = hasPrevious ? (EgoSpeed - previousEgoSpeed) / dt : 0f;
            previousEgoSpeed = EgoSpeed;
            hasPrevious = true;

            Vector3 fwd = egoRb.rotation * Vector3.forward;
            EgoYawDeg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

            if (road != null)
            {
                LaneIndex = road.LaneIndexAt(EgoX);
                LaneOffset = LaneIndex >= 0 ? EgoX - road.LaneCentreX(LaneIndex) : float.NaN;
            }

            // ---- Lead ----
            LeadZ = leadRb.position.z;
            LeadSpeed = leadCar.Speed;

            // ---- Is the lead car a possible collision partner? ----
            float egoFrontZ = (egoRb.position + egoRb.rotation * egoFrontLocal).z;
            float leadRearZ = (leadRb.position + leadRb.rotation * leadRearLocal).z;
            LongitudinalGap = leadRearZ - egoFrontZ;

            float lateralDistance = Mathf.Abs(EgoX - leadRb.position.x);
            LateralOverlap = lateralDistance < egoHalfWidth + leadHalfWidth;
            bool leadAhead = leadRb.position.z > egoRb.position.z;   // compare car centres
            LeadRelevant = LateralOverlap && leadAhead;

            if (!LeadRelevant)
            {
                Gap = ClosingSpeed = TTC = TimeHeadway = float.NaN;
                return;
            }

            // ---- Gap, closing speed, TTC, headway (lead ahead, same lane) ----
            Gap = LongitudinalGap;                     // can be <= 0 only while the cars touch
            ClosingSpeed = EgoSpeed - LeadSpeed;

            if (Gap <= 0f) TTC = 0f;                   // bumpers touching: collision
            else if (ClosingSpeed > minClosingSpeed) TTC = Gap / ClosingSpeed;
            else TTC = float.NaN;                      // not catching up: no TTC

            TimeHeadway = EgoSpeed > 0.5f ? Mathf.Max(Gap, 0f) / EgoSpeed : float.NaN;

            if (!float.IsNaN(TTC) && (float.IsNaN(MinTTC) || TTC < MinTTC)) MinTTC = TTC;
        }
    }
}

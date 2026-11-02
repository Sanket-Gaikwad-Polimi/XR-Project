// EgoCarController.cs
// The driver's car, driven by Unity's physics: a Rigidbody (the car body, with mass)
// on four WheelColliders (each wheel has suspension and tyre friction).
//
// What this script does every physics step (FixedUpdate, 50 Hz):
//   1. Gear      -> Drive or Reverse (test mode: hold the brake while stopped).
//   2. Steering  -> front wheel angle, limited by speed (see below).
//   3. Engine    -> drive torque on the front wheels (front-wheel drive). Force is
//                   limited by a maximum force (traction) and by engine power
//                   (F = P / v), so the car accelerates less at high speed.
//   4. Brakes    -> brake torque on all four wheels, split front/rear.
//   5. Air drag  -> F = 0.5 * rho * Cd * A * v^2, applied to the body.
//   Tyre forces (grip, slip) are computed by Unity's WheelCollider tyre model.
//
// SPEED-SENSITIVE STEERING (why the first version felt twitchy)
//   The first version allowed ~10 degrees of wheel angle at 90 km/h. A car turning
//   with wheel angle delta at speed v has lateral acceleration a ~ v^2 * tan(delta) / L
//   (L = wheelbase). 10 deg at 25 m/s asks for ~40 m/s^2 = 4 g, far beyond tyre grip
//   (~1 g), so the tyres slid and the car spun. Our first logs show this: 3-6 deg at
//   motorway speed and yaw angles up to 157 deg.
//   Now the maximum wheel angle is chosen so that FULL steering input gives at most
//   maxLateralAccel:   delta_max(v) = atan(L * a_max / v^2), capped at maxSteerAngle.
//   At 90 km/h this is ~1.2 deg; at 30 km/h ~11 deg; when parking 30 deg.
//   This is NOT a driving assist: it does not steer for the driver, it only scales
//   how much wheel angle the stick/keys can ask for (a real steering wheel does the
//   same job through its large turning range).
//
// There is no gearbox model (single gear) and no lane-keeping assist.
// All numbers are PLACEHOLDERS chosen by us; see Docs/BaseScene_Parameters.md.

using System.Collections.Generic;
using System.Globalization;
using AdaptiveADAS.Logging;
using AdaptiveADAS.Scenario;
using UnityEngine;

namespace AdaptiveADAS.Vehicle
{
    [RequireComponent(typeof(Rigidbody))]
    public class EgoCarController : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public DriverInput driverInput;
        [Tooltip("Optional test helpers (cruise control). May be empty.")]
        public TestAssists testAssists;
        public DriveEvents events;
        public WheelCollider wheelFL, wheelFR, wheelRL, wheelRR;
        [Tooltip("Visible wheel objects (children of this car). Moved to match the WheelColliders.")]
        public Transform visualFL, visualFR, visualRL, visualRR;

        [Header("Body")]
        [Tooltip("Vehicle mass in kg (our choice: typical mid-size car).")]
        public float massKg = 1500f;
        [Tooltip("Centre of mass in car coordinates. Low, so the greybox car does not roll over.")]
        public Vector3 centreOfMass = new Vector3(0f, 0.45f, 0.1f);
        [Tooltip("Speed at the start of the drive (km/h). Same as the lead car so the drive starts in car-following.")]
        public float initialSpeedKmh = 90f;

        [Header("Engine (front-wheel drive)")]
        [Tooltip("Maximum drive force at the wheels (N). 4500 N / 1500 kg = 3 m/s^2 maximum acceleration.")]
        public float maxDriveForce = 4500f;
        [Tooltip("Engine power (W). Limits force at speed: F = P / v. 90 kW = typical mid-size car.")]
        public float enginePower = 90000f;
        [Tooltip("No drive force above this speed (km/h).")]
        public float maxSpeedKmh = 160f;
        [Tooltip("Braking force from the engine when the throttle is released (N).")]
        public float engineBrakeForce = 600f;

        [Header("Brakes")]
        [Tooltip("BRAKE-ONLY deceleration at full pedal (m/s^2), if the tyres can deliver it. ~0.8 g, TO VERIFY. " +
                 "The car's total deceleration is a bit higher because engine braking (~0.4 m/s^2) and air drag " +
                 "(~0.17 m/s^2 at 90 km/h) add to it: about 8.5 m/s^2 at 90 km/h.")]
        public float maxBrakeDecel = 8f;
        [Tooltip("Brake build-up time (s): time for the brake force to rise from 0 to the pedal value " +
                 "(models brake system pressure build-up). 0 = instant (behaviour so far). " +
                 "Releasing the brake is always instant. Value to be set after checking the literature.")]
        public float brakeBuildUpTime = 0f;
        [Tooltip("Share of brake force on the front axle.")]
        [Range(0f, 1f)] public float frontBrakeBias = 0.65f;
        [Tooltip("Brake torque per wheel used to hold the car still when stopped (N m).")]
        public float holdBrakeTorque = 500f;

        [Header("Reverse (test mode only)")]
        [Tooltip("Allow reverse: when stopped, HOLD the brake for reverseEngageDelay s -> gear R. " +
                 "Then RELEASE the brake and press it again to drive backwards; the throttle pedal brakes. " +
                 "Disabled in study mode: participants who stop behind the hazard keep the brake pressed, " +
                 "and must not start rolling backwards.")]
        public bool allowReverse = true;
        public float reverseEngageDelay = 1.0f;
        public float maxReverseSpeedKmh = 15f;
        public float maxReverseForce = 2500f;

        [Header("Steering")]
        [Tooltip("Largest front wheel angle (degrees), reached only at low speed.")]
        public float maxSteerAngle = 30f;
        [Tooltip("Lateral acceleration (m/s^2) that FULL steering input produces at speed. " +
                 "5 m/s^2 ~ 0.5 g: enough for a quick evasive move, below the tyre limit (~1 g). Our choice, to tune.")]
        public float maxLateralAccel = 5f;

        [Header("Tyres (Unity WheelCollider friction)")]
        [Tooltip("Multiplier on longitudinal grip (1 = Unity default curve).")]
        public float forwardGrip = 1f;
        [Tooltip("Multiplier on sideways grip (1 = Unity default curve).")]
        public float sidewaysGrip = 1f;

        [Header("Air drag")]
        [Tooltip("Drag coefficient Cd (typical car about 0.3).")]
        public float dragCoefficient = 0.3f;
        [Tooltip("Frontal area (m^2).")]
        public float frontalArea = 2.2f;
        private const float AirDensity = 1.225f; // kg/m^3 at sea level, 15 C

        [Header("Collisions")]
        [Tooltip("Contacts with the same kind of object within this time (s) count as ONE collision event.")]
        public float collisionDebounceSeconds = 1.5f;

        // ---- Outputs (read by the twin, the overlay and the logger) ----
        public Rigidbody Body { get; private set; }
        /// <summary>Speed along the car's own forward direction (m/s); negative when reversing.</summary>
        public float ForwardSpeed { get; private set; }
        public float SteerAngleDeg { get; private set; }
        /// <summary>Largest wheel angle allowed at the current speed (degrees).</summary>
        public float CurrentMaxSteerDeg { get; private set; }
        /// <summary>Brake command actually applied after the optional build-up time (0..1).</summary>
        public float AppliedBrake { get; private set; }
        /// <summary>Throttle actually used: driver pedal or cruise control, whichever is larger.</summary>
        public float AppliedThrottle { get; private set; }
        public bool InReverse { get; private set; }
        public string Gear => InReverse ? "R" : "D";
        public int LeadCollisionEvents { get; private set; }
        public int OtherCollisionEvents { get; private set; }
        public string LastCollisionWith { get; private set; } = "";

        private float wheelBase;
        private float brakeHoldTimer;
        private bool reverseReleasedOnce;   // brake released since R was engaged?
        private readonly Dictionary<string, double> lastContactTime = new Dictionary<string, double>();

        private void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.mass = massKg;
            Body.centerOfMass = centreOfMass;
            Body.interpolation = RigidbodyInterpolation.Interpolate;               // smooth camera
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // no tunnelling at speed

            // Distance between front and rear axle, measured from the wheel positions.
            wheelBase = Mathf.Abs(wheelFL.transform.localPosition.z - wheelRL.transform.localPosition.z);

            foreach (WheelCollider w in new[] { wheelFL, wheelFR, wheelRL, wheelRR })
            {
                WheelFrictionCurve f = w.forwardFriction;  f.stiffness = forwardGrip;  w.forwardFriction = f;
                WheelFrictionCurve s = w.sidewaysFriction; s.stiffness = sidewaysGrip; w.sidewaysFriction = s;
            }

            // More physics sub-steps for the wheels: Unity's recommended way to make
            // WheelColliders stable at speed: (speed threshold m/s, steps below, steps above).
            // One call configures all WheelColliders on this Rigidbody.
            wheelFL.ConfigureVehicleSubsteps(5f, 12, 15);
        }

        private void Start()
        {
            // Start already moving. The wheels start at 0 rpm, so for a fraction of a
            // second the tyres slip a little until they spin up.
            Body.linearVelocity = transform.forward * (initialSpeedKmh / 3.6f);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            float driverThrottle = driverInput != null ? driverInput.Throttle : 0f;
            float brake = driverInput != null ? driverInput.Brake : 0f;
            float steer = driverInput != null ? driverInput.Steer : 0f;
            float cruise = testAssists != null ? testAssists.CruiseThrottle : 0f;
            AppliedThrottle = Mathf.Max(driverThrottle, cruise);

            ForwardSpeed = Vector3.Dot(Body.linearVelocity, transform.forward);
            float speed = Mathf.Abs(ForwardSpeed);
            float wheelRadius = wheelFL.radius;

            // ---- 1. Gear: Drive <-> Reverse ----
            UpdateGear(AppliedThrottle, brake, speed, dt);
            // In reverse the pedals swap roles: brake pedal = go backwards, throttle pedal = brake.
            // The brake must be released once after engaging R before it drives backwards.
            if (InReverse && brake < 0.05f) reverseReleasedOnce = true;
            float drivePedal = InReverse ? (reverseReleasedOnce ? brake : 0f) : AppliedThrottle;
            float brakePedal = InReverse ? AppliedThrottle : brake;

            // ---- 2. Steering: wheel angle limited by speed ----
            float fromLateralLimit = speed > 0.5f
                ? Mathf.Atan(wheelBase * maxLateralAccel / (speed * speed)) * Mathf.Rad2Deg
                : maxSteerAngle;
            CurrentMaxSteerDeg = Mathf.Min(maxSteerAngle, fromLateralLimit);
            SteerAngleDeg = steer * CurrentMaxSteerDeg;
            wheelFL.steerAngle = SteerAngleDeg;
            wheelFR.steerAngle = SteerAngleDeg;

            // ---- 3. Engine: force limited by traction and by power ----
            float driveForce = 0f;
            float speedLimit = (InReverse ? maxReverseSpeedKmh : maxSpeedKmh) / 3.6f;
            if (speed < speedLimit)
            {
                float maxForce = InReverse ? maxReverseForce : maxDriveForce;
                float powerLimitedForce = enginePower / Mathf.Max(speed, 1f); // avoid divide by 0
                driveForce = drivePedal * Mathf.Min(maxForce, powerLimitedForce);
            }
            float direction = InReverse ? -1f : 1f;
            float driveTorquePerWheel = direction * driveForce * wheelRadius / 2f; // two driven wheels
            wheelFL.motorTorque = driveTorquePerWheel;
            wheelFR.motorTorque = driveTorquePerWheel;
            wheelRL.motorTorque = 0f;
            wheelRR.motorTorque = 0f;

            // ---- 4. Brakes: pedal -> (optional build-up) -> force -> torque per wheel ----
            // With brakeBuildUpTime > 0 the applied brake rises towards the pedal at
            // 1/brakeBuildUpTime per second (e.g. 0.2 s -> full brake 0.2 s after a full press).
            if (brakeBuildUpTime > 0f && brakePedal > AppliedBrake)
                AppliedBrake = Mathf.MoveTowards(AppliedBrake, brakePedal, dt / brakeBuildUpTime);
            else
                AppliedBrake = brakePedal;
            float brakeForce = AppliedBrake * massKg * maxBrakeDecel;
            float frontTorque = brakeForce * frontBrakeBias / 2f * wheelRadius;
            float rearTorque = brakeForce * (1f - frontBrakeBias) / 2f * wheelRadius;

            // Engine braking on the driven (front) wheels when the pedal is released.
            if (drivePedal < 0.01f && speed > 1f)
                frontTorque += engineBrakeForce / 2f * wheelRadius;

            // Hold the car still when it has stopped and nobody is asking it to move.
            if (drivePedal < 0.01f && speed < 0.5f)
            {
                frontTorque = Mathf.Max(frontTorque, holdBrakeTorque);
                rearTorque = Mathf.Max(rearTorque, holdBrakeTorque);
            }

            wheelFL.brakeTorque = frontTorque;
            wheelFR.brakeTorque = frontTorque;
            wheelRL.brakeTorque = rearTorque;
            wheelRR.brakeTorque = rearTorque;

            // ---- 5. Air drag on the body ----
            Vector3 v = Body.linearVelocity;
            float dragForce = 0.5f * AirDensity * dragCoefficient * frontalArea * v.sqrMagnitude;
            if (v.sqrMagnitude > 0.01f)
                Body.AddForce(-v.normalized * dragForce);

            // ---- Visible wheels follow the WheelColliders ----
            UpdateWheelVisual(wheelFL, visualFL);
            UpdateWheelVisual(wheelFR, visualFR);
            UpdateWheelVisual(wheelRL, visualRL);
            UpdateWheelVisual(wheelRR, visualRR);
        }

        /// <summary>
        /// Reverse logic (simple, like many driving games):
        ///  - In Drive: if the car is stopped and the brake is held for reverseEngageDelay -> Reverse
        ///    (then release and press the brake again to move backwards).
        ///  - In Reverse: if the car is (almost) stopped and the throttle is pressed -> Drive.
        /// </summary>
        private void UpdateGear(float throttle, float brake, float speed, float dt)
        {
            if (!InReverse)
            {
                bool reverseAllowed = allowReverse && (testAssists == null || testAssists.HelpersAllowed);
                bool holdingBrakeWhileStopped = reverseAllowed && speed < 0.3f && brake > 0.5f && throttle < 0.05f;
                brakeHoldTimer = holdingBrakeWhileStopped ? brakeHoldTimer + dt : 0f;
                if (brakeHoldTimer >= reverseEngageDelay)
                {
                    InReverse = true;
                    reverseReleasedOnce = false;
                    brakeHoldTimer = 0f;
                    if (events != null) events.Record("ReverseOn");
                }
            }
            else if (throttle > 0.05f && speed < 0.5f)
            {
                InReverse = false;
                if (events != null) events.Record("ReverseOff");
            }
        }

        /// <summary>Instantly moves the car (used by the test-only reset).</summary>
        public void Teleport(Vector3 position, Quaternion rotation, float speedMps)
        {
            Body.position = position;
            Body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            Body.linearVelocity = rotation * Vector3.forward * speedMps;
            Body.angularVelocity = Vector3.zero;
            InReverse = false;
            brakeHoldTimer = 0f;
        }

        /// <summary>
        /// Copies the wheel's position (suspension travel) and rotation (spin + steer)
        /// to the visible wheel. We convert to car-local coordinates because the
        /// visible wheels are children of the car, which Unity interpolates smoothly.
        /// </summary>
        private void UpdateWheelVisual(WheelCollider wheel, Transform visual)
        {
            if (wheel == null || visual == null) return;
            wheel.GetWorldPose(out Vector3 worldPos, out Quaternion worldRot);
            Quaternion inverseBodyRot = Quaternion.Inverse(Body.rotation);
            visual.localPosition = inverseBodyRot * (worldPos - Body.position);
            visual.localRotation = inverseBodyRot * worldRot;
        }

        // ---- Collisions as EVENTS ----
        // Unity calls OnCollisionEnter at first contact and OnCollisionStay while touching.
        // Scraping along a barrier or bouncing twice off the same car produces many contacts,
        // so a new EVENT is only counted if there was no contact with the same kind of object
        // in the last collisionDebounceSeconds.
        private void OnCollisionEnter(Collision c) => HandleContact(c, isFirstContact: true);
        private void OnCollisionStay(Collision c) => HandleContact(c, isFirstContact: false);

        private void HandleContact(Collision c, bool isFirstContact)
        {
            CollisionLabel labelComponent = c.collider.GetComponentInParent<CollisionLabel>();
            string label = labelComponent != null ? labelComponent.label : "Other";

            double now = Time.fixedTimeAsDouble;
            bool isNewEvent = isFirstContact &&
                (!lastContactTime.TryGetValue(label, out double last) || now - last > collisionDebounceSeconds);
            lastContactTime[label] = now;
            if (!isNewEvent) return;

            if (label == "Lead") LeadCollisionEvents++;
            else OtherCollisionEvents++;
            LastCollisionWith = label;

            float relKmh = c.relativeVelocity.magnitude * 3.6f;
            if (events != null)
                events.Record("Collision", label, "rel_speed_kmh=" + relKmh.ToString("F1", CultureInfo.InvariantCulture) + ";object=" + c.gameObject.name);
        }
    }
}

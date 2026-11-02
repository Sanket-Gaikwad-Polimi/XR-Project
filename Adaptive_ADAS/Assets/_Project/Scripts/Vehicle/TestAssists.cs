// TestAssists.cs
// TEST-ONLY helpers for developing at home. All of them are ignored when
// DriveSession.studyMode is ON, so they can never affect participant data.
//
//  1. Cruise control (optional): holds the speed you had when you switched it on.
//     It only uses the THROTTLE (never brakes, never steers). Pressing the brake
//     pedal switches it off, like in a real car. If you press the throttle harder
//     than the cruise control does, your input wins.
//       Keyboard E  /  Gamepad X (Xbox) or Square (PlayStation)
//
//  2. Reset: puts the car back in the centre of its lane (left or right lane of our
//     carriageway, whichever is nearer), pointing forward, at a safe speed, and at
//     least resetMinGapToLead metres behind the lead car if it is close.
//       Keyboard Backspace  /  Gamepad Y (Xbox) or Triangle (PlayStation)
//
// Both are logged as events (CruiseOn, CruiseOff, Reset).
// Reverse gear (EgoCarController) is also test-only and uses HelpersAllowed.
// There is NO lane-keeping or steering help of any kind.
//
// [DefaultExecutionOrder(-40)]: runs after DriverInput (-50), before the car (0).

using System.Globalization;
using AdaptiveADAS.Experiment;
using AdaptiveADAS.Logging;
using AdaptiveADAS.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveADAS.Vehicle
{
    [DefaultExecutionOrder(-40)]
    public class TestAssists : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public DriveEvents events;
        public EgoCarController egoCar;
        public DriverInput driverInput;
        public LeadCarController leadCar;
        public RoadLayout road;

        [Header("Cruise control (test only)")]
        public bool cruiseControlAvailable = true;
        [Tooltip("Cruise control cannot be set below this speed (km/h).")]
        public float minCruiseSpeedKmh = 30f;
        [Tooltip("Throttle per m/s of speed error (a simple proportional controller). Our choice.")]
        public float cruiseGain = 0.5f;

        [Header("Reset (test only)")]
        public bool resetAvailable = true;
        [Tooltip("Speed after a reset (km/h).")]
        public float resetSpeedKmh = 50f;
        [Tooltip("After a reset the car is at least this far (m) behind the lead car if it is in the same lane.")]
        public float resetMinGapToLead = 30f;

        // ---- Outputs ----
        public bool CruiseOn { get; private set; }
        public float CruiseSetSpeedKmh { get; private set; }
        /// <summary>Throttle requested by the cruise control (0 when off).</summary>
        public float CruiseThrottle { get; private set; }

        /// <summary>False in study mode. Also used by EgoCarController to allow reverse.</summary>
        public bool HelpersAllowed => session == null || !session.studyMode;
        private bool cruiseTogglePressed, resetPressed;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private void Update()
        {
            // Button presses are detected per frame (Update) and handled in the next physics step.
            Keyboard kb = Keyboard.current;
            Gamepad gp = Gamepad.current;
            if ((kb != null && kb.eKey.wasPressedThisFrame) || (gp != null && gp.buttonWest.wasPressedThisFrame))
                cruiseTogglePressed = true;
            if ((kb != null && kb.backspaceKey.wasPressedThisFrame) || (gp != null && gp.buttonNorth.wasPressedThisFrame))
                resetPressed = true;
        }

        private void FixedUpdate()
        {
            if (!HelpersAllowed)
            {
                if (CruiseOn) SetCruise(false, "study_mode");
                cruiseTogglePressed = resetPressed = false;
                return;
            }

            if (resetPressed) { resetPressed = false; if (resetAvailable) DoReset(); }

            // ---- Cruise control ----
            if (cruiseTogglePressed)
            {
                cruiseTogglePressed = false;
                if (CruiseOn) SetCruise(false, "button");
                else if (cruiseControlAvailable && !egoCar.InReverse && egoCar.ForwardSpeed * 3.6f >= minCruiseSpeedKmh)
                {
                    CruiseSetSpeedKmh = Mathf.Round(egoCar.ForwardSpeed * 3.6f);
                    SetCruise(true, "button");
                }
            }

            if (CruiseOn && driverInput.Brake > 0.05f) SetCruise(false, "brake");

            CruiseThrottle = CruiseOn
                ? Mathf.Clamp01(cruiseGain * (CruiseSetSpeedKmh / 3.6f - egoCar.ForwardSpeed))
                : 0f;
        }

        private void SetCruise(bool on, string reason)
        {
            CruiseOn = on;
            if (!on) CruiseThrottle = 0f;
            if (events != null)
                events.Record(on ? "CruiseOn" : "CruiseOff", "",
                    "set_kmh=" + CruiseSetSpeedKmh.ToString("F0", Inv) + ";reason=" + reason);
        }

        private void DoReset()
        {
            Vector3 p = egoCar.Body.position;

            // Nearest of OUR lanes (0 = left, 1 = right).
            int lane = Mathf.Clamp(Mathf.RoundToInt((p.x - road.CarriagewayInnerX) / road.laneWidth - 0.5f),
                                   0, road.lanesPerDirection - 1);
            float x = road.LaneCentreX(lane);
            float z = p.z;

            // Keep a safe distance behind the lead car if it is in that lane and close.
            if (leadCar != null)
            {
                Vector3 lp = leadCar.Body.position;
                bool sameLane = Mathf.Abs(lp.x - x) < road.laneWidth * 0.5f;
                float carLength = 4.5f; // greybox car length (BaseSceneBuilder)
                if (sameLane && z > lp.z - carLength - resetMinGapToLead && z < lp.z + carLength)
                    z = lp.z - carLength - resetMinGapToLead;
            }

            var newPos = new Vector3(x, 0.1f, z);
            if (CruiseOn) SetCruise(false, "reset");
            egoCar.Teleport(newPos, Quaternion.identity, resetSpeedKmh / 3.6f);

            if (events != null)
                events.Record("Reset", "",
                    "from_x=" + p.x.ToString("F2", Inv) + ";from_z=" + p.z.ToString("F1", Inv) +
                    ";to_lane=" + lane + ";to_z=" + z.ToString("F1", Inv));
        }
    }
}

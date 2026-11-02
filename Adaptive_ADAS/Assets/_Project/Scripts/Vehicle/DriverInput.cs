// DriverInput.cs
// Reads the driver's pedals and steering from GAMEPAD (main home-testing device)
// or keyboard, using the new Input System, and outputs three clean values:
//   Throttle 0..1, Brake 0..1, Steer -1 (left) .. +1 (right)
//
// PEDALS
//   Gamepad triggers are analog and used DIRECTLY (no smoothing), like real pedals,
//   so they add no artificial delay to brake reaction times.
//   Keyboard keys are on/off, so the keyboard pedal RAMPS from 0 to 1 over a short time.
//
// STEERING (all devices)
//   A real steering wheel cannot jump from centre to full lock instantly. So the
//   steering value moves towards the requested value at a limited rate
//   (steerRate), and returns to centre a bit faster when released
//   (steerReturnRate) = self-centering.
//   Gamepad stick: a response curve (exponent > 1) gives finer control near the
//   centre without reducing full deflection.
//
// No lane-keeping or any automatic steering help is applied here or anywhere else:
// it would confound the study.
//
// Bindings (our choice):
//   Gamepad:  Right trigger = throttle, Left trigger = brake, Left stick X = steer
//   Keyboard: W / Up = throttle, S / Down = brake, A-D / Left-Right = steer
//
// GAMEPAD DETECTION
//   Unity's Input System recognises Xbox (XInput) controllers, PlayStation 4/5 and
//   Switch Pro controllers as "Gamepad". Generic USB/"DirectInput" pads appear only
//   as "Joystick" and are NOT used here. Connect/disconnect is logged as an event
//   (GamepadConnected / GamepadDisconnected / JoystickConnected).

using System.Linq;
using AdaptiveADAS.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveADAS.Vehicle
{
    // Runs before the car (order 0) so the car uses this step's input.
    [DefaultExecutionOrder(-50)]
    public class DriverInput : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        [Tooltip("Used to log gamepad connect / disconnect events.")]
        public DriveEvents events;

        [Header("Steering (all devices)")]
        [Tooltip("Max change of the steering value per second (1 = centre to full in 1 s). " +
                 "2.5 = full in 0.4 s: a quick but human steering-wheel movement. Our choice.")]
        public float steerRate = 2.5f;
        [Tooltip("Speed of return to centre when the input is released (self-centering), per second. Our choice.")]
        public float steerReturnRate = 3.5f;

        [Header("Gamepad")]
        [Tooltip("Stick values below this are treated as 0 (removes stick drift). Our choice.")]
        [Range(0f, 0.3f)] public float stickDeadzone = 0.08f;
        [Tooltip("Stick response curve: output = input^exponent. 1 = linear, 1.5 = finer control near centre. Our choice.")]
        [Range(1f, 3f)] public float stickResponseExponent = 1.5f;
        [Tooltip("Trigger values below this are treated as 0. Our choice.")]
        [Range(0f, 0.2f)] public float triggerDeadzone = 0.03f;

        [Header("Keyboard pedals (ramp rates per second)")]
        [Tooltip("How fast a keyboard pedal goes from 0 to 1. 3 = full pedal in 0.33 s. Our choice.")]
        public float keyPedalRiseRate = 3f;
        [Tooltip("How fast a keyboard pedal returns to 0 when released. 5 = 0.2 s. Our choice.")]
        public float keyPedalFallRate = 5f;

        // ---- Outputs read by the car, the overlay and the logger ----
        public float Throttle { get; private set; }
        public float Brake { get; private set; }
        public float Steer { get; private set; }
        /// <summary>"Gamepad", "Keyboard" or "None": which device gave the last non-zero input.</summary>
        public string ActiveDevice { get; private set; } = "None";
        /// <summary>Name of the connected gamepad, or "" if none.</summary>
        public string GamepadName => Gamepad.current != null ? Gamepad.current.displayName : "";
        /// <summary>All input devices Unity sees right now, e.g. "Keyboard; Mouse; Xbox Controller".</summary>
        public static string AllDeviceNames =>
            string.Join("; ", InputSystem.devices.Select(d => $"{d.displayName} [{d.GetType().Name}]"));

        // Raw values (before keyboard ramp / steering rate limit), logged for analysis.
        public float RawThrottle { get; private set; }
        public float RawBrake { get; private set; }
        public float RawSteer { get; private set; }

        private InputAction keyThrottle, keyBrake, keySteer;
        private InputAction padThrottle, padBrake, padSteer;
        private float kThrottle, kBrake;   // keyboard pedals after ramping

        private void Awake()
        {
            // Actions are defined in code (instead of an .inputactions asset)
            // so every binding is visible in this one file.
            keyThrottle = new InputAction("KeyThrottle", InputActionType.Button);
            keyThrottle.AddBinding("<Keyboard>/w");
            keyThrottle.AddBinding("<Keyboard>/upArrow");

            keyBrake = new InputAction("KeyBrake", InputActionType.Button);
            keyBrake.AddBinding("<Keyboard>/s");
            keyBrake.AddBinding("<Keyboard>/downArrow");

            keySteer = new InputAction("KeySteer", InputActionType.Value);
            keySteer.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");
            keySteer.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");

            padThrottle = new InputAction("PadThrottle", InputActionType.Value, "<Gamepad>/rightTrigger");
            padBrake = new InputAction("PadBrake", InputActionType.Value, "<Gamepad>/leftTrigger");
            padSteer = new InputAction("PadSteer", InputActionType.Value, "<Gamepad>/leftStick/x");
        }

        private void OnEnable()
        {
            keyThrottle.Enable(); keyBrake.Enable(); keySteer.Enable();
            padThrottle.Enable(); padBrake.Enable(); padSteer.Enable();
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            keyThrottle.Disable(); keyBrake.Disable(); keySteer.Disable();
            padThrottle.Disable(); padBrake.Disable(); padSteer.Disable();
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        private void Start()
        {
            // State at the start of the drive (the meta file also lists all devices).
            if (events != null)
            {
                if (Gamepad.current != null) events.Record("GamepadConnected", Gamepad.current.displayName, "at_start");
                else events.Record("GamepadMissing", "", "devices=" + AllDeviceNames);
            }
            Debug.Log($"[DriverInput] Input devices: {AllDeviceNames}");
        }

        /// <summary>Called by Unity whenever a device is plugged in, removed, or reconnects.</summary>
        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            string kind = device is Gamepad ? "Gamepad" : device is Joystick ? "Joystick" : null;
            if (kind == null || events == null) return;   // keyboard/mouse changes are not logged

            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
                events.Record(kind + "Connected", device.displayName);
            else if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
                events.Record(kind + "Disconnected", device.displayName);
        }

        private void OnDestroy()
        {
            keyThrottle.Dispose(); keyBrake.Dispose(); keySteer.Dispose();
            padThrottle.Dispose(); padBrake.Dispose(); padSteer.Dispose();
        }

        // Read input in FixedUpdate so the values are fresh for every physics step
        // (the car and the logger also run in FixedUpdate).
        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // ---- Gamepad (analog) ----
            float pThrottle = Deadzone(padThrottle.ReadValue<float>(), triggerDeadzone);
            float pBrake = Deadzone(padBrake.ReadValue<float>(), triggerDeadzone);
            float pSteer = Deadzone(padSteer.ReadValue<float>(), stickDeadzone);
            pSteer = Mathf.Sign(pSteer) * Mathf.Pow(Mathf.Abs(pSteer), stickResponseExponent);

            // ---- Keyboard (on/off) ----
            float kThrottleTarget = keyThrottle.IsPressed() ? 1f : 0f;
            float kBrakeTarget = keyBrake.IsPressed() ? 1f : 0f;
            float kSteer = Mathf.Clamp(keySteer.ReadValue<float>(), -1f, 1f);

            kThrottle = Ramp(kThrottle, kThrottleTarget, keyPedalRiseRate, keyPedalFallRate, dt);
            kBrake = Ramp(kBrake, kBrakeTarget, keyPedalRiseRate, keyPedalFallRate, dt);

            // ---- Combine: the larger input wins ----
            RawThrottle = Mathf.Max(kThrottleTarget, pThrottle);
            RawBrake = Mathf.Max(kBrakeTarget, pBrake);
            RawSteer = Mathf.Abs(pSteer) >= Mathf.Abs(kSteer) ? pSteer : kSteer;

            Throttle = Mathf.Max(kThrottle, pThrottle);
            Brake = Mathf.Max(kBrake, pBrake);

            // ---- Steering: rate-limited towards the requested value ----
            // Moving back towards centre (or across it) uses the faster return rate.
            bool returning = Mathf.Abs(RawSteer) < Mathf.Abs(Steer) || Mathf.Sign(RawSteer) != Mathf.Sign(Steer);
            float rate = returning ? steerReturnRate : steerRate;
            Steer = Mathf.MoveTowards(Steer, RawSteer, rate * dt);

            // ---- Which device is in use (shown in the overlay, logged) ----
            if (pThrottle > 0f || pBrake > 0f || pSteer != 0f) ActiveDevice = "Gamepad";
            else if (kThrottleTarget > 0f || kBrakeTarget > 0f || kSteer != 0f) ActiveDevice = "Keyboard";
        }

        /// <summary>Move value towards target, with different speeds up and down.</summary>
        private static float Ramp(float value, float target, float riseRate, float fallRate, float dt)
        {
            float rate = target > value ? riseRate : fallRate;
            return Mathf.MoveTowards(value, target, rate * dt);
        }

        /// <summary>Values smaller than the dead zone become 0; the rest is rescaled to 0..1.</summary>
        private static float Deadzone(float v, float dz)
        {
            float a = Mathf.Abs(v);
            if (a <= dz) return 0f;
            return Mathf.Sign(v) * (a - dz) / (1f - dz);
        }
    }
}

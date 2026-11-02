// DriveSession.cs
// The "clock" of one drive, the study-mode switch, restart and end-of-drive.
//
// Why a separate clock? Unity's Time.time keeps running across scene reloads,
// so after pressing R the time would not start at 0. Here we simply count
// physics steps: driveTime = number of FixedUpdate steps x fixed timestep.
// With Fixed Timestep = 0.02 s (Project Settings > Time) that is exactly 50 Hz.
//
// STUDY MODE: when ticked, all test-only helpers are disabled (cruise control,
// reset key, chase camera). Leave it off while developing at home.
//
// [DefaultExecutionOrder(-100)] makes this FixedUpdate run BEFORE the cars,
// the twin and the logger, so they all see the same time stamp in one step.

using AdaptiveADAS.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace AdaptiveADAS.Experiment
{
    [DefaultExecutionOrder(-100)]
    public class DriveSession : MonoBehaviour
    {
        [Header("Mode")]
        [Tooltip("ON for real participants: disables cruise control, reset and chase camera.")]
        public bool studyMode = false;

        [Header("Driver guidance (no limiter, no automation)")]
        [Tooltip("Speed limit shown on the roadside signs and in the instruction (km/h). Placeholder: equal to the lead car's speed.")]
        public float speedLimitKmh = 90f;
        [Tooltip("Instruction given to the driver for this drive (shown in the debug overlay; later read out / shown before the drive).")]
        public string driverInstruction = "Follow the car ahead";

        [Header("References (set by the scene builder)")]
        [Tooltip("The driver's car. Used to detect the end of the road.")]
        public Transform egoCar;
        public DriveEvents events;

        [Header("End of drive")]
        [Tooltip("The drive ends when the ego car passes this z position (metres along the road).")]
        public float endOfDriveZ = 4900f;

        /// <summary>Seconds since the drive started (physics time, 0.02 s resolution).</summary>
        public double DriveTime => stepCount * (double)Time.fixedDeltaTime;

        /// <summary>Number of physics steps since the drive started.</summary>
        public long StepCount => stepCount;

        /// <summary>True once the ego car has passed endOfDriveZ. The logger stops writing.</summary>
        public bool DriveFinished { get; private set; }

        private long stepCount;

        private void Start()
        {
            if (events != null) events.Record("DriveStart", "", studyMode ? "mode=study" : "mode=test");
        }

        private void FixedUpdate()
        {
            if (DriveFinished) return;

            stepCount++;

            if (egoCar != null && egoCar.position.z >= endOfDriveZ)
            {
                DriveFinished = true;
                if (events != null) events.Record("DriveEnd", "", "reason=end_of_road");
                Debug.Log($"[DriveSession] Drive finished at t = {DriveTime:F2} s. Press R to start a new drive.");
            }
        }

        private void Update()
        {
            // Restart: R on keyboard or Start/Options on the gamepad.
            // Reloading the scene destroys everything, so the logger closes its file
            // and a new drive (new CSV file, new random brake time) begins.
            bool restartKey = Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;
            bool restartPad = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            if (restartKey || restartPad)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }
    }
}

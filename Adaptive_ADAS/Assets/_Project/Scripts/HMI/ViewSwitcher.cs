// ViewSwitcher.cs
// Switches between the driver's-seat camera (used in the study) and a chase
// camera behind the car (for debugging only). C on the keyboard or the
// Select/Back/Share button on the gamepad. Disabled in study mode.

using AdaptiveADAS.Experiment;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveADAS.HMI
{
    public class ViewSwitcher : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public Camera driverCamera;
        public Camera chaseCamera;

        private void Start() => SetDriverView(true);

        private void Update()
        {
            if (session != null && session.studyMode)
            {
                if (!driverCamera.enabled) SetDriverView(true);
                return;
            }
            bool key = Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
            bool pad = Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame;
            if (key || pad) SetDriverView(!driverCamera.enabled);
        }

        private void SetDriverView(bool driver)
        {
            if (driverCamera != null) driverCamera.enabled = driver;
            if (chaseCamera != null) chaseCamera.enabled = !driver;
        }
    }
}

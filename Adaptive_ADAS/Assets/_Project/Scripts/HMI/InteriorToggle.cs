// InteriorToggle.cs  (on the ego car)
// Shows or hides the whole greybox interior (dashboard, speedometer, mirror, A-pillars,
// windshield frame, roof liner, door sills, side display) in one go. The lab simulator
// may have a physical cockpit, in which case the virtual interior must be off.
// The driver camera position does not change.
//   Inspector: tick/untick "Interior Visible" (also during Play).
//   Test mode: key I toggles it. Each change is logged as an event (InteriorOn / InteriorOff).

using AdaptiveADAS.Experiment;
using AdaptiveADAS.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveADAS.HMI
{
    public class InteriorToggle : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public GameObject interiorRoot;
        public DriveSession session;
        public DriveEvents events;

        [Header("State")]
        [Tooltip("Show the virtual interior. Turn off when the lab has a physical cockpit.")]
        public bool interiorVisible = true;

        public bool InteriorOn => interiorRoot != null && interiorRoot.activeSelf;

        private void Start() => Apply(interiorVisible, logEvent: false);

        private void Update()
        {
            bool testMode = session == null || !session.studyMode;
            if (testMode && Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
                interiorVisible = !interiorVisible;

            if (interiorRoot != null && interiorVisible != interiorRoot.activeSelf)
                Apply(interiorVisible, logEvent: true);
        }

        private void Apply(bool on, bool logEvent)
        {
            if (interiorRoot == null) return;
            interiorRoot.SetActive(on);
            if (logEvent && events != null) events.Record(on ? "InteriorOn" : "InteriorOff");
        }
    }
}

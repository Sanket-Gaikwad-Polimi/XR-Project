// GazeSourceSwitch.cs  (on DriveSystems)
// Chooses the gaze source BEFORE Play (Inspector: DriveSystems > Gaze Source Switch > Source):
//   Mouse         the mouse pointer is the gaze (home, no tracker)              [default]
//   PupilCapture  the real eye tracker through Pupil Capture (lab)
//   PupilFake     FakePupilServer driven by the mouse, through the same network code as the
//                 real tracker (home test of the eye-tracker path, no install needed)
// It sets DriverTwin.gazeSource and switches the Pupil components on or off, so nothing tries to
// connect when the mouse is used. The scripted self-test (T) still takes over the gaze for its
// duration and gives it back afterwards, whatever is chosen here.
//
// [DefaultExecutionOrder(-80)]: Awake runs before the Pupil components start.

using UnityEngine;

namespace AdaptiveADAS.Twin
{
    public enum GazeSourceKind { Mouse, PupilCapture, PupilFake }

    [DefaultExecutionOrder(-80)]
    public class GazeSourceSwitch : MonoBehaviour
    {
        [Tooltip("Mouse = home without tracker; PupilCapture = lab; PupilFake = home test of the eye-tracker path.")]
        public GazeSourceKind source = GazeSourceKind.Mouse;

        [Header("References (set by the scene builder)")]
        public DriverTwin driverTwin;
        public MouseGazeSource mouse;
        public PupilGazeSource pupil;
        public FakePupilServer fake;

        [Header("Pupil Capture address (used when Source = PupilCapture)")]
        [Tooltip("127.0.0.1 = Pupil Capture on this PC; otherwise the IP address of the PC running it.")]
        public string captureHost = "127.0.0.1";
        public int capturePort = 50020;

        private void Awake()
        {
            if (driverTwin == null) { Debug.LogError("[GazeSourceSwitch] No DriverTwin. Rebuild the scene."); return; }
            bool usePupil = source != GazeSourceKind.Mouse;
            if (mouse != null) mouse.enabled = source == GazeSourceKind.Mouse;
            if (fake != null) fake.enabled = source == GazeSourceKind.PupilFake;
            if (pupil != null)
            {
                if (source == GazeSourceKind.PupilFake && fake != null) { pupil.host = "127.0.0.1"; pupil.port = fake.port; }
                else if (source == GazeSourceKind.PupilCapture) { pupil.host = captureHost; pupil.port = capturePort; }
                pupil.enabled = usePupil;
            }
            driverTwin.gazeSource = usePupil ? (GazeSource)pupil : mouse;
            Debug.Log($"[GazeSourceSwitch] Gaze source: {source}" + (usePupil && pupil != null ? $" ({pupil.host}:{pupil.port})" : ""));
        }
    }
}

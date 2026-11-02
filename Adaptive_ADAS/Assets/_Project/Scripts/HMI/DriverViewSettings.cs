// DriverViewSettings.cs  (on the DriverCamera)
// Sets the driver camera's field of view (FOV).
//
// GEOMETRIC FOV ("the screen is a window"):
//   If the virtual camera's FOV equals the angle the physical screen covers at the
//   driver's eye, objects appear at their true angular size: a car 50 m away looks
//   exactly as big as a real car 50 m away would through a window of that size.
//       screen height H = diagonal * h / sqrt(w^2 + h^2)        (w:h = aspect, e.g. 16:9)
//       vertical FOV    = 2 * atan( (H / 2) / viewing distance )
//       horizontal FOV  = 2 * atan( (W / 2) / viewing distance )
//   Example: 15.6" 16:9 laptop at 60 cm -> H = 19.4 cm, W = 34.5 cm
//            -> vertical 18.4 deg, horizontal 32.1 deg.
//
// MANUAL FOV: a wider value (e.g. 55 deg vertical) shows more of the scene (mirror,
//   speedometer and side display all on screen) but MINIFIES it: everything looks
//   smaller and farther away than in reality. Fine for development at home; for the
//   study the geometric FOV of the lab screens should be used (or the deviation
//   justified). How FOV mismatch affects speed/distance perception: literature TO FIND.
//
// Unity's Camera.fieldOfView is the VERTICAL FOV; the horizontal FOV follows from the
// window's aspect ratio.

using UnityEngine;

namespace AdaptiveADAS.HMI
{
    [RequireComponent(typeof(Camera))]
    public class DriverViewSettings : MonoBehaviour
    {
        public enum FovMode { GeometricFromScreen, Manual }

        [Header("Mode")]
        [Tooltip("GeometricFromScreen = true-size view computed from the screen below. Manual = use manualVerticalFov.")]
        public FovMode fovMode = FovMode.Manual;

        [Header("Physical screen (used for the geometric FOV)")]
        [Tooltip("Screen diagonal in inches (Sanket's laptop: 15.6).")]
        public float screenDiagonalInch = 15.6f;
        [Tooltip("Aspect ratio of the screen, width part (16 for 16:9).")]
        public float aspectWidth = 16f;
        [Tooltip("Aspect ratio of the screen, height part (9 for 16:9).")]
        public float aspectHeight = 9f;
        [Tooltip("Distance from the driver's eyes to the screen (cm). Sanket at home: ~60 cm.")]
        public float viewingDistanceCm = 60f;

        [Header("Manual")]
        [Tooltip("Vertical FOV (deg) used in Manual mode. 55 deg shows mirror, speedometer and side display on a laptop (our choice for development).")]
        public float manualVerticalFov = 55f;

        [Header("Computed (read-only, for information)")]
        [SerializeField] private float screenHeightCm;
        [SerializeField] private float screenWidthCm;
        [SerializeField] private float geometricVerticalFov;
        [SerializeField] private float geometricHorizontalFov;
        [SerializeField] private float appliedVerticalFov;

        public float AppliedVerticalFov => appliedVerticalFov;
        public float GeometricVerticalFov => geometricVerticalFov;

        private void Awake() => Apply();
        private void OnValidate() => Apply();   // updates the numbers when a value is changed in the Inspector

        public void Apply()
        {
            float diagonalCm = screenDiagonalInch * 2.54f;
            float d = Mathf.Sqrt(aspectWidth * aspectWidth + aspectHeight * aspectHeight);
            screenHeightCm = diagonalCm * aspectHeight / d;
            screenWidthCm = diagonalCm * aspectWidth / d;
            geometricVerticalFov = AngleDeg(screenHeightCm, viewingDistanceCm);
            geometricHorizontalFov = AngleDeg(screenWidthCm, viewingDistanceCm);

            appliedVerticalFov = fovMode == FovMode.GeometricFromScreen ? geometricVerticalFov : manualVerticalFov;
            GetComponent<Camera>().fieldOfView = appliedVerticalFov;
        }

        /// <summary>Angle (deg) covered by an object of given size seen face-on from a distance.</summary>
        public static float AngleDeg(float size, float distance) =>
            2f * Mathf.Atan(size * 0.5f / distance) * Mathf.Rad2Deg;
    }
}

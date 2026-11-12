// GazeSample.cs
// One gaze sample, the same for every gaze source (mouse, scripted test, Pupil eye tracker,
// later an XR headset). The key part is a WORLD RAY: where the driver's line of sight starts
// (the eye) and where it points. DriverTwin classifies the ray, so the source does not matter.
//
//   Mouse      ray from the driver camera through the mouse pointer
//   Scripted   ray from the driver camera through the test target
//   Pupil      ray from the camera that shows the monitor the driver looks at, through the
//              gaze position on that monitor (Pupil Capture Surface Tracker)
//   XR (later) the headset's eye-gaze ray
//
// Design: Docs/EyeTracking.md, section "Gaze ray interface".

using UnityEngine;

namespace AdaptiveADAS.Twin
{
    public struct GazeSample
    {
        /// <summary>False = no usable gaze this step (no data, too old, confidence too low).</summary>
        public bool valid;
        /// <summary>True if the gaze is on one of the displays (monitors). False = valid data but the
        /// driver looks somewhere else (real wheel, room, phone): classified as OffScreen.</summary>
        public bool onDisplay;
        /// <summary>Origin = driver eye in the scene, direction = line of sight.</summary>
        public Ray worldRay;
        /// <summary>0..1. Mouse and scripted = 1.</summary>
        public float confidence;
        /// <summary>Time stamp in the source's own clock (Pupil time, s). NaN if the source has none.</summary>
        public double sourceTime;
        /// <summary>Index of the display (Pupil: index in PupilGazeSource.surfaces). -1 = none.</summary>
        public int displayIndex;
        /// <summary>Normalised position on that display (0..1, origin bottom-left).</summary>
        public Vector2 displayPos;
        /// <summary>For a display that is itself a zone (e.g. a real tablet as the side display,
        /// planned for Step 5): the zone, used instead of the ray. Not used yet.</summary>
        public bool hasDirectZone;
        public GazeZoneType directZone;

        /// <summary>No gaze (invalid sample).</summary>
        public static GazeSample Invalid => new GazeSample
        {
            valid = false, onDisplay = false, confidence = 0f, sourceTime = double.NaN,
            displayIndex = -1, displayPos = new Vector2(float.NaN, float.NaN),
        };

        /// <summary>
        /// Sample from a viewport point of a camera (0..1, origin bottom-left). Points outside 0..1
        /// are valid samples that are not on the display (OffScreen).
        /// </summary>
        public static GazeSample FromViewport(Camera cam, Vector2 vp, int displayIndex = 0,
                                              float confidence = 1f, double sourceTime = double.NaN)
        {
            if (cam == null) return Invalid;
            bool on = vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            return new GazeSample
            {
                valid = true,
                onDisplay = on,
                worldRay = cam.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f)),
                confidence = confidence,
                sourceTime = sourceTime,
                displayIndex = on ? displayIndex : -1,
                displayPos = vp,
            };
        }
    }
}

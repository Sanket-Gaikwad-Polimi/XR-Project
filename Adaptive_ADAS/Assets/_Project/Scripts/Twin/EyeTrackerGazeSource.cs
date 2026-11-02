// EyeTrackerGazeSource.cs
// STUB: placeholder for the lab's eye tracker. It returns "no gaze" until it is
// implemented, so the twin treats the driver as looking off-screen.
// To use it later: add this component to DriveSystems and drag it into
// DriverTwin.gazeSource (instead of MouseGazeSource).
//
// HOW A REAL EYE TRACKER WOULD PLUG IN (not implemented; the lab hardware is not known
// yet. Everything below about specific products is from memory and must be CHECKED in
// the manufacturer's documentation before we rely on it):
//
// 1. What the twin needs: the gaze point ON THE SIMULATOR SCREEN, as viewport
//    coordinates (0..1, origin bottom-left), plus "valid / not valid" per sample.
//    Gaze in the tracker's own coordinates (e.g. a scene-camera image) must first be
//    mapped onto the screen.
//
// 2. Pupil Labs Core (head-worn) with Pupil Capture: Capture's "Surface Tracker" plugin
//    can define the screen as a surface using printed AprilTag markers at the screen
//    corners, and then report gaze in normalised surface coordinates. Capture publishes
//    data over its network API (ZeroMQ + msgpack messages; "Pupil Remote" for requests).
//    Two ways to receive it in Unity:
//      a) a small Python script using Pupil's network API that forwards
//         (timestamp, x, y, confidence) to Unity over UDP (simplest to debug), or
//      b) a C# ZeroMQ client (e.g. the NetMQ package) + a msgpack decoder inside Unity.
//    Pupil Labs Neon / Invisible have a different real-time API: TO CHECK.
//
// 3. Screen-based trackers (e.g. Tobii Pro bar under the screen): their SDK usually gives
//    gaze relative to the display area directly. Check where the ORIGIN is (some use the
//    top-left corner: then y_viewport = 1 - y).
//
// 4. Implementation pattern (any tracker):
//    - receive samples on a background thread (never block Unity's main thread),
//    - keep only the newest sample + its timestamp (lock or Interlocked),
//    - TryGetGaze returns it if it is recent (e.g. < 50 ms old) and its confidence is
//      above a threshold; otherwise false (blink, tracking lost),
//    - log the tracker's own timestamp next to Unity's time so the two clocks can be
//      aligned afterwards (log column to add when the hardware is known),
//    - record calibration quality (accuracy in degrees) in the meta file; it decides
//      how big the gaze zones (especially the lead car zone) should be.

using UnityEngine;

namespace AdaptiveADAS.Twin
{
    public class EyeTrackerGazeSource : GazeSource
    {
        public override string SourceName => "EyeTracker (stub, no data)";

        public override bool TryGetGaze(out Vector2 viewport)
        {
            viewport = default;
            return false;   // not implemented yet
        }
    }
}

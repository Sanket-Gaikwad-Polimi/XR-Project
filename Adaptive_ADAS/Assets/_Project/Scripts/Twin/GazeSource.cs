// GazeSource.cs
// The "gaze source interface": anything that can tell where on the screen the driver
// is looking. DriverTwin only talks to this class, so the source can be swapped
// (mouse at home, eye tracker in the lab, scripted sequence for testing) without
// changing the twin.
//
// Why an abstract class and not a C# interface: Unity cannot show interface-typed
// fields in the Inspector, so a field "IGazeSource gazeSource" could not be set by
// dragging a component. An abstract MonoBehaviour can, and works the same way.
//
// Coordinates: Unity VIEWPORT coordinates of the driver camera (= the whole Game window):
//   x = 0 left edge ... 1 right edge,  y = 0 bottom edge ... 1 top edge.
//   Values outside 0..1 mean the driver looks outside the screen.

using UnityEngine;

namespace AdaptiveADAS.Twin
{
    public abstract class GazeSource : MonoBehaviour
    {
        /// <summary>Short name written to the log meta file, e.g. "Mouse".</summary>
        public abstract string SourceName { get; }

        /// <summary>
        /// Latest gaze point in viewport coordinates. Returns false when there is no valid
        /// sample (e.g. eye tracker lost the eyes, blink, no device).
        /// </summary>
        public abstract bool TryGetGaze(out Vector2 viewport);
    }
}

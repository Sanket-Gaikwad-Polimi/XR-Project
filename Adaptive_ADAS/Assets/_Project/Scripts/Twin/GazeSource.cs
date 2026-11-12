// GazeSource.cs
// The "gaze source interface": anything that can tell where the driver is looking.
// DriverTwin only talks to this class, so the source can be swapped (mouse at home, Pupil
// eye tracker in the lab, fake Pupil stream at home, scripted sequence for the self-test,
// later an XR headset) without changing the twin. Which source is used: GazeSourceSwitch.
//
// Why an abstract class and not a C# interface: Unity cannot show interface-typed
// fields in the Inspector, so a field "IGazeSource gazeSource" could not be set by
// dragging a component. An abstract MonoBehaviour can, and works the same way.
//
// Since the eye-tracker step every source returns a GazeSample: a WORLD RAY (eye + line of
// sight) plus valid flag and confidence (see GazeSample.cs). Before, it was a viewport point
// of the one driver camera, which does not work for three monitors or a headset.

using UnityEngine;

namespace AdaptiveADAS.Twin
{
    public abstract class GazeSource : MonoBehaviour
    {
        /// <summary>Short name written to the log, e.g. "Mouse".</summary>
        public abstract string SourceName { get; }

        /// <summary>
        /// Newest gaze sample for this physics step. Returns false (and an invalid sample) when
        /// there is no usable sample (no device, tracking lost, blink, confidence too low).
        /// </summary>
        public abstract bool TryGetGazeSample(out GazeSample sample);
    }
}

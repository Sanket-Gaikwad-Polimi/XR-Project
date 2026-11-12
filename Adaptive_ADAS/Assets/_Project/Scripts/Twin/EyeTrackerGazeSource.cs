// EyeTrackerGazeSource.cs
// OLD STUB, no longer used. The real eye tracker is PupilGazeSource (Pupil Core + Pupil
// Capture). This class is kept only so that scenes built before the eye-tracker step do not
// show a "missing script" warning. The scene builder does not add it any more; after
// ADAS > Build Base Scene it is gone from the scene and this file can be deleted.

using UnityEngine;

namespace AdaptiveADAS.Twin
{
    public class EyeTrackerGazeSource : GazeSource
    {
        public override string SourceName => "EyeTracker (old stub, no data)";

        public override bool TryGetGazeSample(out GazeSample sample)
        {
            sample = GazeSample.Invalid;
            return false;
        }
    }
}

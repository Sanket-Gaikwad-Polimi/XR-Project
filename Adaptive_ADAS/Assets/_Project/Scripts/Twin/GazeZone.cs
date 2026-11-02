// GazeZone.cs
// Marks an invisible collider as one gaze zone (the driver's gaze ray is tested against
// these colliders; see DriverTwin.cs and Docs/DriverTwin.md).
// The colliders are TRIGGERS on the layer "GazeZones": they never collide with anything
// (DriverTwin switches that layer's collisions off) and are only hit by the gaze ray.
// The renderer of the same object is normally OFF; GazeDebugView switches it on to show
// the zones coloured and semi-transparent (test mode only).

using UnityEngine;

namespace AdaptiveADAS.Twin
{
    /// <summary>Where the driver is looking. Road, OffRoad and OffScreen have no collider:
    /// Road = the gaze ray leaves the car without hitting any zone AND is inside the road-ahead
    /// window (DriverTwin.roadAhead...); OffRoad = it leaves the car outside that window (sky,
    /// median barrier, roadside, near side lanes, side windows); OffScreen = the gaze point is
    /// outside the screen (or no gaze data). OffRoad is last so the numbers of the older zones
    /// (stored in the scene) do not change.</summary>
    public enum GazeZoneType { Road, LeadCar, Mirror, Speedometer, SideDisplay, Interior, OffScreen, OffRoad }

    public class GazeZone : MonoBehaviour
    {
        public GazeZoneType zone = GazeZoneType.Interior;

        /// <summary>Road and LeadCar refill the attention buffer; everything else is "eyes off road".</summary>
        public static bool IsOnRoad(GazeZoneType z) => z == GazeZoneType.Road || z == GazeZoneType.LeadCar;
    }
}

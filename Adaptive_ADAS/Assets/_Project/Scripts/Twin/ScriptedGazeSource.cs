// ScriptedGazeSource.cs  (on DriveSystems; used by GazeBufferTest)
// TEST ONLY: a gaze source that "looks" at a fixed sequence of zones, e.g.
//   3 s Road -> 1.5 s Mirror -> 2.5 s SideDisplay -> 3 s Road.
// For each zone it produces a real gaze POINT on the screen (not just a zone name), so the
// whole chain is tested: gaze point -> ray -> zone collider -> buffer.
//   - zones with colliders: the centre of the zone, projected onto the screen; if that
//     point does not classify as the wanted zone (something in front), a grid of points
//     over the zone is tried;
//   - Road: a point on the road surface 12 m ahead (in front of the bonnet, short of the lead car);
//   - OffScreen: a point left of the screen.
// The sequence is locked to PHYSICS STEPS (each segment = round(seconds / 0.02) steps),
// so the timing is exact and does not depend on the frame rate.

using System;
using System.Collections.Generic;
using AdaptiveADAS.Experiment;
using UnityEngine;

namespace AdaptiveADAS.Twin
{
    public class ScriptedGazeSource : GazeSource
    {
        [Serializable]
        public struct Segment
        {
            public GazeZoneType zone;
            [Tooltip("Duration (s). Rounded to whole physics steps (0.02 s).")]
            public float seconds;
        }

        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public DriverTwin driverTwin;
        public Camera driverCamera;

        [Header("Sequence (default from the Step 3 brief)")]
        public List<Segment> sequence = new List<Segment>
        {
            new Segment { zone = GazeZoneType.Road, seconds = 3.0f },
            new Segment { zone = GazeZoneType.Mirror, seconds = 1.5f },
            new Segment { zone = GazeZoneType.SideDisplay, seconds = 2.5f },
            new Segment { zone = GazeZoneType.Road, seconds = 3.0f },
        };
        [Tooltip("Road target: point on the road surface this far ahead of the driver's eye (m).")]
        public float roadPointAheadM = 12f;

        public override string SourceName => "Scripted";

        public bool Running { get; private set; }
        /// <summary>StepCount when the sequence was started; its first step is StartStep + 1.</summary>
        public long StartStep { get; private set; }
        public int TotalSteps { get; private set; }

        private GazeZone[] allZones;

        public static int StepsFor(float seconds) =>
            Mathf.Max(1, Mathf.RoundToInt(seconds / Time.fixedDeltaTime));

        /// <summary>Starts the sequence at the next physics step.</summary>
        public void Begin(long currentStep)
        {
            StartStep = currentStep;
            TotalSteps = 0;
            foreach (Segment s in sequence) TotalSteps += StepsFor(s.seconds);
            Running = true;
        }

        public void Stop() => Running = false;

        /// <summary>Zone planned for sequence step k (1 = first step). False after the end.</summary>
        public bool TryGetPlannedZone(long k, out GazeZoneType zone)
        {
            zone = GazeZoneType.Road;
            if (k < 1) return false;
            long end = 0;
            foreach (Segment s in sequence)
            {
                end += StepsFor(s.seconds);
                if (k <= end) { zone = s.zone; return true; }
            }
            return false;
        }

        public override bool TryGetGaze(out Vector2 viewport)
        {
            viewport = default;
            if (!Running || session == null) return false;
            if (!TryGetPlannedZone(session.StepCount - StartStep, out GazeZoneType zone)) return false;
            viewport = TargetPoint(zone);
            return true;
        }

        /// <summary>A screen point (viewport) at which the driver twin sees the given zone.</summary>
        public Vector2 TargetPoint(GazeZoneType zone)
        {
            if (zone == GazeZoneType.OffScreen || driverCamera == null) return new Vector2(-0.25f, 0.5f);

            if (zone == GazeZoneType.Road)
            {
                Transform cam = driverCamera.transform;
                Vector3 flatForward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                Vector3 onRoad = cam.position + flatForward * roadPointAheadM;
                onRoad.y = 0f;                                    // road surface
                var candidates = new List<Vector2> { ToViewport(onRoad), new Vector2(0.5f, 0.42f),
                                                     new Vector2(0.35f, 0.45f), new Vector2(0.65f, 0.45f) };
                foreach (Vector2 c in candidates)
                    if (Matches(c, zone)) return c;
                return candidates[0];
            }

            if (zone == GazeZoneType.OffRoad && driverTwin != null)
            {
                // Sky above the road-ahead window, then left / right of it at the horizon.
                float up = Mathf.Tan((driverTwin.roadAheadUpDeg + 6f) * Mathf.Deg2Rad);
                float side = Mathf.Tan((driverTwin.roadAheadHalfWidthDeg + 6f) * Mathf.Deg2Rad);
                var candidates = new List<Vector2> { CameraDirToViewport(new Vector3(0f, up, 1f)),
                                                     CameraDirToViewport(new Vector3(-side, 0f, 1f)),
                                                     CameraDirToViewport(new Vector3(side, 0f, 1f)) };
                foreach (Vector2 c in candidates)
                    if (Matches(c, zone)) return c;
                return candidates[0];
            }

            // Zones with colliders: centre first, then a 7 x 7 grid over the zone's screen area.
            if (allZones == null)
                allZones = FindObjectsByType<GazeZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Vector2 fallback = new Vector2(0.5f, 0.5f);
            foreach (GazeZone z in allZones)
            {
                if (z == null || z.zone != zone || !z.gameObject.activeInHierarchy) continue;
                Bounds b = z.GetComponent<Collider>().bounds;
                Vector2 centre = ToViewport(b.center);
                fallback = centre;
                if (Matches(centre, zone)) return centre;

                Rect r = ScreenRect(b);
                for (int i = 1; i <= 7; i++)
                    for (int j = 1; j <= 7; j++)
                    {
                        var p = new Vector2(r.xMin + r.width * i / 8f, r.yMin + r.height * j / 8f);
                        if (Matches(p, zone)) return p;
                    }
            }
            return fallback;   // zone not reachable on screen: the test will report a zone mismatch
        }

        private bool Matches(Vector2 vp, GazeZoneType zone) =>
            driverTwin != null && driverTwin.Classify(vp) == zone;

        /// <summary>Viewport point of a direction given in the driver camera's own frame.</summary>
        private Vector2 CameraDirToViewport(Vector3 localDir) =>
            ToViewport(driverCamera.transform.position + driverCamera.transform.TransformDirection(localDir));

        private Vector2 ToViewport(Vector3 world)
        {
            Vector3 v = driverCamera.WorldToViewportPoint(world);
            return v.z > 0f ? new Vector2(v.x, v.y) : new Vector2(-1f, -1f);   // behind the camera = off-screen
        }

        /// <summary>Screen area (viewport) covered by a world box.</summary>
        private Rect ScreenRect(Bounds b)
        {
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector2 v = ToViewport(corner);
                min = Vector2.Min(min, v);
                max = Vector2.Max(max, v);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}

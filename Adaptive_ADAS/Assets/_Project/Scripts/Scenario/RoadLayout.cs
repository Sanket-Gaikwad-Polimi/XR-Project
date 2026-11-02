// RoadLayout.cs
// Describes the road geometry in ONE place. The scene builder reads these numbers
// to build the road, and the twin reads them to compute the lane position.
//
// Coordinate convention used in the whole project:
//   +z = driving direction of the ego car (along the road)
//   +x = to the right of the ego car
//   y  = up, road surface at y = 0
// Right-hand traffic (Italy): our carriageway is on the +x side of the median,
// the opposite carriageway is on the -x side.
//
// Lane numbering on our carriageway: 0 = left (overtaking) lane, 1 = right lane.

using UnityEngine;

namespace AdaptiveADAS.Scenario
{
    public class RoadLayout : MonoBehaviour
    {
        [Header("Road geometry (metres)")]
        [Tooltip("Width of one lane. 3.5 m is a typical motorway value (TO VERIFY in a road-design standard).")]
        public float laneWidth = 3.5f;

        [Tooltip("Lanes per direction.")]
        public int lanesPerDirection = 2;

        [Tooltip("Width of the central median between the two carriageways (our choice).")]
        public float medianWidth = 3f;

        [Tooltip("Hard shoulder to the right of the right lane (our choice, TO VERIFY).")]
        public float shoulderWidth = 2.5f;

        [Tooltip("Road length. 5 km = about 3.3 minutes at 90 km/h.")]
        public float roadLength = 5000f;

        [Header("Lane markings (metres)")]
        [Tooltip("Width of painted lines (our choice, TO VERIFY).")]
        public float lineWidth = 0.15f;

        [Tooltip("Length of one dash of the lane divider (TO VERIFY, Italian motorway rules).")]
        public float dashLength = 4.5f;

        [Tooltip("Gap between two dashes (TO VERIFY, Italian motorway rules).")]
        public float dashGap = 7.5f;

        [Header("Roadside")]
        [Tooltip("Spacing of the white roadside posts. They give a sense of speed (optic flow). Our choice.")]
        public float postSpacing = 50f;

        /// <summary>x of the inner edge of our carriageway (next to the median).</summary>
        public float CarriagewayInnerX => medianWidth * 0.5f;

        /// <summary>x of the outer edge of the paved road, including the shoulder.</summary>
        public float PavedOuterX => CarriagewayInnerX + lanesPerDirection * laneWidth + shoulderWidth;

        /// <summary>x of the centre of lane i on OUR carriageway (0 = left lane).</summary>
        public float LaneCentreX(int laneIndex)
        {
            return CarriagewayInnerX + (laneIndex + 0.5f) * laneWidth;
        }

        /// <summary>Which of our lanes contains x. Returns -1 if x is not on one of our lanes.</summary>
        public int LaneIndexAt(float x)
        {
            float fromInnerEdge = x - CarriagewayInnerX;
            if (fromInnerEdge < 0f) return -1;
            int index = Mathf.FloorToInt(fromInnerEdge / laneWidth);
            return index < lanesPerDirection ? index : -1;
        }
    }
}

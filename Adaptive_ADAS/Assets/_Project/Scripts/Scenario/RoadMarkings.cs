// RoadMarkings.cs
// Creates the lane markings and the roadside posts WHEN THE DRIVE STARTS
// (in Awake), from the numbers in RoadLayout.
//
// Why at runtime and not in the scene file?
//   The first version created ~830 lane-dash objects and ~200 posts in the editor.
//   Each object is saved as text in BaseScene.unity, which made the file 2.8 MB and
//   every rebuild a huge git change. Now the scene only stores this component; the
//   markings are rebuilt identically every time from the same numbers.
//   All lane lines are ONE mesh (one object, one draw call): each dash is a flat
//   rectangle (4 corners, 2 triangles) lying 5 mm above the asphalt.
//
// Note: markings and posts are visible only in Play mode, not in the Scene view
// while editing.

using System.Collections.Generic;
using UnityEngine;

namespace AdaptiveADAS.Scenario
{
    [RequireComponent(typeof(RoadLayout))]
    public class RoadMarkings : MonoBehaviour
    {
        [Header("Materials (set by the scene builder)")]
        public Material lineMaterial;
        public Material postMaterial;

        [Header("Options")]
        public bool createPosts = true;
        [Tooltip("Height of the line surface above the asphalt (m), to avoid flickering.")]
        public float lineHeight = 0.005f;

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private void Awake()
        {
            RoadLayout road = GetComponent<RoadLayout>();
            BuildLines(road);
            if (createPosts) BuildPosts(road);
        }

        private void BuildLines(RoadLayout road)
        {
            float L = road.roadLength;
            float lw = road.lineWidth;
            float inner = road.CarriagewayInnerX;

            foreach (float side in new[] { 1f, -1f })   // +1 = our carriageway, -1 = opposite
            {
                // Continuous line on the median side of the left lane.
                AddRectangle(side * (inner + lw * 0.5f), lw, 0f, L);
                // Dashed lines between lanes.
                for (int k = 1; k < road.lanesPerDirection; k++)
                {
                    float x = side * (inner + k * road.laneWidth);
                    for (float z = 0f; z + road.dashLength <= L; z += road.dashLength + road.dashGap)
                        AddRectangle(x, lw, z, z + road.dashLength);
                }
                // Continuous line between the right lane and the hard shoulder.
                AddRectangle(side * (inner + road.lanesPerDirection * road.laneWidth - lw * 0.5f), lw, 0f, L);
            }

            var mesh = new Mesh { name = "LaneMarkings" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;  // allows > 65k vertices
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("LaneMarkings (runtime)");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = lineMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Adds a flat rectangle centred on x, from z0 to z1, facing up.</summary>
        private void AddRectangle(float x, float width, float z0, float z1)
        {
            int i = vertices.Count;
            float h = width * 0.5f;
            vertices.Add(new Vector3(x - h, lineHeight, z0));
            vertices.Add(new Vector3(x - h, lineHeight, z1));
            vertices.Add(new Vector3(x + h, lineHeight, z1));
            vertices.Add(new Vector3(x + h, lineHeight, z0));
            // Two triangles, clockwise seen from above = facing up in Unity.
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
        }

        private void BuildPosts(RoadLayout road)
        {
            // White posts at both outer road edges: give a sense of speed (optic flow).
            var parent = new GameObject("RoadsidePosts (runtime)").transform;
            parent.SetParent(transform, false);
            float x = road.PavedOuterX + 0.5f;
            for (float z = 0f; z <= road.roadLength; z += road.postSpacing)
            {
                foreach (float side in new[] { 1f, -1f })
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    p.name = "Post";
                    p.transform.SetParent(parent, false);
                    p.transform.localPosition = new Vector3(side * x, 0.5f, z);
                    p.transform.localScale = new Vector3(0.12f, 0.5f, 0.12f);  // 1 m tall
                    p.GetComponent<Renderer>().sharedMaterial = postMaterial;
                    p.AddComponent<CollisionLabel>().label = "Post";
                }
            }
        }
    }
}

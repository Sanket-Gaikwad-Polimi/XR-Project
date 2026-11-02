// SpeedLimitSigns.cs
// Places simple greybox speed-limit signs on the right side of our carriageway when
// the drive starts (like RoadMarkings: generated at runtime, not stored in the scene).
// Sign = grey pole + red disc + white disc + black number.
// The number comes from DriveSession.speedLimitKmh, so sign and instruction always match.
// Each sign object is named "SpeedLimit_<value>" (visible in the Hierarchy during Play).
//
// Digits: drawn from a small digit texture (Textures/SpeedSignDigits.png, digits 0-9 in
// Liberation Sans Bold) WITH MIPMAPS, i.e. pre-shrunk copies of the texture that the GPU uses
// when the sign is far away. Without them (the old TextMesh digits) a far-away number is drawn
// from a few random texels of the large glyphs, and a "90" a few pixels tall can look like "50".
// With mipmaps a far-away number is a soft grey blur until it is close enough to read.
// If the digit material is missing, the old TextMesh digits are used (now bold).
//
// This is GUIDANCE only: nothing limits or controls the car's speed.

using AdaptiveADAS.Experiment;
using UnityEngine;

namespace AdaptiveADAS.Scenario
{
    [RequireComponent(typeof(RoadLayout))]
    public class SpeedLimitSigns : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public Material poleMaterial;
        public Material redMaterial;
        public Material whiteMaterial;
        [Tooltip("Unlit transparent material with SpeedSignDigits.png (made by the scene builder). Empty = TextMesh digits.")]
        public Material digitMaterial;

        [Header("Placement")]
        [Tooltip("z of the first sign (m). Visible right at the start of the drive.")]
        public float firstSignZ = 120f;
        [Tooltip("Distance between signs (m). Our choice.")]
        public float signSpacing = 1000f;
        [Tooltip("Distance of the sign centre outside the paved road edge (m).")]
        public float offsetFromRoadEdge = 1.5f;
        [Tooltip("Height of the sign centre (m).")]
        public float signHeight = 2.4f;

        [Header("Size (see Docs/BaseScene_Parameters.md, 'Speed-limit sign legibility')")]
        [Tooltip("Diameter of the sign (m). OUR CHOICE for legibility on screen: 1.5 m gives digits about 8 px tall at " +
                 "100 m on a 1080-px-high view with the 55 deg FOV. Real Italian sign sizes: TO FIND (smaller).")]
        public float signDiameter = 1.5f;
        [Tooltip("Height of the digits as a fraction of the diameter. Our choice by eye.")]
        [Range(0.2f, 0.7f)] public float digitHeightFraction = 0.5f;
        [Tooltip("Horizontal scale of the digits (1 = the font's own width). 0.85 = slightly condensed, so that two " +
                 "digits 0.5 x diameter tall fit inside the white centre. Our choice.")]
        [Range(0.6f, 1f)] public float digitWidthScale = 0.85f;
        [Tooltip("The number is made smaller if its ink is wider than this fraction of the diameter (e.g. 3 digits). " +
                 "The white centre is 0.76 of the diameter. Our choice.")]
        [Range(0.3f, 0.76f)] public float maxNumberWidthFraction = 0.68f;

        // ---- Layout of SpeedSignDigits.png (fixed when the texture was made: Docs/tools/make_sign_digits.js) ----
        // 2048 x 256 px, 10 equal cells (digit d in cell d, 204.8 px wide). The ink of every digit lies
        // between y = 28 and 228 px (200 px tall). Advance (distance from one digit to the next in the
        // font) = 154.75 px. The widest digit ink is 157 px. Each digit quad uses the middle 180 px of its cell.
        private const float AtlasWidthPx = 2048f, CellWidthPx = 204.8f, CellHeightPx = 256f;
        private const float InkHeightPx = 200f, AdvancePx = 154.75f, MaxInkWidthPx = 157f, QuadWidthPx = 180f;

        private void Awake()
        {
            RoadLayout road = GetComponent<RoadLayout>();
            if (session == null)
                Debug.LogError("[ADAS] SpeedLimitSigns has no DriveSession: the signs show the fallback value 90. Rebuild the scene.");
            int limit = session != null ? Mathf.RoundToInt(session.speedLimitKmh) : 90;
            var parent = new GameObject("SpeedLimitSigns (runtime)").transform;
            parent.SetParent(transform, false);

            float x = road.PavedOuterX + offsetFromRoadEdge;
            for (float z = firstSignZ; z < road.roadLength; z += signSpacing)
                BuildSign(parent, new Vector3(x, 0f, z), limit);
        }

        private void BuildSign(Transform parent, Vector3 basePos, int limit)
        {
            var sign = new GameObject($"SpeedLimit_{limit}").transform;
            sign.SetParent(parent, false);
            sign.localPosition = basePos;

            // Pole (cylinder primitive: 2 m tall at scale y = 1), up to the sign centre.
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(sign, false);
            pole.transform.localScale = new Vector3(0.08f, signHeight * 0.5f, 0.08f);
            pole.transform.localPosition = new Vector3(0f, signHeight * 0.5f, 0.05f);
            pole.GetComponent<Renderer>().sharedMaterial = poleMaterial;
            pole.AddComponent<CollisionLabel>().label = "Sign";

            // Discs face the approaching driver (towards -z). A cylinder rotated 90 deg about x
            // has its flat faces pointing along z.
            Disc(sign, "RedRing", signDiameter, 0f, redMaterial);
            Disc(sign, "WhiteCentre", signDiameter * 0.76f, -0.012f, whiteMaterial);

            // Digits 1 cm in front of the white disc (its front face is at z = -0.022).
            var centre = new Vector3(0f, signHeight, -0.032f);
            if (digitMaterial != null) BuildDigitQuads(sign, limit.ToString(), centre);
            else BuildTextMesh(sign, limit.ToString(), centre);
        }

        /// <summary>One flat mesh with a quad per digit, cut out of the digit texture.</summary>
        private void BuildDigitQuads(Transform sign, string text, Vector3 centre)
        {
            // Size: digit ink height = digitHeightFraction x diameter; whole number smaller if too wide.
            float sy = signDiameter * digitHeightFraction / InkHeightPx;   // metres per texture pixel, vertical
            float sx = sy * digitWidthScale;                               // metres per texture pixel, horizontal
            float inkWidth = ((text.Length - 1) * AdvancePx + MaxInkWidthPx) * sx;
            float maxWidth = signDiameter * maxNumberWidthFraction;
            if (inkWidth > maxWidth) { float k = maxWidth / inkWidth; sx *= k; sy *= k; }

            float quadW = QuadWidthPx * sx, quadH = CellHeightPx * sy, step = AdvancePx * sx;
            float width = (text.Length - 1) * step + quadW;                // all quads together
            var vertices = new Vector3[text.Length * 4];
            var uvs = new Vector2[text.Length * 4];
            var triangles = new int[text.Length * 6];
            for (int i = 0; i < text.Length; i++)
            {
                int d = Mathf.Clamp(text[i] - '0', 0, 9);
                float cx = -width * 0.5f + quadW * 0.5f + i * step;   // quad centre (m)
                float u0 = (d * CellWidthPx + (CellWidthPx - QuadWidthPx) * 0.5f) / AtlasWidthPx;
                float u1 = u0 + QuadWidthPx / AtlasWidthPx;
                int v = i * 4;
                vertices[v + 0] = new Vector3(cx - quadW * 0.5f, -quadH * 0.5f, 0f);   // bottom left
                vertices[v + 1] = new Vector3(cx - quadW * 0.5f, +quadH * 0.5f, 0f);   // top left
                vertices[v + 2] = new Vector3(cx + quadW * 0.5f, +quadH * 0.5f, 0f);   // top right
                vertices[v + 3] = new Vector3(cx + quadW * 0.5f, -quadH * 0.5f, 0f);   // bottom right
                uvs[v + 0] = new Vector2(u0, 0f);
                uvs[v + 1] = new Vector2(u0, 1f);
                uvs[v + 2] = new Vector2(u1, 1f);
                uvs[v + 3] = new Vector2(u1, 0f);
                // Clockwise as seen from the driver (looking along +z) = front face.
                int t = i * 6;
                triangles[t + 0] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = "SpeedLimitDigits_" + text, vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Number");
            go.transform.SetParent(sign, false);
            go.transform.localPosition = centre;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = digitMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Old method (fallback only): legacy TextMesh, now bold. No mipmaps, see the top of the file.</summary>
        private void BuildTextMesh(Transform sign, string text, Vector3 centre)
        {
            var textGo = new GameObject("Number");
            textGo.transform.SetParent(sign, false);
            textGo.transform.localPosition = centre;
            var tm = textGo.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.text = text;
            tm.fontSize = 100;            // large font size = sharp text up close
            tm.fontStyle = FontStyle.Bold;
            tm.characterSize = 0.04f;     // then scaled to the wanted height below
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.black;
            var mr = textGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;

            float h = mr.bounds.size.y;
            if (h > 0.001f) textGo.transform.localScale *= (signDiameter * digitHeightFraction) / h;
        }

        private void Disc(Transform sign, string name, float diameter, float zOffset, Material mat)
        {
            var d = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            d.name = name;
            Object.Destroy(d.GetComponent<Collider>());
            d.transform.SetParent(sign, false);
            d.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            d.transform.localScale = new Vector3(diameter, 0.01f, diameter);
            d.transform.localPosition = new Vector3(0f, signHeight, zOffset);
            d.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}

// GazeDebugView.cs  (on DriveSystems)
// TEST MODE ONLY (nothing is shown in study mode):
//   - a small gaze dot where the driver is "looking" (the mouse pointer is hidden while
//     driving), coloured by the current gaze zone, with zone, buffer and state next to it.  G toggles.
//     The label is small (font scaled to the screen height), sits below-right of the dot, and
//     moves out of the way of the DebugOverlay panel so the two never overlap.
//   - the invisible gaze-zone colliders drawn coloured and semi-transparent, plus the outline
//     of the ROAD-AHEAD WINDOW (green rectangle): gaze out of the windows inside it = Road,
//     outside it = OffRoad.  Z toggles.
// Colours: LeadCar orange, Mirror cyan, Speedometer yellow, SideDisplay magenta,
//          Interior lavender, Road green, OffRoad grey (Road / OffRoad: dot only, no collider).

using System.Collections.Generic;
using AdaptiveADAS.Experiment;
using AdaptiveADAS.Twin;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveADAS.HMI
{
    public class GazeDebugView : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public DriverTwin driverTwin;

        [Header("Display (test mode only)")]
        [Tooltip("Show the gaze dot (G).")]
        public bool showGazeDot = true;
        [Tooltip("Show the gaze zones coloured and semi-transparent (Z).")]
        public bool showZones = false;
        [Tooltip("Gaze dot diameter in pixels.")]
        public int dotSizePx = 12;
        [Tooltip("Label font size as a fraction of the screen height (0.011 = 12 px at 1080). Our choice.")]
        public float labelFontPerHeight = 0.011f;
        [Tooltip("Smallest label font size (px).")]
        public int labelMinFontSize = 10;

        private readonly List<Renderer> zoneRenderers = new List<Renderer>();
        private Texture2D dotTexture;
        private GUIStyle labelStyle;
        private readonly GUIContent labelContent = new GUIContent();

        private bool TestMode => session == null || !session.studyMode;

        /// <summary>Colour of each zone (also used by the scene builder for the zone materials).</summary>
        public static Color ZoneColor(GazeZoneType z) => z switch
        {
            GazeZoneType.Road => new Color(0.20f, 1.00f, 0.30f),
            GazeZoneType.LeadCar => new Color(1.00f, 0.45f, 0.10f),
            GazeZoneType.Mirror => new Color(0.10f, 0.90f, 1.00f),
            GazeZoneType.Speedometer => new Color(1.00f, 0.90f, 0.10f),
            GazeZoneType.SideDisplay => new Color(1.00f, 0.20f, 0.80f),
            GazeZoneType.Interior => new Color(0.60f, 0.60f, 1.00f),
            GazeZoneType.OffRoad => new Color(0.70f, 0.70f, 0.70f),
            _ => new Color(1.00f, 0.25f, 0.25f),
        };

        private void Start()
        {
            // All zones in the scene, including those under a hidden interior.
            foreach (GazeZone z in FindObjectsByType<GazeZone>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (z.TryGetComponent(out Renderer r)) zoneRenderers.Add(r);
            dotTexture = MakeDot(32);
            ApplyZoneVisibility();
        }

        private void Update()
        {
            if (TestMode && Keyboard.current != null)
            {
                if (Keyboard.current.gKey.wasPressedThisFrame) showGazeDot = !showGazeDot;
                if (Keyboard.current.zKey.wasPressedThisFrame) showZones = !showZones;
            }
            ApplyZoneVisibility();
        }

        private void ApplyZoneVisibility()
        {
            bool on = showZones && TestMode;
            foreach (Renderer r in zoneRenderers)
                if (r != null && r.enabled != on) r.enabled = on;
        }

        private void OnGUI()
        {
            if (!TestMode || driverTwin == null) return;
            GUI.depth = -1;                          // on top of the DebugOverlay panel (depth 1)
            if (showZones) DrawRoadAheadWindow();
            if (!showGazeDot || !driverTwin.GazeValid) return;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    wordWrap = false,
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(4, 4, 1, 1),
                    margin = new RectOffset(0, 0, 0, 0),
                };
                labelStyle.normal.textColor = Color.white;
            }
            labelStyle.fontSize = Mathf.Max(labelMinFontSize, Mathf.RoundToInt(Screen.height * labelFontPerHeight));
            Vector2 vp = driverTwin.GazeViewport;
            float x = vp.x * Screen.width;
            float y = (1f - vp.y) * Screen.height;   // GUI y runs top-down, viewport y bottom-up
            float s = dotSizePx;

            Color old = GUI.color;
            GUI.color = ZoneColor(driverTwin.Zone);
            GUI.DrawTexture(new Rect(x - s * 0.5f, y - s * 0.5f, s, s), dotTexture);
            GUI.color = old;
            // Zone, buffer and state next to the dot.
            labelContent.text = $"{driverTwin.Zone} · {driverTwin.Buffer:F2} s · {driverTwin.State}" +
                                (driverTwin.HazardSeen ? " · hazard seen" : "");
            Vector2 size = labelStyle.CalcSize(labelContent);
            Rect r = PlaceLabel(new Vector2(x, y), s, size, DebugOverlay.PanelRect);

            old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);    // faint dark background: readable on sky and road
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(r, labelContent, labelStyle);
        }

        /// <summary>Green outline of the road-ahead window (only in the driver's view, not the chase camera).</summary>
        private void DrawRoadAheadWindow()
        {
            Camera cam = driverTwin.driverCamera;
            if (cam == null || !cam.isActiveAndEnabled) return;
            float tx = Mathf.Tan(driverTwin.roadAheadHalfWidthDeg * Mathf.Deg2Rad);
            float tu = Mathf.Tan(driverTwin.roadAheadUpDeg * Mathf.Deg2Rad);
            float td = Mathf.Tan(driverTwin.roadAheadDownDeg * Mathf.Deg2Rad);
            Vector2 a = ToGui(cam, new Vector3(-tx, tu, 1f));     // top left
            Vector2 b = ToGui(cam, new Vector3(tx, -td, 1f));     // bottom right
            Rect r = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
            const float w = 2f;
            Color old = GUI.color;
            GUI.color = ZoneColor(GazeZoneType.Road);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, w), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - w, r.width, w), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, w, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - w, r.yMin, w, r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>GUI position (pixels, y down) of a direction in the camera's own frame.</summary>
        private static Vector2 ToGui(Camera cam, Vector3 localDir)
        {
            Vector3 s = cam.WorldToScreenPoint(cam.transform.position + cam.transform.TransformDirection(localDir));
            return new Vector2(s.x, Screen.height - s.y);
        }

        /// <summary>
        /// Label position: below-right of the dot. If it would leave the screen it goes left of /
        /// above the dot. If it would overlap the overlay panel, it moves to the right of the panel.
        /// </summary>
        private static Rect PlaceLabel(Vector2 dot, float dotSize, Vector2 size, Rect panel)
        {
            float gap = dotSize * 0.75f;
            var r = new Rect(dot.x + gap, dot.y + gap * 0.5f, size.x, size.y);
            if (r.xMax > Screen.width) r.x = dot.x - gap - size.x;            // left of the dot
            if (r.yMax > Screen.height) r.y = dot.y - gap * 0.5f - size.y;    // above the dot
            if (panel.width > 0f && r.Overlaps(panel)) r.x = panel.xMax + 4f; // right of the panel
            r.x = Mathf.Clamp(r.x, 0f, Mathf.Max(0f, Screen.width - size.x));
            r.y = Mathf.Clamp(r.y, 0f, Mathf.Max(0f, Screen.height - size.y));
            return r;
        }

        /// <summary>White filled circle with a dark rim, tinted by GUI.color.</summary>
        private static Texture2D MakeDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = size * 0.5f;
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    float d = Vector2.Distance(new Vector2(i + 0.5f, j + 0.5f), new Vector2(r, r));
                    Color c = d > r ? Color.clear : d > r - 3f ? new Color(0f, 0f, 0f, 0.8f) : Color.white;
                    tex.SetPixel(i, j, c);
                }
            tex.Apply();
            return tex;
        }

        private void OnDestroy()
        {
            if (dotTexture != null) Destroy(dotTexture);
        }
    }
}

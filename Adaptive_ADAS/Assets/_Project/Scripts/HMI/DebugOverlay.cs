// DebugOverlay.cs
// Development overlay in the top-left corner. For DEVELOPMENT only: the participant-facing
// HUD comes later and will be built with uGUI. Uses Unity's immediate-mode GUI (OnGUI)
// because it needs no Canvas or prefab.
//
// F1 cycles three modes:   COMPACT (default)  ->  FULL  ->  OFF  ->  COMPACT
//   COMPACT  about 6 lines inside the top-left 25 % x 20 % of the screen (user requirement).
//            With the driver camera (no pitch, 55 deg vertical FOV, 16:9) that area is more
//            than ~17 deg above the horizon: sky, roof liner, windshield header and the top of
//            the left A-pillar only. The road, lead car, mirror, speedometer and side display
//            are all lower or further right (angles in Docs/BaseScene_Parameters.md, "Debug overlay").
//   FULL     everything (the old detailed overlay), at most 40 % of the screen width.
//            H shows / hides the key help line (it is only shown in FULL).
//   OFF      no panel. The test-mode HAZARD marker still appears (see below).
// Font sizes follow the screen height. If the text would not fit the size limits, the font
// is made smaller (measured with Unity's own text measurement, GUIStyle.CalcSize).
//
// STUDY MODE: nothing is drawn at all (the overlay would give the hazard away).
// TEST MODE: a short "HAZARD" marker appears at the hazard onset so the timing can be
// checked by eye. It is placed right of the panel, never over it. Never shown to participants.
//
// PanelRect (static) tells GazeDebugView where the panel is, so the gaze label can avoid it.
//
// Eye tracker: when the Pupil source is on (real or fake), COMPACT gets one tracker line
// (status, share of valid samples over 5 s, eyes) and FULL a "Tracker" block (version, clock sync,
// latency, world-camera rate, accuracy). The line is our design for the lab: it should tell at a
// glance whether the gaze data can be trusted.

using System.Collections.Generic;
using AdaptiveADAS.Experiment;
using AdaptiveADAS.Logging;
using AdaptiveADAS.Twin;
using AdaptiveADAS.Vehicle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveADAS.HMI
{
    public class DebugOverlay : MonoBehaviour
    {
        public enum Mode { Compact, Full, Off }

        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public VehicleTwin twin;
        public DriverInput driverInput;
        public EgoCarController egoCar;
        public LeadCarController leadCar;
        public TestAssists testAssists;
        public DriveLogger logger;
        public DriveEvents events;
        public DriverViewSettings driverView;
        public InteriorToggle interior;
        public DriverTwin driverTwin;
        public GazeBufferTest gazeTest;
        public PupilGazeSource pupil;
        public FakePupilServer fakePupil;

        [Header("Display (test mode only)")]
        [Tooltip("F1 cycles COMPACT -> FULL -> OFF. In study mode nothing is drawn, whatever this says.")]
        public Mode mode = Mode.Compact;
        [Tooltip("Key help line in FULL mode (H toggles it while in FULL).")]
        public bool showHelp = true;
        [Tooltip("TTC below this is shown in red (display only, NOT the warning logic).")]
        public float ttcHighlight = 3f;
        [Tooltip("Opacity of the dark panel background (0 = none, 1 = black). Our choice.")]
        [Range(0f, 1f)] public float backgroundAlpha = 0.55f;

        [Header("COMPACT panel (requirement: top-left, at most ~25 % width and ~20 % height)")]
        [Range(0.1f, 0.5f)] public float compactMaxWidth = 0.25f;
        [Range(0.1f, 0.5f)] public float compactMaxHeight = 0.20f;
        [Tooltip("Starting font size as a fraction of the screen height (0.021 = 23 px at 1080, 16 px at 768). " +
                 "Made smaller if the text would not fit the limits above. Our choice.")]
        public float compactFontPerHeight = 0.021f;

        [Header("FULL panel")]
        [Range(0.2f, 1f)] public float fullMaxWidth = 0.40f;
        [Tooltip("Font size as a fraction of the screen height (0.016 = 17 px at 1080, 12 px at 768). Our choice.")]
        public float fullFontPerHeight = 0.016f;

        [Header("Font size limits (px)")]
        [Tooltip("Smallest font the fitting may use; below this the text is clipped instead.")]
        public int minFontSize = 9;
        public int maxFontSize = 28;

        [Header("Test-mode hazard marker (never in study mode)")]
        public bool showHazardMarker = true;
        [Tooltip("How long the HAZARD marker stays on screen after onset (s).")]
        public float hazardMarkerSeconds = 1.0f;
        [Tooltip("Marker font size as a fraction of the screen height (0.06 = 65 px at 1080). Our choice.")]
        public float hazardMarkerFontPerHeight = 0.06f;

        /// <summary>Where the panel was drawn (GUI pixels, origin top-left). Empty when no panel is shown.</summary>
        public static Rect PanelRect { get; private set; }

        // Worst-case COMPACT lines: the font size is chosen so that these fit, so it does not
        // change while driving when numbers get longer. Index AttentionRow also gets the bar.
        private static readonly string[] CompactTemplate =
        {
            "<b>188.8</b> km/h   gear R   cruise 188",
            "Lead 188.8 km/h  <b>Braking</b>  <b>HAZARD ACTIVE</b>",
            "Gap 188.8 m   TTC <b>88.8 s</b>   THW 88.8 s",
            "8.88 s  <b>Distracted</b>  ·  Speedometer",
            "Hazard seen  <b>YES</b>  +88.88 s",
            "Fake  <b>Low confidence</b>  100 %  mono eye0",
            "<b>Gamepad not detected</b>",
            "Gaze test  RUNNING  88.8 / 88.8 s",
            "<b>Drive finished: R = new drive</b>",
        };
        private const int AttentionRow = 3;
        private const string HelpText =
            "F1 overlay mode · H this help · C/Select camera · I interior · G gaze dot · Z gaze zones · T gaze test · " +
            "R/Start restart · E/X cruise · Backspace/Y reset · hold brake when stopped = reverse · " +
            "fake tracker: B blink, M markers lost, N low confidence, K fake calibration";

        private GUIStyle textStyle, wrapStyle, helpStyle, markerStyle;
        private readonly List<string> lines = new List<string>(8);
        private readonly GUIContent tmp = new GUIContent();
        // Cache of the COMPACT fit (recomputed when the screen size or the number of lines changes).
        private int fitScreenW = -1, fitScreenH = -1, fitLines = -1;
        private int compactFont;
        private float compactPanelW;

        private void Update()
        {
            if (session != null && session.studyMode) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame)
                mode = mode == Mode.Compact ? Mode.Full : mode == Mode.Full ? Mode.Off : Mode.Compact;
            if (kb.hKey.wasPressedThisFrame && mode == Mode.Full)
                showHelp = !showHelp;
        }

        private void OnDisable() => PanelRect = Rect.zero;

        private void OnGUI()
        {
            PanelRect = Rect.zero;
            if (twin == null || session == null) return;
            if (session.studyMode) return;          // participants never see any of this

            GUI.depth = 1;                          // the gaze dot (GazeDebugView, depth -1) is drawn on top
            EnsureStyles();
            if (mode == Mode.Compact) DrawCompact();
            else if (mode == Mode.Full) DrawFull();
            DrawHazardMarker();                     // test aid, in every mode, right of the panel
        }

        // ------------------------------------------------------------------ COMPACT

        private void DrawCompact()
        {
            lines.Clear();
            string cruise = testAssists != null && testAssists.CruiseOn ? $"{testAssists.CruiseSetSpeedKmh:F0}" : "off";
            lines.Add($"<b>{twin.EgoSpeed * 3.6f:F1}</b> km/h   {Dim("gear")} {egoCar.Gear}   {Dim("cruise")} {cruise}");

            string hazard = leadCar.HazardActive ? "  <color=#ffb040><b>HAZARD ACTIVE</b></color>" : "";
            lines.Add($"{Dim("Lead")} {twin.LeadSpeed * 3.6f:F1} km/h  {ShortLeadState(leadCar.State)}{hazard}");

            lines.Add($"{Dim("Gap")} {Capped(twin.Gap, " m", 999f)}   {Dim("TTC")} {Ttc("F1", 99f)}   " +
                      $"{Dim("THW")} {Capped(twin.TimeHeadway, " s", 99f)}");
            lines.Add(AttentionText());              // index AttentionRow: the bar is drawn in front of it
            lines.Add(HazardSeenText());

            // Only when relevant:
            if (PupilOn) lines.Add(TrackerShortText());
            if (driverInput != null && string.IsNullOrEmpty(driverInput.GamepadName))
                lines.Add("<color=#ff6060><b>Gamepad not detected</b></color>");
            if (gazeTest != null && !string.IsNullOrEmpty(gazeTest.ShortStatus))
                lines.Add($"{Dim("Gaze test")}  {gazeTest.ShortStatus}");
            if (session.DriveFinished)
                lines.Add("<color=#ffe060><b>Drive finished: R = new drive</b></color>");

            FitCompact(lines.Count);
            int f = compactFont;
            textStyle.fontSize = f;
            float pad = Pad(f), lineH = LineHeight(textStyle), barW = BarWidth(f), gap = Mathf.Round(f * 0.5f);
            float margin = Margin();
            var panel = new Rect(margin, margin, compactPanelW, lines.Count * lineH + 2f * pad);
            PanelRect = panel;
            DrawBackground(panel);

            for (int i = 0; i < lines.Count; i++)
            {
                var row = new Rect(panel.x + pad, panel.y + pad + i * lineH, panel.width - 2f * pad, lineH);
                if (i == AttentionRow)
                {
                    DrawBufferBar(new Rect(row.x, row.y + lineH * 0.2f, barW, lineH * 0.6f));
                    row.xMin += barW + gap;
                }
                GUI.Label(row, lines[i], textStyle);
            }
        }

        /// <summary>
        /// Picks the largest font (starting from compactFontPerHeight x screen height) for which the
        /// worst-case lines fit inside compactMaxWidth x compactMaxHeight of the screen.
        /// </summary>
        private void FitCompact(int lineCount)
        {
            if (Screen.width == fitScreenW && Screen.height == fitScreenH && lineCount == fitLines) return;
            fitScreenW = Screen.width; fitScreenH = Screen.height; fitLines = lineCount;

            float margin = Margin();
            float maxW = Screen.width * compactMaxWidth - margin;
            float maxH = Screen.height * compactMaxHeight - margin;
            int start = Mathf.Clamp(Mathf.RoundToInt(Screen.height * compactFontPerHeight), minFontSize, maxFontSize);
            int rows = Mathf.Max(lineCount, CompactTemplate.Length - 2);   // at least the 6 "normal" lines + the tracker line

            for (int f = start; f >= minFontSize; f--)
            {
                textStyle.fontSize = f;
                float w = 0f;
                for (int i = 0; i < Mathf.Min(rows, CompactTemplate.Length); i++)
                {
                    float lw = TextWidth(textStyle, CompactTemplate[i]);
                    if (i == AttentionRow) lw += BarWidth(f) + Mathf.Round(f * 0.5f);
                    w = Mathf.Max(w, lw);
                }
                w += 2f * Pad(f);
                float h = lineCount * LineHeight(textStyle) + 2f * Pad(f);
                compactFont = f;
                compactPanelW = Mathf.Min(Mathf.Ceil(w), maxW);
                if (w <= maxW && h <= maxH) return;
            }
            // Even the smallest font does not fit (very small or very narrow window): lines that are
            // too long are clipped at the panel edge, and the panel may be a little taller than the limit.
        }

        // ------------------------------------------------------------------ FULL

        private void DrawFull()
        {
            string text = FullText();
            float margin = Margin();
            float panelW = Mathf.Floor(Screen.width * fullMaxWidth - margin);
            int f = Mathf.Clamp(Mathf.RoundToInt(Screen.height * fullFontPerHeight), minFontSize, maxFontSize);

            // Shrink only if the panel would not fit the screen height (normally it does).
            float pad = 0f, lineH = 0f, hText = 0f, hHelp = 0f, panelH = 0f;
            while (true)
            {
                wrapStyle.fontSize = f;
                helpStyle.fontSize = Mathf.Max(minFontSize, Mathf.RoundToInt(f * 0.85f));
                pad = Pad(f);
                lineH = LineHeight(wrapStyle);
                float innerW = panelW - 2f * pad;
                hText = TextHeight(wrapStyle, text, innerW);
                hHelp = showHelp ? TextHeight(helpStyle, HelpText, innerW) : 0f;
                panelH = 2f * pad + hText + lineH + (showHelp ? pad * 0.5f + hHelp : 0f);
                if (panelH <= Screen.height - 2f * margin || f <= minFontSize) break;
                f--;
            }

            var panel = new Rect(margin, margin, panelW, Mathf.Min(panelH, Screen.height - 2f * margin));
            PanelRect = panel;
            DrawBackground(panel);
            float x = panel.x + pad, w = panel.width - 2f * pad, y = panel.y + pad;
            GUI.Label(new Rect(x, y, w, hText), text, wrapStyle);
            y += hText;

            // Attention buffer bar + text (replaces the old wide bar under the box).
            textStyle.fontSize = f;
            float barW = BarWidth(f), gap = Mathf.Round(f * 0.5f);
            string maxText = Dim("(buffer max " + MaxBuffer().ToString("F1") + " s)");
            DrawBufferBar(new Rect(x, y + lineH * 0.2f, barW, lineH * 0.6f));
            GUI.Label(new Rect(x + barW + gap, y, w - barW - gap, lineH),
                      Dim("Attention") + "  " + AttentionText() + "   " + maxText, textStyle);
            y += lineH;

            if (showHelp)
                GUI.Label(new Rect(x, y + pad * 0.5f, w, hHelp), HelpText, helpStyle);
        }

        private string FullText()
        {
            string ttc = Ttc("F2");
            string lane = twin.LaneIndex switch { 0 => "left", 1 => "right", _ => "off lane" };
            string hazard = leadCar.HazardActive ? $"  <color=#ffb040><b>{leadCar.hazardId} ACTIVE</b></color>" : "";

            string gamepad = driverInput == null ? "--" : string.IsNullOrEmpty(driverInput.GamepadName)
                ? "<color=#ff6060><b>NOT DETECTED</b></color>  (see test steps; devices: " + DriverInput.AllDeviceNames + ")"
                : $"<color=#80ff80>{driverInput.GamepadName}</color>";
            string input = driverInput == null ? "--" :
                $"<b>{driverInput.ActiveDevice}</b>   thr {driverInput.Throttle:F2}  brk {driverInput.Brake:F2} (applied {egoCar.AppliedBrake:F2})  str {driverInput.Steer:+0.00;-0.00}";
            string cruise = testAssists != null && testAssists.CruiseOn ? $"ON {testAssists.CruiseSetSpeedKmh:F0} km/h" : "off";
            string modeText = session.studyMode ? "<color=#ffb040>STUDY MODE</color>" : "test mode";

            string file = string.IsNullOrEmpty(logger?.CurrentFilePath)
                ? "logging off"
                : System.IO.Path.GetFileName(logger.CurrentFilePath) + $"  ({logger.RowsWritten} rows)";

            string view = driverView == null ? "--" :
                $"FOV {driverView.AppliedVerticalFov:F1}° vertical ({driverView.fovMode}; geometric would be {driverView.GeometricVerticalFov:F1}°)" +
                $"   interior {(interior != null && interior.InteriorOn ? "on" : "off")}";

            string gaze = "--";
            if (driverTwin != null)
            {
                string seen = driverTwin.HazardSeen
                    ? $"<color=#80ff80>YES</color> (first look {driverTwin.HazardFixationSinceOnset:F2} s after onset)"
                    : "no";
                gaze = $"{driverTwin.Zone} for {driverTwin.GlanceDuration:F1} s   eyes off road {driverTwin.EyesOffRoad:F1} s   " +
                       $"hazard seen {seen}   [{driverTwin.GazeSourceName}]";
                if (gazeTest != null && !string.IsNullOrEmpty(gazeTest.StatusText))
                    gaze += $"\n<b>Gaze test</b>    {gazeTest.StatusText}";
            }

            return
                $"<color=#ffe060><b>INSTRUCTION: {session.driverInstruction} · speed limit {session.speedLimitKmh:F0} km/h</b></color>\n" +
                $"<b>Drive time</b>   {session.DriveTime:F1} s   {modeText}" + (session.DriveFinished ? "   <b>FINISHED (R = new drive)</b>" : "") + "\n" +
                $"<b>Ego speed</b>    {twin.EgoSpeed * 3.6f:F1} km/h   gear {egoCar.Gear}   cruise {cruise}\n" +
                $"<b>Lane</b>         {lane}   offset {Num(twin.LaneOffset, " m", "F2")}   max wheel angle {egoCar.CurrentMaxSteerDeg:F1}°\n" +
                $"<b>Lead</b>         {twin.LeadSpeed * 3.6f:F1} km/h   [{leadCar.State}]   brake lights {(leadCar.BrakeLightsOn ? "ON" : "off")}{hazard}\n" +
                $"<b>Hazard</b>       {leadCar.TriggerStatus}\n" +
                $"<b>Distance</b>     {Num(twin.Gap, " m")}   (along road {twin.LongitudinalGap:F1} m, {(twin.LeadRelevant ? "ahead in my lane" : "not in my path")})\n" +
                $"<b>Closing</b>      {Num(twin.ClosingSpeed * 3.6f, " km/h")}\n" +
                $"<b>TTC</b>          {ttc}    min {Num(twin.MinTTC, " s", "F2")}    headway {Num(twin.TimeHeadway, " s", "F2")}\n" +
                $"<b>Gamepad</b>      {gamepad}\n" +
                $"<b>Input</b>        {input}\n" +
                $"<b>Collisions</b>   lead {egoCar.LeadCollisionEvents}   other {egoCar.OtherCollisionEvents}" +
                    (string.IsNullOrEmpty(egoCar.LastCollisionWith) ? "" : $"   (last: {egoCar.LastCollisionWith})") + "\n" +
                $"<b>Last event</b>   {(events != null ? events.LastEventText : "")}\n" +
                $"<b>View</b>         {view}\n" +
                $"<b>Gaze</b>         {gaze}\n" +
                (PupilOn ? $"<b>Tracker</b>      {TrackerFullText()}\n" : "") +
                $"<b>Log</b>          {file}";
        }

        // ------------------------------------------------------------------ shared pieces

        private string AttentionText()
        {
            if (driverTwin == null) return "attention --";
            string zone = driverTwin.GazeValid ? driverTwin.Zone.ToString() : "no gaze";
            return $"{driverTwin.Buffer:F2} s  <color={StateHex(driverTwin.State)}><b>{driverTwin.State}</b></color>  ·  {zone}";
        }

        private bool PupilOn => pupil != null && pupil.enabled;

        private static string StatusWord(PupilStatus s) => s switch
        {
            PupilStatus.Connected => "<color=#80ff80><b>Connected</b></color>",
            PupilStatus.LowConfidence => "<color=#ffb040><b>Low confidence</b></color>",
            PupilStatus.NoData => "<color=#ff6060><b>NO DATA</b></color>",
            PupilStatus.NotConnected => "<color=#ff6060><b>NOT CONNECTED</b></color>",
            PupilStatus.Connecting => "<color=#ffe060>connecting</color>",
            _ => "off",
        };

        /// <summary>COMPACT tracker line, e.g. "Pupil  Connected  97 %  binocular".</summary>
        private string TrackerShortText()
        {
            string share = float.IsNaN(pupil.ValidShare) ? "" : $"  {pupil.ValidShare * 100f:F0} %";
            string eyes = string.IsNullOrEmpty(pupil.LastEyes) ? "" : "  " + pupil.LastEyes.Replace(" 3d", "").Replace(" 2d", "");
            string extra = pupil.BlinkActive ? "  blink" : "";
            return $"{Dim(pupil.IsFake ? "Fake" : "Pupil")}  {StatusWord(pupil.Status)}{share}{eyes}{extra}";
        }

        /// <summary>FULL tracker block (one long line, wrapped by the panel).</summary>
        private string TrackerFullText()
        {
            pupil.LatencyStats(out double med, out double p95, out int n);
            PupilClockSync c = pupil.LastSync;
            string D(double v, string f, string unit) => double.IsNaN(v) || double.IsInfinity(v) ? "--" : v.ToString(f) + unit;
            string text =
                $"{pupil.SourceName} {pupil.CaptureVersion} at {pupil.Endpoint}  {StatusWord(pupil.Status)}   " +
                $"valid {(float.IsNaN(pupil.ValidShare) ? "--" : (pupil.ValidShare * 100f).ToString("F0") + " %")} " +
                $"(conf ≥ {pupil.confidenceThreshold:F2})   {pupil.SamplesPerSecond:F0} samples/s   eyes {(pupil.LastEyes == "" ? "--" : pupil.LastEyes)}   " +
                $"blinks {pupil.BlinkCount}{(pupil.BlinkActive ? " (now)" : "")}\n" +
                $"              clock offset {(c.ok ? c.offset.ToString("F4") + " s" : "--")}  round trip {(c.ok ? c.roundTripMs.ToString("F2") + " ms" : "--")}  " +
                $"drift {D(pupil.DriftMsPerMinute(), "F2", " ms/min")}   latency median {D(med, "F0", " ms")}  p95 {D(p95, "F0", " ms")}   " +
                $"world camera {D(pupil.MeasuredWorldFps, "F0", " fps")} measured   accuracy {D(pupil.PupilAccuracyDeg, "F2", "°")}  precision {D(pupil.PupilPrecisionDeg, "F2", "°")}";
            if (!string.IsNullOrEmpty(pupil.UnknownSurfaces)) text += $"\n              <color=#ffb040>surfaces not configured in Unity: {pupil.UnknownSurfaces}</color>";
            if (fakePupil != null && fakePupil.enabled) text += $"\n              {fakePupil.StateText}";
            return text;
        }

        private string HazardSeenText()
        {
            if (driverTwin == null) return $"{Dim("Hazard seen")}  --";
            return driverTwin.HazardSeen
                ? $"{Dim("Hazard seen")}  <color=#80ff80><b>YES</b></color>  +{driverTwin.HazardFixationSinceOnset:F2} s"
                : $"{Dim("Hazard seen")}  no";
        }

        /// <summary>TTC text, red below ttcHighlight. cap: larger values are shown as "&gt;cap" (COMPACT only).</summary>
        private string Ttc(string format, float cap = float.PositiveInfinity)
        {
            string ttc = twin.TTC > cap ? Capped(twin.TTC, " s", cap) : Num(twin.TTC, " s", format);
            return !float.IsNaN(twin.TTC) && twin.TTC < ttcHighlight ? $"<color=#ff5050><b>{ttc}</b></color>" : ttc;
        }

        /// <summary>Lead-car state for the COMPACT panel ("WaitingForConditions" is shortened).</summary>
        private static string ShortLeadState(LeadCarController.LeadState s) => s switch
        {
            LeadCarController.LeadState.WaitingForConditions => "Waiting",
            LeadCarController.LeadState.Braking => "<b>Braking</b>",
            _ => s.ToString(),
        };

        private static string Num(float v, string unit, string format = "F1") =>
            float.IsNaN(v) || float.IsInfinity(v) ? "--" : v.ToString(format) + unit;

        /// <summary>Like Num, but values above cap are shown as "&gt;cap" so the COMPACT lines keep their length.</summary>
        private static string Capped(float v, string unit, float cap) =>
            !float.IsNaN(v) && !float.IsInfinity(v) && v > cap ? $">{cap:F0}{unit}" : Num(v, unit);

        private static string Dim(string label) => $"<color=#9aa4ad>{label}</color>";

        private static Color StateColor(AttentionState s) => s switch
        {
            AttentionState.Attentive => new Color(0.25f, 0.85f, 0.35f),
            AttentionState.Reduced => new Color(1.00f, 0.80f, 0.15f),
            _ => new Color(1.00f, 0.25f, 0.20f),
        };

        private static string StateHex(AttentionState s) => "#" + ColorUtility.ToHtmlStringRGB(StateColor(s));

        private float MaxBuffer() => driverTwin != null ? Mathf.Max(0.001f, driverTwin.bufferMax) : 1f;

        /// <summary>
        /// Small attention-buffer bar: full width = bufferMax, colour = attention state
        /// (green Attentive, yellow Reduced, red Distracted), thin white marks at the state limits.
        /// </summary>
        private void DrawBufferBar(Rect r)
        {
            if (driverTwin == null) return;
            float max = MaxBuffer();
            float fill = Mathf.Clamp01((float)driverTwin.Buffer / max);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);                                          // empty part
            GUI.color = StateColor(driverTwin.State);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * fill, r.height), Texture2D.whiteTexture); // buffer
            GUI.color = Color.white;
            foreach (float limit in new[] { driverTwin.distractedBelow, driverTwin.attentiveMin })
                GUI.DrawTexture(new Rect(Mathf.Round(r.x + r.width * Mathf.Clamp01(limit / max)) - 1f, r.y, 2f, r.height),
                                Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawBackground(Rect r)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, backgroundAlpha);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>Red "HAZARD" text for hazardMarkerSeconds after onset, centred in the area right of the panel.</summary>
        private void DrawHazardMarker()
        {
            if (!showHazardMarker || leadCar == null || double.IsNaN(leadCar.BrakeOnsetTime)) return;
            double since = session.DriveTime - leadCar.BrakeOnsetTime;
            if (since < 0 || since > hazardMarkerSeconds) return;

            int f = Mathf.Clamp(Mathf.RoundToInt(Screen.height * hazardMarkerFontPerHeight), 16, 120);
            markerStyle.fontSize = f;
            float left = PanelRect.width > 0f ? PanelRect.xMax : 0f;
            GUI.Label(new Rect(left, Screen.height * 0.12f, Screen.width - left, f * 1.5f),
                      $"HAZARD  (t = {leadCar.BrakeOnsetTime:F2} s)", markerStyle);
        }

        private void EnsureStyles()
        {
            if (textStyle != null) return;
            textStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Clip,          // never draw outside the panel
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
            };
            textStyle.normal.textColor = Color.white;
            wrapStyle = new GUIStyle(textStyle) { wordWrap = true };
            helpStyle = new GUIStyle(wrapStyle);
            helpStyle.normal.textColor = new Color(0.80f, 0.82f, 0.85f);
            markerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
            };
            markerStyle.normal.textColor = new Color(1f, 0.15f, 0.1f);
        }

        // Sizes derived from the font size (our choice: they keep the proportions at any resolution).
        private static float Pad(int fontSize) => Mathf.Round(fontSize * 0.45f);
        private static float BarWidth(int fontSize) => Mathf.Round(fontSize * 4.5f);
        private static float Margin() => Mathf.Max(4f, Mathf.Round(Screen.height * 0.008f));

        private float LineHeight(GUIStyle s) { tmp.text = "Ag"; return Mathf.Ceil(s.CalcSize(tmp).y); }
        private float TextWidth(GUIStyle s, string t) { tmp.text = t; return s.CalcSize(tmp).x; }
        private float TextHeight(GUIStyle s, string t, float width) { tmp.text = t; return Mathf.Ceil(s.CalcHeight(tmp, width)); }
    }
}

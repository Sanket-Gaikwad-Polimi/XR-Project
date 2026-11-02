// GazeBufferTest.cs  (on DriveSystems)
// TEST MODE ONLY. Checks that DriverTwin implements the attention-buffer rules correctly.
//   T (keyboard) starts the test; or tick "Run At Drive Start".
// What happens:
//   1. The driver twin is reset (buffer full, as at the start of a drive) and its gaze source is
//      switched to ScriptedGazeSource (fixed sequence, default 3 s Road, 1.5 s Mirror,
//      2.5 s SideDisplay, 3 s Road). The mouse is ignored while the test runs.
//   2. Every physics step, the twin's buffer, state, zone and eyes-off-road time are compared
//      with EXPECTED values. The expected values come from a separate, continuous-time
//      calculation of the same rules (piecewise-linear integration per segment, written
//      independently of DriverTwin's step-by-step code), using the twin's Inspector parameters.
//   2b. If the sequence and the parameters are the defaults, the buffer and state are ALSO
//      checked against the hand-calculated table in Docs/DriverTwin.md section 8.
//   3. At the end the gaze source is switched back, and a report is written to the Console and
//      to DriveLogs/drive_..._gazetest.txt (next to the drive's CSV). Events GazeTestStart /
//      GazeTestEnd go to the events CSV, so analyses can exclude the synthetic gaze.
//
// [DefaultExecutionOrder(160)] = right after DriverTwin (150) in the same physics step.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using AdaptiveADAS.Experiment;
using AdaptiveADAS.Logging;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveADAS.Twin
{
    [DefaultExecutionOrder(160)]
    public class GazeBufferTest : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public DriveEvents events;
        public DriverTwin driverTwin;
        public ScriptedGazeSource scripted;
        public DriveLogger logger;

        [Header("Test settings")]
        [Tooltip("Start the test automatically at the first physics step of the drive.")]
        public bool runAtDriveStart = false;
        [Tooltip("Report table: one line every N seconds (plus every segment end).")]
        public float reportEverySeconds = 0.5f;
        [Tooltip("Allowed difference between expected and measured buffer / eyes-off-road (s).")]
        public float tolerance = 0.001f;

        /// <summary>Short status for the debug overlay ("" = never run).</summary>
        public string StatusText { get; private set; } = "";
        /// <summary>Very short status for the COMPACT overlay: RUNNING t / total, PASS or FAIL ("" = never run).</summary>
        public string ShortStatus { get; private set; } = "";
        public bool Running { get; private set; }

        private GazeSource previousSource;
        private int stepsCompared, bufferMismatches, stateMismatches, zoneMismatches, eyesOffMismatches, failLines;
        private double maxBufferDiff;
        private readonly StringBuilder table = new StringBuilder();
        private readonly HashSet<int> reportSteps = new HashSet<int>();
        private bool startPending;

        // Docs/DriverTwin.md section 8: (time from test start s, buffer s, state) for the default
        // sequence and the brief's parameter values. Hand-calculated from the rules.
        private static readonly (double t, double buffer, AttentionState state)[] DocTable =
        {
            (3.00, 2.00, AttentionState.Attentive), (4.00, 2.00, AttentionState.Attentive),
            (4.50, 1.50, AttentionState.Attentive), (4.52, 1.48, AttentionState.Reduced),
            (5.50, 0.50, AttentionState.Reduced),   (5.52, 0.48, AttentionState.Distracted),
            (6.00, 0.00, AttentionState.Distracted), (7.00, 0.00, AttentionState.Distracted),
            (7.10, 0.00, AttentionState.Distracted), (7.60, 0.50, AttentionState.Reduced),
            (8.60, 1.50, AttentionState.Attentive), (9.10, 2.00, AttentionState.Attentive),
            (10.00, 2.00, AttentionState.Attentive),
        };
        private bool docCheck;
        private int docChecked, docMismatches;
        private readonly StringBuilder docLines = new StringBuilder();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const double Eps = 1e-6;

        private bool TestMode => session == null || !session.studyMode;

        private void Start()
        {
            if (runAtDriveStart && TestMode) startPending = true;
        }

        private void Update()
        {
            if (!TestMode || Running) return;
            if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame) startPending = true;
        }

        private void FixedUpdate()
        {
            if (session == null || driverTwin == null || scripted == null) return;
            if (session.DriveFinished)
            {
                if (Running) Finish();      // drive ended during the test: report what was compared
                return;
            }

            if (Running) Compare();
            // Starting at the END of a step means the sequence begins exactly at the next step.
            if (startPending && !Running) { startPending = false; StartTest(); }
        }

        // ------------------------------------------------------------------
        private void StartTest()
        {
            previousSource = driverTwin.gazeSource;
            driverTwin.ResetAttention();
            scripted.Begin(session.StepCount);
            driverTwin.gazeSource = scripted;
            Running = true;

            stepsCompared = bufferMismatches = stateMismatches = zoneMismatches = eyesOffMismatches = failLines = 0;
            maxBufferDiff = 0;
            table.Clear();
            docCheck = IsDocDefault();
            docChecked = docMismatches = 0;
            docLines.Clear();
            table.AppendLine("  t(s)  planned zone  measured zone   buffer exp  buffer meas   state exp   state meas   eyes-off exp/meas  ok");

            // Report lines: every reportEverySeconds and at every segment end.
            reportSteps.Clear();
            int every = Mathf.Max(1, ScriptedGazeSource.StepsFor(reportEverySeconds));
            for (int k = every; k <= scripted.TotalSteps; k += every) reportSteps.Add(k);
            int end = 0;
            foreach (ScriptedGazeSource.Segment s in scripted.sequence) { end += ScriptedGazeSource.StepsFor(s.seconds); reportSteps.Add(end); }

            string seq = string.Join(" ", scripted.sequence.ConvertAll(seg => $"{seg.zone}:{seg.seconds.ToString("F2", Inv)}"));
            if (events != null) events.Record("GazeTestStart", "", "sequence=" + seq);
            StatusText = $"RUNNING  ({seq})";
            ShortStatus = "RUNNING";
            Debug.Log($"[GazeBufferTest] Started: {seq}");
        }

        private void Compare()
        {
            int k = (int)(session.StepCount - scripted.StartStep);      // 1 = first step of the sequence
            if (k < 1) return;
            double dt = Time.fixedDeltaTime;
            double tau = k * dt;

            if (!scripted.TryGetPlannedZone(k, out GazeZoneType planned)) { Finish(); return; }
            Expected e = ExpectedAt(tau, dt);

            double bufferDiff = Math.Abs(driverTwin.Buffer - e.buffer);
            bool bufferOk = bufferDiff <= tolerance;
            bool stateOk = driverTwin.State == e.state;
            bool zoneOk = driverTwin.Zone == planned;
            bool eyesOk = Math.Abs(driverTwin.EyesOffRoad - e.eyesOffRoad) <= tolerance;

            stepsCompared++;
            maxBufferDiff = Math.Max(maxBufferDiff, bufferDiff);
            if (!bufferOk) bufferMismatches++;
            if (!stateOk) stateMismatches++;
            if (!zoneOk) zoneMismatches++;
            if (!eyesOk) eyesOffMismatches++;

            bool ok = bufferOk && stateOk && zoneOk && eyesOk;
            bool printFail = !ok && failLines < 10;        // also show the first 10 failing steps
            if (printFail) failLines++;
            if (reportSteps.Contains(k) || printFail)
            {
                table.AppendLine(string.Format(Inv, "{0,6:F2}  {1,-12}  {2,-12}  {3,11:F3}  {4,11:F3}   {5,-10}  {6,-10}   {7,6:F2} / {8,5:F2}   {9}",
                    tau, planned, driverTwin.Zone, e.buffer, driverTwin.Buffer, e.state, driverTwin.State,
                    e.eyesOffRoad, driverTwin.EyesOffRoad, ok ? "OK" : "FAIL"));
            }
            // Hand-calculated table of DriverTwin.md section 8 (default sequence only).
            if (docCheck)
                foreach (var row in DocTable)
                    if (Mathf.RoundToInt((float)(row.t / dt)) == k)
                    {
                        bool rowOk = Math.Abs(driverTwin.Buffer - row.buffer) <= tolerance && driverTwin.State == row.state;
                        docChecked++;
                        if (!rowOk) docMismatches++;
                        docLines.AppendLine(string.Format(Inv, "{0,6:F2}   doc {1,5:F2} s {2,-10}   measured {3,6:F3} s {4,-10}   {5}",
                            row.t, row.buffer, row.state, driverTwin.Buffer, driverTwin.State, rowOk ? "OK" : "FAIL"));
                    }

            StatusText = $"RUNNING  t = {tau:F2} / {scripted.TotalSteps * dt:F2} s";
            ShortStatus = $"RUNNING  {tau:F1} / {scripted.TotalSteps * dt:F1} s";

            if (k >= scripted.TotalSteps) Finish();
        }

        private void Finish()
        {
            Running = false;
            scripted.Stop();
            driverTwin.gazeSource = previousSource;

            bool pass = bufferMismatches == 0 && stateMismatches == 0 && zoneMismatches == 0 && eyesOffMismatches == 0
                        && docMismatches == 0;
            string docPart = docCheck ? $"; DriverTwin.md section 8 table: {docChecked - docMismatches}/{docChecked} OK"
                                      : "; section 8 table not checked (sequence or parameters changed)";
            string summary = $"{(pass ? "PASS" : "FAIL")}: {stepsCompared} steps compared; mismatches: buffer {bufferMismatches}, " +
                             $"state {stateMismatches}, zone {zoneMismatches}, eyes-off-road {eyesOffMismatches}; " +
                             $"max buffer difference {maxBufferDiff.ToString("F6", Inv)} s (tolerance {tolerance.ToString(Inv)} s)" + docPart;
            StatusText = (pass ? "<color=#80ff80>" : "<color=#ff6060>") + summary + "</color>";
            ShortStatus = pass ? "<color=#80ff80><b>PASS</b></color>" : "<color=#ff6060><b>FAIL</b></color> (F1 = details)";
            if (events != null)
                events.Record("GazeTestEnd", pass ? "PASS" : "FAIL",
                    $"steps={stepsCompared};buffer_mismatch={bufferMismatches};state_mismatch={stateMismatches};" +
                    $"zone_mismatch={zoneMismatches};max_buffer_diff_s={maxBufferDiff.ToString("F6", Inv)};" +
                    $"doc_table_ok={(docCheck ? $"{docChecked - docMismatches}/{docChecked}" : "not_checked")}");

            var report = new StringBuilder();
            report.AppendLine("Scripted gaze test (DriverTwin attention buffer)");
            report.AppendLine($"Parameters: buffer max {driverTwin.bufferMax}, start {driverTwin.bufferStart}, refill {driverTwin.refillRate}/s " +
                              $"after {driverTwin.refillDelay} s, drain {driverTwin.drainRate}/s, mirror/speedometer latency {driverTwin.mirrorSpeedometerLatency} s, " +
                              $"states: Attentive >= {driverTwin.attentiveMin}, Distracted < {driverTwin.distractedBelow} (our design choice)");
            report.AppendLine("Expected values: continuous-time calculation of the rules (independent of DriverTwin's code).");
            report.AppendLine(summary);
            if (zoneMismatches > 0)
                report.AppendLine("Zone mismatches: a planned zone could not be hit on screen. Is the interior on (I) and the FOV " +
                                  "wide enough (Manual 55 deg) to show the mirror and side display?");
            report.AppendLine();
            if (docCheck)
            {
                report.AppendLine("Check against the hand-calculated table in Docs/DriverTwin.md section 8:");
                report.Append(docLines.ToString());
                report.AppendLine();
            }
            report.AppendLine("Every-step comparison with the continuous-time calculation (lines every " +
                              reportEverySeconds.ToString(Inv) + " s, at segment ends and at the first failing steps):");
            report.Append(table.ToString());
            Debug.Log("[GazeBufferTest] " + summary + "\n" + report);

            string csv = logger != null ? logger.CurrentFilePath : "";
            if (!string.IsNullOrEmpty(csv))
            {
                string path = Path.Combine(Path.GetDirectoryName(csv), Path.GetFileNameWithoutExtension(csv) + "_gazetest.txt");
                File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
                Debug.Log($"[GazeBufferTest] Report written to {path}");
            }
        }

        // ------------------------------------------------------------------
        // Expected values: continuous-time version of the rules in Docs/DriverTwin.md.
        // Within one segment the buffer only rises (Road/LeadCar) or only falls (others), so
        // clamping at the end of each (partial) segment is exact.
        // ------------------------------------------------------------------
        private struct Expected { public double buffer, eyesOffRoad; public AttentionState state; }

        private Expected ExpectedAt(double tau, double dt)
        {
            DriverTwin p = driverTwin;
            double b = Math.Max(0.0, Math.Min(p.bufferMax, p.bufferStart));
            double roadHeld = 0, glanceHeld = 0, eyesOff = 0, segStart = 0;
            bool first = true;
            GazeZoneType prev = GazeZoneType.Road;

            foreach (ScriptedGazeSource.Segment s in scripted.sequence)
            {
                double segLen = ScriptedGazeSource.StepsFor(s.seconds) * dt;
                double d = Math.Min(tau, segStart + segLen) - segStart;    // part of this segment up to tau
                if (d <= Eps) break;

                bool onRoad = GazeZone.IsOnRoad(s.zone);
                bool prevOnRoad = !first && GazeZone.IsOnRoad(prev);
                if (first || s.zone != prev) glanceHeld = 0;              // new glance
                if (onRoad)
                {
                    if (!first && !prevOnRoad) roadHeld = 0;              // just came back to the road
                    double gain = p.refillRate * (Pos(roadHeld + d - p.refillDelay) - Pos(roadHeld - p.refillDelay));
                    b = Math.Min(p.bufferMax, b + gain);
                    roadHeld += d;
                    eyesOff = 0;
                }
                else
                {
                    if (first || prevOnRoad) eyesOff = 0;                 // just left the road
                    double loss = s.zone == GazeZoneType.Mirror || s.zone == GazeZoneType.Speedometer
                        ? p.drainRate * (Pos(glanceHeld + d - p.mirrorSpeedometerLatency) - Pos(glanceHeld - p.mirrorSpeedometerLatency))
                        : p.drainRate * d;
                    b = Math.Max(0.0, b - loss);
                    eyesOff += d;
                    roadHeld = 0;
                }
                glanceHeld += d;
                prev = s.zone;
                first = false;
                segStart += segLen;
                if (tau <= segStart + Eps) break;
            }

            AttentionState state = b >= p.attentiveMin - Eps ? AttentionState.Attentive
                                 : b >= p.distractedBelow - Eps ? AttentionState.Reduced
                                 : AttentionState.Distracted;
            return new Expected { buffer = b, eyesOffRoad = eyesOff, state = state };
        }

        private static double Pos(double x) => Math.Max(0.0, x);

        /// <summary>True if sequence and parameters are the defaults that DriverTwin.md section 8 was calculated for.</summary>
        private bool IsDocDefault()
        {
            var seq = scripted.sequence;
            bool sequenceOk = seq.Count == 4
                && seq[0].zone == GazeZoneType.Road && Mathf.Approximately(seq[0].seconds, 3.0f)
                && seq[1].zone == GazeZoneType.Mirror && Mathf.Approximately(seq[1].seconds, 1.5f)
                && seq[2].zone == GazeZoneType.SideDisplay && Mathf.Approximately(seq[2].seconds, 2.5f)
                && seq[3].zone == GazeZoneType.Road && Mathf.Approximately(seq[3].seconds, 3.0f);
            DriverTwin p = driverTwin;
            bool paramsOk = Mathf.Approximately(p.bufferMax, 2f) && Mathf.Approximately(p.bufferStart, 2f)
                && Mathf.Approximately(p.refillRate, 1f) && Mathf.Approximately(p.refillDelay, 0.1f)
                && Mathf.Approximately(p.drainRate, 1f) && Mathf.Approximately(p.mirrorSpeedometerLatency, 1f)
                && Mathf.Approximately(p.attentiveMin, 1.5f) && Mathf.Approximately(p.distractedBelow, 0.5f)
                && Mathf.Approximately(Time.fixedDeltaTime, 0.02f);
            return sequenceOk && paramsOk;
        }
    }
}

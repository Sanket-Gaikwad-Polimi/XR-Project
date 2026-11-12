// PupilGazeSource.cs  (on DriveSystems)
// Gaze from a Pupil Core eye tracker through Pupil Capture (or from FakePupilServer at home).
// Design and every parameter's reason: Docs/EyeTracking.md.
//
// HOW IT WORKS
//  1. Connects to Pupil Capture's "Pupil Remote" (ZeroMQ request socket, default port 50020) and asks:
//       "SUB_PORT"  port of Capture's data stream,   "v"  Capture version,   "t"  current Pupil time.
//     Optional: starts Capture's blink detector plugin ("Blink_Detection").
//  2. Subscribes to:  surfaces.<name>   gaze mapped onto each monitor (Surface Tracker plugin),
//                     blinks            blink onsets/offsets (Blink Detection plugin),
//                     logging.info      to catch Capture's "Angular accuracy/precision" after calibration,
//                     notify.calibration.successful / failed.
//     (Topic names and message fields checked in the Pupil Capture source code, 2026-11-12:
//      surface_tracker.py, surface.py, blink_detection.py, pupil_remote_controller.py, accuracy_visualizer.py.)
//  3. Every physics step DriverTwin asks for the NEWEST gaze sample: its surface (= monitor) and the
//     position on it (norm_pos 0..1). The camera that shows that monitor turns it into a world ray.
//  4. Clock sync: "t" is asked 10 times; the answer with the shortest round trip gives
//     offset = Pupil time - local time. Repeated every 30 s (drift). Latency of each sample =
//     receive time - (Pupil time stamp - offset).
//
// THREADING: none. Like hmd-eyes, the sockets are read on Unity's main thread (Update and each
// physics step), without waiting. So the receive time is accurate to about one frame.
// Robust to Capture not running / closing: requests time out, gaze becomes INVALID (never a frozen
// last value), the status says why, and the connection is retried every 2 s.
//
// [DefaultExecutionOrder(-60)]: before the cars and the twin.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using AdaptiveADAS.Experiment;
using AdaptiveADAS.Logging;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AdaptiveADAS.Twin
{
    public enum PupilStatus { Off, Connecting, NotConnected, Connected, NoData, LowConfidence }
    public enum EyeSetup { Unknown, Monocular, Binocular }

    /// <summary>One Pupil Capture surface (one monitor) and the Unity camera that shows it.</summary>
    [Serializable]
    public class PupilSurface
    {
        [Tooltip("Surface name exactly as defined in Pupil Capture's Surface Tracker, e.g. monitor_center.")]
        public string surfaceName = "monitor_center";
        [Tooltip("Camera that renders this monitor. Until part G (three monitors) this is the driver camera.")]
        public Camera camera;
        [Tooltip("PLANNED (Step 5), not tested: the surface is a real display that is itself a gaze zone " +
                 "(e.g. a tablet as the side display). Then 'zone' is used instead of a ray.")]
        public bool isZone;
        public GazeZoneType zone = GazeZoneType.SideDisplay;
    }

    /// <summary>One gaze point as received (one row of drive_..._gaze.csv).</summary>
    public struct PupilRawSample
    {
        public double localTime;        // receive time, local clock (s)
        public double driveTime;        // drive time at receive (s)
        public double pupilTime;        // gaze time stamp, Pupil clock (s)
        public double worldFrameTime;   // time stamp of the world-camera frame the surface was found in (Pupil clock)
        public string surface;
        public int display;             // index in 'surfaces', -1 = not configured
        public double x, y;             // norm_pos on the surface (0..1, origin bottom-left)
        public float confidence;
        public bool onSurf;
        public string gazeTopic;        // e.g. gaze.3d.01._on_surface (01 = binocular, 0 / 1 = one eye)
        public double latencyMs;        // estimated, NaN before the first clock sync
    }

    /// <summary>Result of one clock synchronisation.</summary>
    public struct PupilClockSync
    {
        public bool ok;
        public string label;
        public double localTime;        // local clock at the middle of the best round trip (s)
        public double offset;           // Pupil time - local time (s)
        public double roundTripMs;      // best round trip (ms)
        public int requests;
    }

    [DefaultExecutionOrder(-60)]
    public class PupilGazeSource : GazeSource
    {
        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public DriveEvents events;

        [Header("Connection")]
        [Tooltip("PC running Pupil Capture. 127.0.0.1 = this PC. For a second PC: its IP address (open the port in its firewall).")]
        public string host = "127.0.0.1";
        [Tooltip("Pupil Remote port. 50020 is Capture's default (Pupil source, main.py). Shown in Capture's Pupil Remote menu.")]
        public int port = 50020;
        [Tooltip("A request without answer after this time = Capture not reachable (s). Our choice.")]
        public float requestTimeout = 1.0f;
        [Tooltip("Wait before trying again after a failed connection (s). Our choice.")]
        public float retryInterval = 2.0f;
        [Tooltip("No surface data for this long = status 'No data' (s). Our choice.")]
        public float noDataAfter = 1.0f;

        [Header("Surfaces (one per monitor; names as in Pupil Capture)")]
        public List<PupilSurface> surfaces = new List<PupilSurface> { new PupilSurface() };

        [Header("Sample rules")]
        [Tooltip("Samples below this confidence are invalid. 0.6 = the value hmd-eyes names as the usual filter " +
                 "('below ~0.6', GazeData.cs). Pupil Player's own default: TO VERIFY. Adopting it is our choice.")]
        [Range(0f, 1f)] public float confidenceThreshold = 0.6f;
        [Tooltip("The newest sample is used only if it was RECEIVED at most this long ago (s); older = invalid " +
                 "(stream stalled). Our choice: several world-camera frames at 30 fps.")]
        public float maxSampleAge = 0.2f;
        [Tooltip("Share of valid samples below which the status is 'Low confidence' (over the last 5 s). Our choice.")]
        [Range(0f, 1f)] public float lowConfidenceShare = 0.5f;

        [Header("Blinks (part C uses them later)")]
        [Tooltip("Receive Capture's blink events (topic 'blinks').")]
        public bool subscribeBlinks = true;
        [Tooltip("Ask Capture to start its Blink Detection plugin at connect (with Capture's default settings).")]
        public bool startBlinkDetector = true;

        [Header("Clock sync")]
        [Tooltip("Number of 't' requests per sync; the shortest round trip is used. Our choice.")]
        public int syncRequests = 10;
        [Tooltip("Repeat the sync this often during the drive, to measure drift (s). Our choice.")]
        public float resyncInterval = 30f;

        [Header("Lab notes (typed in by the experimenter; written to the meta file)")]
        public string trackerModel = "Pupil Core";
        [Tooltip("Eye cameras on the headset. The log also records what the data says (mono / binocular per sample).")]
        public EyeSetup expectedEyes = EyeSetup.Unknown;
        [Tooltip("World camera resolution set in Capture (World window > Video Source), e.g. 1280x720. Empty = not entered.")]
        public string worldCameraResolution = "";
        [Tooltip("World camera frame rate set in Capture (fps). 0 = not entered. The log also MEASURES it from the surface messages.")]
        public int worldCameraFps = 0;
        [Tooltip("Calibration method used in Capture (e.g. Screen Marker, Single Marker). Empty = not entered.")]
        public string calibrationMethod = "";

        [Header("Debug")]
        [Tooltip("Print the topic and field names of the first message of each kind to the Console (to check the format).")]
        public bool logFirstMessages = true;

        // ------------------------------------------------------------------ outputs

        public override string SourceName => isFake ? "PupilFake" : "Pupil";
        public PupilStatus Status { get; private set; } = PupilStatus.Off;
        public string CaptureVersion { get; private set; } = "";
        public string Endpoint => $"{host}:{port}";
        /// <summary>Share of samples at or above the confidence threshold over the last 5 s (0..1), NaN if none.</summary>
        public float ValidShare { get; private set; } = float.NaN;
        public float SamplesPerSecond { get; private set; }
        /// <summary>Eyes of the newest sample: "binocular", "mono eye0", "mono eye1" or "".</summary>
        public string LastEyes { get; private set; } = "";
        public bool BlinkActive { get; private set; }
        public int BlinkCount { get; private set; }
        public double PupilAccuracyDeg { get; private set; } = double.NaN;
        public double PupilPrecisionDeg { get; private set; } = double.NaN;
        public PupilClockSync FirstSync { get; private set; }
        public PupilClockSync LastSync { get; private set; }
        public int SyncCount { get; private set; }
        /// <summary>World camera rate measured from the surface message time stamps (fps), NaN if unknown.</summary>
        public double MeasuredWorldFps { get; private set; } = double.NaN;
        public long ReceivedSamples { get; private set; }
        public string UnknownSurfaces => string.Join(";", unknownSurfaces);
        /// <summary>True when connected to FakePupilServer (it answers "v" with a version that starts with "fake").</summary>
        public bool IsFake => isFake;
        /// <summary>Raised when the information for the meta file changed (version, sync, accuracy).</summary>
        public event Action InfoChanged;

        /// <summary>Local clock (s): high-resolution, thread-safe, independent of Unity's frame timing.</summary>
        public static double LocalNow => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        // ------------------------------------------------------------------ internal state

        private enum Phase { Idle, WaitSubPort, WaitVersion, WaitPlugin, Running }
        private Phase phase = Phase.Idle;
        private RequestSocket req;
        private SubscriberSocket sub;
        private bool netmqAcquired, isFake, requestPending, healthCheckPending;
        private double requestSentAt, nextAttemptAt, lastDataAt, nextHealthCheckAt, nextResyncAt, blinkOnsetAt;
        private string subPort = "";

        // Newest gaze point (see Consider).
        private bool hasLatest;
        private PupilRawSample latest;
        private double latestScore;
        private double lastCountedPupilTime = double.NegativeInfinity;

        private readonly Queue<(double t, bool ok)> recent = new Queue<(double, bool)>();   // last 5 s, for ValidShare
        private readonly List<PupilRawSample> rawQueue = new List<PupilRawSample>(512);
        private readonly List<float> latenciesMs = new List<float>(4096);
        private readonly Dictionary<string, long> eyeCounts = new Dictionary<string, long>();
        private readonly Dictionary<string, double> lastWorldTs = new Dictionary<string, double>();
        private readonly List<double> worldIntervals = new List<double>(256);
        private readonly HashSet<string> unknownSurfaces = new HashSet<string>();
        private readonly HashSet<string> dumpedTopics = new HashSet<string>();
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const int MaxRawQueue = 20000, MaxLatencies = 200000, MaxMessagesPerPoll = 1000;

        // ------------------------------------------------------------------ Unity

        private void Start()
        {
            SetStatus(PupilStatus.Connecting, "start");
            nextAttemptAt = 0;
        }

        private void Update()
        {
            Tick();
        }

        private void OnDisable() => Disconnect("disabled", PupilStatus.Off);
        private void OnApplicationQuit() => Disconnect("quit", PupilStatus.Off);

        // ------------------------------------------------------------------ gaze for DriverTwin

        public override bool TryGetGazeSample(out GazeSample sample)
        {
            sample = GazeSample.Invalid;
            if (!isActiveAndEnabled) return false;
            Tick();                                         // read what arrived since the last frame
            if (!hasLatest || phase != Phase.Running) return false;
            if (LocalNow - latest.localTime > maxSampleAge) return false;      // stream stalled
            if (latest.confidence < confidenceThreshold) return false;         // low confidence (blink, lost pupil)

            int i = latest.display;
            if (i < 0 || i >= surfaces.Count || !latest.onSurf)
            {
                // Valid data, but not on any configured monitor: OffScreen.
                sample = new GazeSample
                {
                    valid = true, onDisplay = false, confidence = latest.confidence, sourceTime = latest.pupilTime,
                    displayIndex = -1, displayPos = new Vector2((float)latest.x, (float)latest.y),
                };
                return true;
            }
            PupilSurface s = surfaces[i];
            var vp = new Vector2((float)latest.x, (float)latest.y);
            if (s.isZone)
            {
                sample = new GazeSample
                {
                    valid = true, onDisplay = true, confidence = latest.confidence, sourceTime = latest.pupilTime,
                    displayIndex = i, displayPos = vp, hasDirectZone = true, directZone = s.zone,
                    worldRay = s.camera != null ? s.camera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f)) : default,
                };
                return true;
            }
            if (s.camera == null) return false;
            sample = GazeSample.FromViewport(s.camera, vp, i, latest.confidence, latest.pupilTime);
            return true;
        }

        /// <summary>Moves the raw samples received so far into 'into' (used by DriveLogger).</summary>
        public void TakeRawSamples(List<PupilRawSample> into)
        {
            into.AddRange(rawQueue);
            rawQueue.Clear();
        }

        /// <summary>Median and 95th percentile of the estimated latency so far (ms); NaN if none.</summary>
        public void LatencyStats(out double medianMs, out double p95Ms, out int n)
        {
            n = latenciesMs.Count;
            medianMs = p95Ms = double.NaN;
            if (n == 0) return;
            var copy = new List<float>(latenciesMs);
            copy.Sort();
            medianMs = copy[n / 2];
            p95Ms = copy[Mathf.Min(n - 1, (int)(0.95 * n))];
        }

        /// <summary>Share of samples per eye setup so far, e.g. "binocular 0.92;mono eye0 0.08".</summary>
        public string EyeShares()
        {
            long total = 0;
            foreach (long c in eyeCounts.Values) total += c;
            if (total == 0) return "";
            var parts = new List<string>();
            foreach (var kv in eyeCounts) parts.Add($"{kv.Key} {(kv.Value / (double)total).ToString("F3", Inv)}");
            return string.Join(";", parts);
        }

        /// <summary>Clock drift between the first and the last sync (ms per minute), NaN if unknown.</summary>
        public double DriftMsPerMinute()
        {
            if (!FirstSync.ok || !LastSync.ok || SyncCount < 2) return double.NaN;
            double dt = LastSync.localTime - FirstSync.localTime;
            return dt > 1.0 ? (LastSync.offset - FirstSync.offset) * 1000.0 / (dt / 60.0) : double.NaN;
        }

        // ------------------------------------------------------------------ connection state machine

        private void Tick()
        {
            if (!isActiveAndEnabled) return;
            double now = LocalNow;
            try
            {
                switch (phase)
                {
                    case Phase.Idle:
                        if (now >= nextAttemptAt) BeginConnect();
                        break;
                    case Phase.WaitSubPort:
                        if (TryReply(out string portReply))
                        {
                            subPort = portReply.Trim();
                            Send("v");
                            phase = Phase.WaitVersion;
                        }
                        break;
                    case Phase.WaitVersion:
                        if (TryReply(out string version))
                        {
                            CaptureVersion = version.Trim();
                            isFake = CaptureVersion.StartsWith("fake", StringComparison.OrdinalIgnoreCase);
                            if (startBlinkDetector && subscribeBlinks)
                            {
                                SendStartPlugin("Blink_Detection");
                                phase = Phase.WaitPlugin;
                            }
                            else StartRunning();
                        }
                        break;
                    case Phase.WaitPlugin:
                        if (TryReply(out _)) StartRunning();
                        break;
                    case Phase.Running:
                        PollData(now);
                        RunningChecks(now);
                        break;
                }
                // A request that got no answer: Capture is not there (or stopped).
                if (requestPending && now - requestSentAt > requestTimeout)
                    Disconnect(phase == Phase.Running ? "Pupil Remote stopped answering" : "no answer from Pupil Remote at " + Endpoint,
                               PupilStatus.NotConnected);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Pupil] " + e.GetType().Name + ": " + e.Message);
                Disconnect("error: " + e.Message, PupilStatus.NotConnected);
            }
        }

        private void BeginConnect()
        {
            if (!netmqAcquired) { PupilNetMQ.Acquire(); netmqAcquired = true; }
            SetStatus(PupilStatus.Connecting, Endpoint);
            req = new RequestSocket();
            req.Options.Linger = TimeSpan.Zero;
            req.Connect($"tcp://{host}:{port}");
            Send("SUB_PORT");
            phase = Phase.WaitSubPort;
        }

        private void StartRunning()
        {
            sub = new SubscriberSocket();
            sub.Options.Linger = TimeSpan.Zero;
            sub.Connect($"tcp://{host}:{subPort}");
            sub.Subscribe("surfaces.");
            if (subscribeBlinks) sub.Subscribe("blinks");
            sub.Subscribe("logging.info");
            sub.Subscribe("notify.calibration.successful");
            sub.Subscribe("notify.calibration.failed");
            phase = Phase.Running;
            lastDataAt = LocalNow;                      // grace period before "No data"
            hasLatest = false;
            Debug.Log($"[Pupil] Connected to Pupil Capture {CaptureVersion} at {Endpoint} (data port {subPort}).");
            SetStatus(PupilStatus.NoData, "connected, waiting for surface data");
            SyncClock("connect");
            nextResyncAt = LocalNow + resyncInterval;
            InfoChanged?.Invoke();
        }

        private void RunningChecks(double now)
        {
            // Health check while no data comes: is Capture still answering?
            bool noData = now - lastDataAt > noDataAfter;
            if (noData && !requestPending && now >= nextHealthCheckAt)
            {
                Send("t");
                healthCheckPending = true;
                nextHealthCheckAt = now + retryInterval;
            }
            if (healthCheckPending && TryReply(out _)) healthCheckPending = false;

            // Periodic clock sync (only when no request is waiting).
            if (!requestPending && now >= nextResyncAt)
            {
                SyncClock("periodic");
                nextResyncAt = now + resyncInterval;
            }

            // Blink without offset: forget it after 2 s (our choice, safety only).
            if (BlinkActive && now - blinkOnsetAt > 2.0) BlinkActive = false;

            // Valid share and rate over the last 5 s.
            while (recent.Count > 0 && now - recent.Peek().t > 5.0) recent.Dequeue();
            int ok = 0;
            foreach (var r in recent) if (r.ok) ok++;
            ValidShare = recent.Count > 0 ? ok / (float)recent.Count : float.NaN;
            SamplesPerSecond = recent.Count / 5f;

            PupilStatus s = noData ? PupilStatus.NoData
                : (!float.IsNaN(ValidShare) && ValidShare < lowConfidenceShare ? PupilStatus.LowConfidence : PupilStatus.Connected);
            SetStatus(s, noData ? "no surface data (markers not seen, no surfaces defined, or not calibrated)" : "");
        }

        private void Disconnect(string reason, PupilStatus newStatus)
        {
            PupilNetMQ.CloseSocket(sub); sub = null;
            PupilNetMQ.CloseSocket(req); req = null;
            requestPending = healthCheckPending = false;
            hasLatest = false;
            BlinkActive = false;
            phase = Phase.Idle;
            nextAttemptAt = LocalNow + retryInterval;
            SetStatus(newStatus, reason);
            if (newStatus == PupilStatus.Off && netmqAcquired)
            {
                PupilNetMQ.Release();
                netmqAcquired = false;
            }
        }

        // ------------------------------------------------------------------ requests (Pupil Remote)

        private void Send(string command)
        {
            if (!req.TrySendFrame(TimeSpan.Zero, command)) throw new InvalidOperationException("could not send '" + command + "'");
            requestPending = true;
            requestSentAt = LocalNow;
        }

        /// <summary>Notification "start_plugin" through Pupil Remote (as hmd-eyes' RequestController.StartPlugin).</summary>
        private void SendStartPlugin(string pluginName)
        {
            var payload = new Dictionary<string, object> { { "subject", "start_plugin" }, { "name", pluginName } };
            var m = new NetMQMessage();
            m.Append("notify.start_plugin");
            m.Append(PupilMsgPack.Encode(payload));
            if (!req.TrySendMultipartMessage(TimeSpan.Zero, m)) throw new InvalidOperationException("could not send start_plugin");
            requestPending = true;
            requestSentAt = LocalNow;
        }

        /// <summary>Answer to the pending request, without waiting.</summary>
        private bool TryReply(out string reply)
        {
            reply = null;
            if (!requestPending || req == null) return false;
            if (!req.TryReceiveFrameString(TimeSpan.Zero, out reply)) return false;
            requestPending = false;
            return true;
        }

        /// <summary>
        /// Clock sync: asks "t" up to syncRequests times and keeps the shortest round trip.
        /// This WAITS for each answer (short: about 1 ms on the same PC), so it is only done when
        /// Capture has just answered. A timeout ends the connection (retried later).
        /// </summary>
        public PupilClockSync SyncClock(string label)
        {
            var result = new PupilClockSync { ok = false, label = label, roundTripMs = double.PositiveInfinity };
            if (req == null || requestPending || phase != Phase.Running) return result;
            TimeSpan wait = TimeSpan.FromSeconds(Mathf.Min(requestTimeout, 0.25f));
            for (int i = 0; i < Mathf.Max(1, syncRequests); i++)
            {
                double t0 = LocalNow;
                if (!req.TrySendFrame(wait, "t")) break;
                if (!req.TryReceiveFrameString(wait, out string answer))
                {
                    Disconnect("no answer to clock sync", PupilStatus.NotConnected);
                    return result;
                }
                double t1 = LocalNow;
                if (!double.TryParse(answer.Trim(), NumberStyles.Float, Inv, out double pupilT)) continue;
                double rtt = (t1 - t0) * 1000.0;
                result.requests++;
                if (rtt < result.roundTripMs)
                {
                    result.roundTripMs = rtt;
                    result.localTime = (t0 + t1) * 0.5;
                    result.offset = pupilT - result.localTime;
                    result.ok = true;
                }
            }
            if (!result.ok) return result;
            if (SyncCount == 0) FirstSync = result;
            LastSync = result;
            SyncCount++;
            Rec("PupilClockSync", label,
                $"offset_s={result.offset.ToString("F6", Inv)};round_trip_ms={result.roundTripMs.ToString("F3", Inv)};requests={result.requests}");
            InfoChanged?.Invoke();
            return result;
        }

        // ------------------------------------------------------------------ data (subscriber)

        private void PollData(double now)
        {
            if (sub == null) return;
            for (int n = 0; n < MaxMessagesPerPoll; n++)
            {
                var msg = new NetMQMessage();
                if (!sub.TryReceiveMultipartMessage(TimeSpan.Zero, ref msg)) break;
                if (msg.FrameCount < 2) continue;
                string topic = msg[0].ConvertToString();
                Dictionary<string, object> d;
                try { d = PupilMsgPack.Decode(msg[1].ToByteArray()) as Dictionary<string, object>; }
                catch (FormatException e) { Debug.LogWarning($"[Pupil] could not decode '{topic}': {e.Message}"); continue; }
                if (d == null) continue;
                DumpOnce(topic, d);

                if (topic.StartsWith("surfaces.", StringComparison.Ordinal)) HandleSurface(topic.Substring(9), d, now);
                else if (topic == "blinks") HandleBlink(d, now);
                else if (topic.StartsWith("logging.", StringComparison.Ordinal)) HandleLog(d);
                else if (topic.StartsWith("notify.calibration.", StringComparison.Ordinal))
                    Rec("PupilCalibration", topic.Substring("notify.calibration.".Length), "");
            }
        }

        private void HandleSurface(string name, Dictionary<string, object> d, double now)
        {
            lastDataAt = now;
            int index = surfaces.FindIndex(s => s != null && s.surfaceName == name);
            if (index < 0 && unknownSurfaces.Add(name))
                Debug.LogWarning($"[Pupil] Surface '{name}' is not in PupilGazeSource.surfaces: its gaze counts as off-screen.");

            // World camera frame rate from successive frames of the same surface (frames without
            // markers are missing, so the median interval is used).
            double worldTs = PupilMsgPack.GetDouble(d, "timestamp");
            if (lastWorldTs.TryGetValue(name, out double prev) && worldTs > prev)
            {
                if (worldIntervals.Count >= 600) worldIntervals.RemoveAt(0);
                worldIntervals.Add(worldTs - prev);
                if (worldIntervals.Count >= 10 && worldIntervals.Count % 10 == 0)
                {
                    var sorted = new List<double>(worldIntervals);
                    sorted.Sort();
                    MeasuredWorldFps = 1.0 / sorted[sorted.Count / 2];
                }
            }
            lastWorldTs[name] = worldTs;

            if (!d.TryGetValue("gaze_on_surfaces", out object g) || !(g is List<object> list)) return;
            double drive = session != null ? session.DriveTime : double.NaN;
            foreach (object o in list)
            {
                if (!(o is Dictionary<string, object> item)) continue;
                if (!PupilMsgPack.GetXY(item, "norm_pos", out double x, out double y)) continue;
                var r = new PupilRawSample
                {
                    localTime = now,
                    driveTime = drive,
                    pupilTime = PupilMsgPack.GetDouble(item, "timestamp"),
                    worldFrameTime = worldTs,
                    surface = name,
                    display = index,
                    x = x, y = y,
                    confidence = (float)PupilMsgPack.GetDouble(item, "confidence", 0.0),
                    onSurf = PupilMsgPack.GetBool(item, "on_surf"),
                    gazeTopic = PupilMsgPack.GetString(item, "topic"),
                };
                r.latencyMs = LastSync.ok ? (now - (r.pupilTime - LastSync.offset)) * 1000.0 : double.NaN;

                if (rawQueue.Count < MaxRawQueue) rawQueue.Add(r);
                Consider(r);

                // Statistics once per gaze point (the same point comes once per surface).
                if (r.pupilTime > lastCountedPupilTime)
                {
                    lastCountedPupilTime = r.pupilTime;
                    ReceivedSamples++;
                    recent.Enqueue((now, r.confidence >= confidenceThreshold));
                    string eyes = EyesOf(r.gazeTopic);
                    eyeCounts[eyes] = eyeCounts.TryGetValue(eyes, out long count) ? count + 1 : 1;
                    if (!double.IsNaN(r.latencyMs) && latenciesMs.Count < MaxLatencies) latenciesMs.Add((float)r.latencyMs);
                }
            }
        }

        /// <summary>
        /// Keeps the newest gaze point. The same point arrives once per surface; then the one ON its
        /// surface wins, and if it is on two (overlap at a bezel), the one further inside (our choice).
        /// </summary>
        private void Consider(PupilRawSample r)
        {
            double inside = Math.Min(Math.Min(r.x, 1 - r.x), Math.Min(r.y, 1 - r.y));
            double score = r.onSurf && r.display >= 0 ? inside : -1.0;
            if (!hasLatest || r.pupilTime > latest.pupilTime || (r.pupilTime == latest.pupilTime && score > latestScore))
            {
                latest = r;
                latestScore = score;
                hasLatest = true;
                LastEyes = EyesOf(r.gazeTopic);
            }
        }

        /// <summary>Gaze topic -> eyes used. Pupil: gaze.3d.01. = both eyes, gaze.3d.0. / .1. = one eye.</summary>
        private static string EyesOf(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return "unknown";
            string[] p = topic.Split('.');
            if (p.Length < 3) return "unknown";
            string mapper = p[1];                       // 3d or 2d
            return p[2] switch
            {
                "01" => $"binocular {mapper}",
                "0" => $"mono eye0 {mapper}",
                "1" => $"mono eye1 {mapper}",
                _ => "unknown",
            };
        }

        private void HandleBlink(Dictionary<string, object> d, double now)
        {
            string type = PupilMsgPack.GetString(d, "type");
            if (type == "onset")
            {
                if (!BlinkActive) BlinkCount++;
                BlinkActive = true;
                blinkOnsetAt = now;
            }
            else if (type == "offset") BlinkActive = false;
            Rec("PupilBlink", type,
                $"pupil_ts={PupilMsgPack.GetDouble(d, "timestamp").ToString("F4", Inv)};" +
                $"confidence={PupilMsgPack.GetDouble(d, "confidence").ToString("F2", Inv)}");
        }

        /// <summary>Catches the Accuracy Visualizer's log lines "Angular accuracy: 1.234 degrees" (TO VERIFY in the lab).</summary>
        private void HandleLog(Dictionary<string, object> d)
        {
            string m = PupilMsgPack.GetString(d, "msg");
            if (TryNumberAfter(m, "Angular accuracy:", out double acc))
            {
                PupilAccuracyDeg = acc;
                Rec("PupilAccuracy", "accuracy", "deg=" + acc.ToString("F3", Inv));
                InfoChanged?.Invoke();
            }
            else if (TryNumberAfter(m, "Angular precision:", out double prec))
            {
                PupilPrecisionDeg = prec;
                Rec("PupilAccuracy", "precision", "deg=" + prec.ToString("F3", Inv));
                InfoChanged?.Invoke();
            }
        }

        private static bool TryNumberAfter(string text, string label, out double value)
        {
            value = double.NaN;
            if (string.IsNullOrEmpty(text)) return false;
            int i = text.IndexOf(label, StringComparison.Ordinal);
            if (i < 0) return false;
            string rest = text.Substring(i + label.Length).TrimStart();
            int end = 0;
            while (end < rest.Length && (char.IsDigit(rest[end]) || rest[end] == '.' || rest[end] == '-')) end++;
            return double.TryParse(rest.Substring(0, end), NumberStyles.Float, Inv, out value);
        }

        private void DumpOnce(string topic, Dictionary<string, object> d)
        {
            if (!logFirstMessages) return;
            string kind = topic.StartsWith("surfaces.", StringComparison.Ordinal) ? "surfaces" : topic;
            if (!dumpedTopics.Add(kind)) return;
            string fields = string.Join(", ", d.Keys);
            if (d.TryGetValue("gaze_on_surfaces", out object g) && g is List<object> l && l.Count > 0 && l[0] is Dictionary<string, object> first)
                fields += "  | gaze item: " + string.Join(", ", first.Keys) + $"  | topic '{PupilMsgPack.GetString(first, "topic")}'";
            Debug.Log($"[Pupil] first '{topic}' message. Fields: {fields}");
        }

        // ------------------------------------------------------------------ status

        private void SetStatus(PupilStatus s, string detail)
        {
            if (s == Status) return;
            PupilStatus old = Status;
            Status = s;
            Rec("TrackerStatus", s.ToString(), $"from={old}" + (string.IsNullOrEmpty(detail) ? "" : ";" + detail.Replace(',', ' ')));
            if (s == PupilStatus.NotConnected && old != PupilStatus.NotConnected)
                Debug.LogWarning($"[Pupil] Not connected ({detail}). Retrying every {retryInterval:F0} s. Is Pupil Capture running?");
        }

        private void Rec(string type, string target, string detail)
        {
            if (events != null) events.Record(type, target, detail);
        }
    }
}

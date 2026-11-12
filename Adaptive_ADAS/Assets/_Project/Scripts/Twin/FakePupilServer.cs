// FakePupilServer.cs  (on DriveSystems)  HOME TESTING ONLY
// Pretends to be Pupil Capture, driven by the MOUSE, so the whole eye-tracker path
// (network, message decoding, clock sync, latency, logging, overlay) can be tested on a laptop
// without the tracker and without installing anything.
//
// It speaks the same protocol as Pupil Capture (as far as PupilGazeSource uses it):
//   - "Pupil Remote" (ZeroMQ reply socket) on 'port' (default 50021, NOT 50020, so it never
//     clashes with a real Pupil Capture on the same PC). Answers SUB_PORT, v ("fake-..."), t
//     (fake Pupil clock = local clock + 'clockOffset'), and "Message forwarded." to notifications.
//   - a data stream (ZeroMQ publisher) with "surfaces.<surfaceName>" messages at the world-camera
//     rate, each holding the gaze points (at the eye-camera rate) since the previous frame, plus
//     "blinks", "logging.info" and calibration notifications on keys.
//
// Keys (only while the fake is running):
//   B  blink (confidence 0 for 'blinkSeconds', with blink onset / offset messages)
//   M  markers lost on / off (no surface messages at all: tests the "No data" status)
//   N  low confidence on / off (confidence 0.3: tests the "Low confidence" status)
//   K  fake calibration: "calibration successful" + fake accuracy / precision log lines
//
// The reply socket runs on its own thread (so the client's short blocking clock sync can never
// wait for Unity's main thread); the data stream is sent from the main thread.
// All numbers below are OUR CHOICES for a plausible test stream, not properties of the real tracker.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AdaptiveADAS.Twin
{
    [DefaultExecutionOrder(-70)]
    public class FakePupilServer : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        [Tooltip("The mouse position in this camera's view is the fake gaze on the surface.")]
        public Camera targetCamera;

        [Header("Network")]
        [Tooltip("Fake Pupil Remote port. 50021 so it never clashes with a real Pupil Capture (50020).")]
        public int port = 50021;

        [Header("Fake stream")]
        public string surfaceName = "monitor_center";
        [Tooltip("World camera frame rate (fps): one surface message per frame. Try 30 and 60.")]
        public float worldFps = 30f;
        [Tooltip("Eye camera rate (Hz): gaze points per second.")]
        public float eyeRateHz = 120f;
        [Tooltip("Off = monocular (topic gaze.3d.0.), on = binocular (gaze.3d.01.).")]
        public bool binocular = false;
        [Tooltip("Typical confidence.")]
        [Range(0f, 1f)] public float confidence = 0.9f;
        [Tooltip("Random spread of the confidence (+/-).")]
        [Range(0f, 0.5f)] public float confidenceNoise = 0.05f;
        [Tooltip("Random spread of the gaze position (fraction of the screen, +/-).")]
        public float positionNoise = 0.003f;
        [Tooltip("Extra delay of the gaze time stamps (ms), to see it in the latency estimate.")]
        public float extraLatencyMs = 10f;
        [Tooltip("Fake Pupil clock = local clock + this (s). Makes the clock sync visible.")]
        public double clockOffset = 1000.0;
        [Tooltip("Duration of a blink (key B) (s).")]
        public float blinkSeconds = 0.15f;
        [Tooltip("Automatic blink every N s (0 = only on key B).")]
        public float autoBlinkEvery = 0f;
        [Tooltip("Hide the system cursor while the game window has focus.")]
        public bool hideCursor = true;

        [Header("Fake calibration result (key K)")]
        public float fakeAccuracyDeg = 1.5f;
        public float fakePrecisionDeg = 0.1f;

        // Shown in the overlay (FULL).
        public bool MarkersLost { get; private set; }
        public bool LowConfidence { get; private set; }
        public string StateText => $"fake :{port}  {worldFps:F0} fps  {(binocular ? "bino" : "mono")}" +
                                   (MarkersLost ? "  MARKERS LOST" : "") + (LowConfidence ? "  LOW CONF" : "") +
                                   (blinkUntil > Now ? "  BLINK" : "");

        private PublisherSocket pub;
        private Thread replyThread;
        private volatile bool running;
        private volatile int pubPort;
        private bool netmqAcquired;
        private double lastGazeTime, nextWorldFrame, blinkUntil = double.NegativeInfinity, nextAutoBlink;
        private readonly List<Dictionary<string, object>> pending = new List<Dictionary<string, object>>();
        private readonly System.Random rng = new System.Random(1);
        private bool blinkOpen;

        private double Now => PupilGazeSource.LocalNow + clockOffset;   // fake Pupil time

        private void OnEnable()
        {
            try
            {
                PupilNetMQ.Acquire();
                netmqAcquired = true;
                pub = new PublisherSocket();
                pub.Options.Linger = TimeSpan.Zero;
                pubPort = pub.BindRandomPort("tcp://127.0.0.1");
                running = true;
                replyThread = new Thread(ReplyLoop) { IsBackground = true, Name = "FakePupilRemote" };
                replyThread.Start();
                lastGazeTime = Now;
                nextWorldFrame = Now;
                nextAutoBlink = Now + autoBlinkEvery;
                Debug.Log($"[FakePupil] Fake Pupil Capture on 127.0.0.1:{port} (data port {pubPort}). " +
                          "Keys: B blink, M markers lost, N low confidence, K fake calibration.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FakePupil] Could not start: {e.Message}");
                Stop();
            }
        }

        private void OnDisable() => Stop();
        private void OnApplicationQuit() => Stop();

        private void Stop()
        {
            running = false;
            if (replyThread != null && !replyThread.Join(1000))
                Debug.LogWarning("[FakePupil] reply thread did not stop in time.");
            replyThread = null;
            PupilNetMQ.CloseSocket(pub);
            pub = null;
            if (netmqAcquired) { PupilNetMQ.Release(); netmqAcquired = false; }
        }

        /// <summary>Background thread: the fake Pupil Remote (request -> reply).</summary>
        private void ReplyLoop()
        {
            ResponseSocket rep = null;
            try
            {
                rep = new ResponseSocket();
                rep.Options.Linger = TimeSpan.Zero;
                rep.Bind($"tcp://127.0.0.1:{port}");
                while (running)
                {
                    var msg = new NetMQMessage();
                    if (!rep.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(100), ref msg)) continue;
                    string cmd = msg.FrameCount > 0 ? msg[0].ConvertToString() : "";
                    string answer;
                    if (msg.FrameCount > 1) answer = "Message forwarded.";            // a notification
                    else if (cmd == "SUB_PORT") answer = pubPort.ToString(CultureInfo.InvariantCulture);
                    else if (cmd == "PUB_PORT") answer = "0";
                    else if (cmd == "v") answer = "fake-1.0 (Unity FakePupilServer)";
                    else if (cmd.StartsWith("t", StringComparison.Ordinal)) answer = Now.ToString("R", CultureInfo.InvariantCulture);
                    else if (cmd.Length > 0 && "RrCc".IndexOf(cmd[0]) >= 0) answer = "OK";
                    else answer = "Unknown command.";
                    rep.TrySendFrame(TimeSpan.FromMilliseconds(100), answer);
                }
            }
            catch (Exception e)
            {
                // Debug.Log may be called from other threads in Unity.
                Debug.LogWarning($"[FakePupil] Pupil Remote thread stopped: {e.Message} (port {port} in use?)");
            }
            finally
            {
                PupilNetMQ.CloseSocket(rep);
            }
        }

        private void Update()
        {
            if (pub == null) return;
            if (hideCursor && Application.isFocused) Cursor.visible = false;
            Keyboard kb = Keyboard.current;
            double now = Now;
            if (kb != null)
            {
                if (kb.bKey.wasPressedThisFrame) blinkUntil = now + blinkSeconds;
                if (kb.mKey.wasPressedThisFrame) MarkersLost = !MarkersLost;
                if (kb.nKey.wasPressedThisFrame) LowConfidence = !LowConfidence;
                if (kb.kKey.wasPressedThisFrame) FakeCalibration();
            }
            if (autoBlinkEvery > 0f && now >= nextAutoBlink)
            {
                blinkUntil = now + blinkSeconds;
                nextAutoBlink = now + autoBlinkEvery;
            }
            UpdateBlinkMessages(now);

            // Gaze points at the eye-camera rate, all at the current mouse position (+ noise).
            Vector2 vp = MouseViewport();
            double step = 1.0 / Mathf.Max(1f, eyeRateHz);
            while (lastGazeTime + step <= now)
            {
                lastGazeTime += step;
                pending.Add(GazePoint(vp, lastGazeTime - extraLatencyMs / 1000.0, lastGazeTime));
            }

            // One surface message per world-camera frame.
            if (now >= nextWorldFrame)
            {
                nextWorldFrame = Math.Max(nextWorldFrame + 1.0 / Mathf.Max(1f, worldFps), now - 0.5);
                if (!MarkersLost) Publish("surfaces." + surfaceName, new Dictionary<string, object>
                {
                    { "topic", "surfaces." + surfaceName },
                    { "name", surfaceName },
                    { "gaze_on_surfaces", new List<object>(pending) },
                    { "fixations_on_surfaces", new List<object>() },
                    { "timestamp", now },
                });
                pending.Clear();
            }
        }

        private Dictionary<string, object> GazePoint(Vector2 vp, double stamp, double captureTime)
        {
            bool blinking = captureTime < blinkUntil;
            double conf = blinking ? 0.0 : LowConfidence ? 0.3 : confidence + (rng.NextDouble() * 2 - 1) * confidenceNoise;
            conf = Math.Max(0.0, Math.Min(1.0, conf));
            double x = vp.x + (rng.NextDouble() * 2 - 1) * positionNoise;
            double y = vp.y + (rng.NextDouble() * 2 - 1) * positionNoise;
            string gazeTopic = binocular ? "gaze.3d.01." : "gaze.3d.0.";
            return new Dictionary<string, object>
            {
                { "topic", gazeTopic + "_on_surface" },
                { "norm_pos", new List<object> { x, y } },
                { "confidence", conf },
                { "on_surf", x >= 0 && x <= 1 && y >= 0 && y <= 1 },
                { "base_data", new List<object> { gazeTopic, stamp } },
                { "timestamp", stamp },
            };
        }

        private void UpdateBlinkMessages(double now)
        {
            bool blinking = now < blinkUntil;
            if (blinking == blinkOpen) return;
            blinkOpen = blinking;
            Publish("blinks", new Dictionary<string, object>
            {
                { "topic", "blinks" }, { "type", blinking ? "onset" : "offset" },
                { "confidence", 0.8 }, { "timestamp", now }, { "record", true },
            });
        }

        private void FakeCalibration()
        {
            Publish("notify.calibration.successful", new Dictionary<string, object>
                { { "subject", "calibration.successful" }, { "timestamp", Now } });
            Publish("logging.info", new Dictionary<string, object>
                { { "topic", "logging.info" }, { "msg", $"Angular accuracy: {fakeAccuracyDeg.ToString("F3", CultureInfo.InvariantCulture)} degrees (FAKE)" } });
            Publish("logging.info", new Dictionary<string, object>
                { { "topic", "logging.info" }, { "msg", $"Angular precision: {fakePrecisionDeg.ToString("F3", CultureInfo.InvariantCulture)} degrees (FAKE)" } });
            Debug.Log("[FakePupil] Sent a fake calibration result.");
        }

        private void Publish(string topic, Dictionary<string, object> payload)
        {
            try { pub.SendMoreFrame(topic).SendFrame(PupilMsgPack.Encode(payload)); }
            catch (Exception e) { Debug.LogWarning($"[FakePupil] send failed: {e.Message}"); }
        }

        private Vector2 MouseViewport()
        {
            if (Mouse.current == null || targetCamera == null) return new Vector2(0.5f, 0.5f);
            Vector3 v = targetCamera.ScreenToViewportPoint(Mouse.current.position.ReadValue());
            return new Vector2(v.x, v.y);
        }
    }
}

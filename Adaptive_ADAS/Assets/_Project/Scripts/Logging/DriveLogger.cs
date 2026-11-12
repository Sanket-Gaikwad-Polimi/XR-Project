// DriveLogger.cs
// Writes the data of one drive, one row per physics step (50 Hz with the
// project's Fixed Timestep of 0.02 s).
//
// Where: <repository root>/DriveLogs/   i.e.  D:\XR Project\DriveLogs\
//   The path is built RELATIVE to the Unity project (Application.dataPath is
//   ...\Adaptive_ADAS\Assets), so nothing is hard-coded. The DriveLogs folder is
//   git-ignored by the .gitignore at the repository root: participant data must
//   never be pushed to GitHub.
//
// Files per drive (same time stamp in the name):
//   drive_YYYYMMDD_HHMMSS.csv          time series, one row per physics step
//   drive_YYYYMMDD_HHMMSS_events.csv   one row per event (hazard onset, collision, reset, ...)
//   drive_YYYYMMDD_HHMMSS_meta.csv     settings of this drive (key,value). Rewritten when
//                                      the hazard starts or is skipped, when the driver
//                                      first looks at the braking lead car, when the eye tracker
//                                      reports new information (version, clock sync, accuracy)
//                                      and at the end of the road, so it also contains the hazard
//                                      outcome, the criticality at onset, the first-fixation result
//                                      and the eye-tracker summary.
//   drive_YYYYMMDD_HHMMSS_gaze.csv     ONLY with the Pupil eye tracker (or its fake): every gaze point
//                                      as received from Pupil Capture (raw, before any rule).
//
// The "events" column of the main CSV lists the events recorded since the previous
// row, as Type(target) separated by "|", e.g. "HazardOnset(H1_LeadBrake)".
// Collisions are detected by the physics engine AFTER the scripts of a step have run,
// so they appear in the row of the NEXT step (0.02 s later). The events CSV has the
// exact step.
//
// Numbers are written with a dot as decimal separator (InvariantCulture). Without
// this, a Windows PC set to Italian would write commas and break the CSV.
// Empty cell = value not defined (e.g. TTC when the lead car is not ahead in our lane).
//
// [DefaultExecutionOrder(200)] = runs after the cars (0) and the twin (100).

using System;
using System.Globalization;
using System.IO;
using System.Text;
using AdaptiveADAS.Experiment;
using AdaptiveADAS.HMI;
using AdaptiveADAS.Twin;
using AdaptiveADAS.Vehicle;
using UnityEngine;

namespace AdaptiveADAS.Logging
{
    [DefaultExecutionOrder(200)]
    public class DriveLogger : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public DriveSession session;
        public DriveEvents events;
        public VehicleTwin twin;
        public DriverInput driverInput;
        public EgoCarController egoCar;
        public LeadCarController leadCar;
        public TestAssists testAssists;
        public DriverViewSettings driverView;
        public InteriorToggle interior;
        public DriverTwin driverTwin;
        [Tooltip("Pupil eye tracker (raw gaze file and tracker summary). Ignored when it is switched off.")]
        public PupilGazeSource pupil;

        [Header("Output")]
        [Tooltip("Log folder, relative to the Unity project folder (the folder that contains Assets). " +
                 "'../DriveLogs' = D:\\XR Project\\DriveLogs")]
        public string logFolderRelative = "../DriveLogs";
        [Tooltip("Turn logging off for quick tests.")]
        public bool loggingEnabled = true;
        [Tooltip("Write buffered rows to disk every N seconds (a crash loses at most this much).")]
        public float flushIntervalSeconds = 1f;

        /// <summary>Full path of the CSV being written (shown in the debug overlay).</summary>
        public string CurrentFilePath { get; private set; } = "";
        public long RowsWritten { get; private set; }

        private StreamWriter writer, eventWriter, gazeWriter;
        private readonly System.Collections.Generic.List<PupilRawSample> rawSamples = new System.Collections.Generic.List<PupilRawSample>(512);
        private bool metaDirty;
        private string metaPath;
        private string startLocalTime;
        private float timeSinceFlush;
        private readonly StringBuilder row = new StringBuilder(640);
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private const string Header =
            "t_s,step,events,hazard_id," +
            "ego_x_m,ego_z_m,ego_speed_mps,ego_accel_mps2,ego_yaw_deg,lane_index,lane_offset_m," +
            "throttle,brake,steer,throttle_raw,brake_raw,steer_raw,throttle_applied,brake_applied,steer_angle_deg," +
            "input_device,gear,cruise_on,cruise_set_kmh," +
            "lead_z_m,lead_speed_mps,lead_state,lead_brake_lights_on,longitudinal_gap_m,lateral_overlap,lead_relevant," +
            "gap_m,closing_speed_mps,ttc_s,time_headway_s," +
            "collision_with,lead_collision_events,other_collision_events," +
            "gaze_x,gaze_y,gaze_zone,glance_duration_s,buffer_s,attention_state,eyes_off_road_s,hazard_seen,interior_on," +
            "gaze_source,gaze_valid,gaze_confidence,gaze_az_deg,gaze_el_deg,gaze_display,gaze_on_display,tracker_status,blink";

        private const string GazeHeader =
            "local_time_s,drive_time_s,pupil_time_s,world_frame_time_s,surface,display,norm_x,norm_y,confidence,on_surf,gaze_topic,latency_ms_est";

        private bool PupilInUse => pupil != null && pupil.enabled;

        private void Start()
        {
            if (!loggingEnabled) return;

            // Application.dataPath = ...\Adaptive_ADAS\Assets -> its parent is the Unity project folder.
            string projectFolder = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.GetFullPath(Path.Combine(projectFolder, logFolderRelative));
            Directory.CreateDirectory(folder);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv);
            startLocalTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", Inv);
            CurrentFilePath = Path.Combine(folder, $"drive_{stamp}.csv");

            writer = new StreamWriter(CurrentFilePath, false, new UTF8Encoding(false));
            writer.WriteLine(Header);

            eventWriter = new StreamWriter(Path.Combine(folder, $"drive_{stamp}_events.csv"), false, new UTF8Encoding(false));
            eventWriter.WriteLine("t_s,step,event,target,detail");

            if (PupilInUse)
            {
                gazeWriter = new StreamWriter(Path.Combine(folder, $"drive_{stamp}_gaze.csv"), false, new UTF8Encoding(false));
                gazeWriter.WriteLine(GazeHeader);
                pupil.InfoChanged += OnPupilInfoChanged;
            }

            metaPath = Path.Combine(folder, $"drive_{stamp}_meta.csv");
            WriteMetaFile(metaPath);
            Debug.Log($"[DriveLogger] Logging to {CurrentFilePath}");
        }

        private void FixedUpdate()
        {
            if (writer == null) return;

            // ---- Events since the last row ----
            string eventsCell = "", collisionWith = "";
            if (events != null)
            {
                var sb = new StringBuilder();
                bool hazardOutcomeChanged = false;
                foreach (DriveEvent e in events.TakePending())
                {
                    if (e.type == "HazardOnset" || e.type == "HazardSkipped" || e.type == "HazardFirstFixation")
                        hazardOutcomeChanged = true;
                    eventWriter.WriteLine($"{e.time.ToString("F3", Inv)},{e.step.ToString(Inv)},{e.type},{e.target},{e.detail}");
                    if (sb.Length > 0) sb.Append('|');
                    sb.Append(e.type);
                    if (!string.IsNullOrEmpty(e.target)) sb.Append('(').Append(e.target).Append(')');
                    if (e.type == "Collision") collisionWith = e.target;
                }
                eventsCell = sb.ToString();
                if (hazardOutcomeChanged) metaDirty = true;   // add outcome + criticality
            }
            if (metaDirty || (session != null && session.DriveFinished))   // also the final summary at the end of the road
            {
                WriteMetaFile(metaPath);
                metaDirty = false;
            }
            WriteRawGaze();

            row.Clear();
            Add(session != null ? session.DriveTime : Time.fixedTimeAsDouble, "F3");
            Add(session != null ? session.StepCount : 0);
            AddText(eventsCell);
            AddText(leadCar.HazardActive ? leadCar.hazardId : "");
            Add(twin.EgoX, "F3"); Add(twin.EgoZ, "F3");
            Add(twin.EgoSpeed, "F3"); Add(twin.EgoAccel, "F3"); Add(twin.EgoYawDeg, "F2");
            Add(twin.LaneIndex); Add(twin.LaneOffset, "F3");
            Add(driverInput.Throttle, "F3"); Add(driverInput.Brake, "F3"); Add(driverInput.Steer, "F3");
            Add(driverInput.RawThrottle, "F3"); Add(driverInput.RawBrake, "F3"); Add(driverInput.RawSteer, "F3");
            Add(egoCar.AppliedThrottle, "F3");
            Add(egoCar.AppliedBrake, "F3");
            Add(egoCar.SteerAngleDeg, "F3");
            AddText(driverInput.ActiveDevice);
            AddText(egoCar.Gear);
            bool cruiseOn = testAssists != null && testAssists.CruiseOn;
            Add(cruiseOn ? 1 : 0);
            Add(cruiseOn ? testAssists.CruiseSetSpeedKmh : float.NaN, "F0");
            Add(twin.LeadZ, "F3"); Add(twin.LeadSpeed, "F3");
            AddText(leadCar.State.ToString());
            Add(leadCar.BrakeLightsOn ? 1 : 0);
            Add(twin.LongitudinalGap, "F3");
            Add(twin.LateralOverlap ? 1 : 0);
            Add(twin.LeadRelevant ? 1 : 0);
            Add(twin.Gap, "F3"); Add(twin.ClosingSpeed, "F3");
            Add(twin.TTC, "F3"); Add(twin.TimeHeadway, "F3");
            AddText(collisionWith);
            Add(egoCar.LeadCollisionEvents);
            Add(egoCar.OtherCollisionEvents);
            // ---- Driver twin (Step 3): gaze in viewport coordinates (0..1, origin bottom-left;
            // empty = no valid gaze sample), zone, attention buffer ----
            if (driverTwin != null)
            {
                Add(driverTwin.GazeViewport.x, "F4"); Add(driverTwin.GazeViewport.y, "F4");
                AddText(driverTwin.Zone.ToString());
                Add(driverTwin.GlanceDuration, "F3");
                Add(driverTwin.Buffer, "F3");
                AddText(driverTwin.State.ToString());
                Add(driverTwin.EyesOffRoad, "F3");
                Add(driverTwin.HazardSeen ? 1 : 0);
            }
            else
            {
                for (int i = 0; i < 8; i++) AddText("");
            }
            Add(interior != null && interior.InteriorOn ? 1 : 0);
            // ---- Gaze source and tracker (eye-tracker step). Azimuth/elevation: gaze direction relative
            // to straight ahead (deg; + = right / up). Display: index of the monitor (Pupil surface). ----
            if (driverTwin != null)
            {
                GazeSample g = driverTwin.Sample;
                AddText(driverTwin.GazeSourceName.Replace(',', ' '));
                Add(driverTwin.GazeValid ? 1 : 0);
                Add(driverTwin.GazeValid ? g.confidence : float.NaN, "F3");
                Add(driverTwin.GazeAzimuthDeg, "F2");
                Add(driverTwin.GazeElevationDeg, "F2");
                if (driverTwin.GazeValid && g.displayIndex >= 0) Add(g.displayIndex); else AddText("");
                if (driverTwin.GazeValid) Add(g.onDisplay ? 1 : 0); else AddText("");
            }
            else
            {
                for (int i = 0; i < 7; i++) AddText("");
            }
            bool tracker = PupilInUse && driverTwin != null && driverTwin.gazeSource == pupil;
            AddText(tracker ? pupil.Status.ToString() : "");
            if (tracker) Add(pupil.BlinkActive ? 1 : 0, last: true); else AddText("", last: true);

            writer.WriteLine(row.ToString());
            RowsWritten++;

            if (session != null && session.DriveFinished) { CloseFile(); return; }

            timeSinceFlush += Time.fixedDeltaTime;
            if (timeSinceFlush >= flushIntervalSeconds)
            {
                writer.Flush();
                eventWriter.Flush();
                gazeWriter?.Flush();
                timeSinceFlush = 0f;
            }
        }

        private void OnPupilInfoChanged() => metaDirty = true;

        /// <summary>Raw gaze points received from Pupil since the last step -> drive_..._gaze.csv.</summary>
        private void WriteRawGaze()
        {
            if (gazeWriter == null || (object)pupil == null) return;
            rawSamples.Clear();
            pupil.TakeRawSamples(rawSamples);
            foreach (PupilRawSample r in rawSamples)
            {
                row.Clear();
                Add(r.localTime, "F6"); Add(r.driveTime, "F3"); Add(r.pupilTime, "F6"); Add(r.worldFrameTime, "F6");
                AddText((r.surface ?? "").Replace(',', ' ')); Add(r.display);
                Add(r.x, "F5"); Add(r.y, "F5"); Add(r.confidence, "F3"); Add(r.onSurf ? 1 : 0);
                AddText((r.gazeTopic ?? "").Replace(',', ' ')); Add(r.latencyMs, "F2", last: true);
                gazeWriter.WriteLine(row.ToString());
            }
        }

        // ---- small helpers that build one CSV row ----
        private void Add(double v, string format, bool last = false)
        {
            // Infinity / NaN -> empty cell ("not defined").
            if (!double.IsNaN(v) && !double.IsInfinity(v)) row.Append(v.ToString(format, Inv));
            if (!last) row.Append(',');
        }
        private void Add(float v, string format, bool last = false) => Add((double)v, format, last);
        private void Add(long v, bool last = false)
        {
            row.Append(v.ToString(Inv));
            if (!last) row.Append(',');
        }
        private void AddText(string s, bool last = false)
        {
            row.Append(s);
            if (!last) row.Append(',');
        }

        private void WriteMetaFile(string path)
        {
            using var meta = new StreamWriter(path, false, new UTF8Encoding(false));
            void W(string key, string value) => meta.WriteLine($"{key},{value}");
            string F(float v) => float.IsNaN(v) || float.IsInfinity(v) ? "" : v.ToString(Inv);

            W("key", "value");
            W("start_local_time", startLocalTime);
            W("meta_written_at_drive_time_s", session != null ? session.DriveTime.ToString("F2", Inv) : "");
            W("unity_version", Application.unityVersion);
            W("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            W("study_mode", session != null && session.studyMode ? "True" : "False");
            W("fixed_timestep_s", Time.fixedDeltaTime.ToString("F4", Inv));
            W("speed_limit_kmh", F(session != null ? session.speedLimitKmh : float.NaN));
            W("driver_instruction", session != null ? session.driverInstruction.Replace(',', ';') : "");
            W("hazard_id", leadCar.hazardId);
            W("lead_cruise_speed_kmh", F(leadCar.cruiseSpeedKmh));
            W("planned_time_window_s", F(leadCar.brakeWindowStart) + "-" + F(leadCar.brakeWindowEnd));
            W("planned_time_s", leadCar.PlannedBrakeTime.ToString("F3", Inv));
            W("trigger_require_same_lane", leadCar.requireSameLane ? "True" : "False");
            W("trigger_thw_window_s", F(leadCar.minTimeHeadway) + "-" + F(leadCar.maxTimeHeadway));
            W("trigger_speed_window_kmh", F(leadCar.minEgoSpeedKmh) + "-" + F(leadCar.maxEgoSpeedKmh));
            W("trigger_limit_closing_speed", leadCar.limitClosingSpeed ? "True" : "False");
            W("trigger_max_abs_closing_mps", F(leadCar.maxAbsClosingSpeed));
            W("trigger_hold_time_s", F(leadCar.conditionsHoldTime));
            W("trigger_max_wait_s", F(leadCar.maxWaitSeconds));
            W("lead_brake_decel_mps2", F(leadCar.brakeDecel));
            W("lead_decel_ramp_s", F(leadCar.decelRampTime));
            // Outcome (pending until the hazard starts or is skipped; rewritten then).
            W("hazard_outcome", leadCar.Outcome);
            W("hazard_onset_time_s", double.IsNaN(leadCar.BrakeOnsetTime) ? "" : leadCar.BrakeOnsetTime.ToString("F3", Inv));
            W("hazard_waited_s", F(leadCar.WaitedSeconds));
            W("onset_ego_speed_kmh", F(leadCar.OnsetEgoSpeedKmh));
            W("onset_lead_speed_kmh", F(leadCar.OnsetLeadSpeedKmh));
            W("onset_gap_m", F(leadCar.OnsetGap));
            W("onset_time_headway_s", F(leadCar.OnsetTimeHeadway));
            W("onset_ttc_s", F(leadCar.OnsetTTC));
            W("onset_closing_speed_mps", F(leadCar.OnsetClosingSpeed));
            W("random_seed", leadCar.UsedSeed.ToString(Inv));
            W("ego_mass_kg", F(egoCar.massKg));
            W("ego_max_brake_decel_mps2", F(egoCar.maxBrakeDecel));
            W("ego_max_brake_decel_note", "brake only; engine braking and air drag come on top");
            W("ego_brake_build_up_s", F(egoCar.brakeBuildUpTime));
            W("ego_engine_brake_force_n", F(egoCar.engineBrakeForce));
            W("ego_max_steer_angle_deg", F(egoCar.maxSteerAngle));
            W("ego_max_lateral_accel_mps2", F(egoCar.maxLateralAccel));
            W("ego_forward_grip", F(egoCar.forwardGrip));
            W("ego_sideways_grip", F(egoCar.sidewaysGrip));
            W("input_steer_rate", F(driverInput.steerRate));
            W("input_steer_return_rate", F(driverInput.steerReturnRate));
            W("input_stick_response_exponent", F(driverInput.stickResponseExponent));
            W("collision_debounce_s", F(egoCar.collisionDebounceSeconds));
            // Driver's view (Step 3). Horizontal FOV follows from the Game window's aspect ratio.
            if (driverView != null)
            {
                Camera cam = driverView.GetComponent<Camera>();
                Vector3 eye = driverView.transform.localPosition;
                W("driver_eye_local_m", $"{eye.x.ToString("F3", Inv)};{eye.y.ToString("F3", Inv)};{eye.z.ToString("F3", Inv)}");
                W("fov_mode", driverView.fovMode.ToString());
                W("screen_diagonal_inch", F(driverView.screenDiagonalInch));
                W("screen_aspect", F(driverView.aspectWidth) + ":" + F(driverView.aspectHeight));
                W("viewing_distance_cm", F(driverView.viewingDistanceCm));
                W("fov_geometric_vertical_deg", F(driverView.GeometricVerticalFov));
                W("fov_applied_vertical_deg", F(cam.fieldOfView));
                W("fov_applied_horizontal_deg", F(Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect)));
                W("render_resolution_px", $"{cam.pixelWidth}x{cam.pixelHeight}");
            }
            W("interior_visible_at_start", interior != null && interior.interiorVisible ? "True" : "False");
            // Driver twin (Step 3): gaze source, buffer rules, state limits, hazard-seen result.
            if (driverTwin != null)
            {
                W("gaze_source", driverTwin.GazeSourceName.Replace(',', ' '));
                W("gaze_viewport_convention", "x 0 left .. 1 right; y 0 bottom .. 1 top; outside = off-screen");
                W("road_ahead_window_deg", $"left/right {F(driverTwin.roadAheadHalfWidthDeg)};down {F(driverTwin.roadAheadDownDeg)};up {F(driverTwin.roadAheadUpDeg)}");
                W("road_ahead_window_note", "our design choice; gaze out of the windows outside it = OffRoad");
                W("buffer_max_s", F(driverTwin.bufferMax));
                W("buffer_start_s", F(driverTwin.bufferStart));
                W("buffer_refill_rate", F(driverTwin.refillRate));
                W("buffer_refill_delay_s", F(driverTwin.refillDelay));
                W("buffer_drain_rate", F(driverTwin.drainRate));
                W("buffer_mirror_speedometer_latency_s", F(driverTwin.mirrorSpeedometerLatency));
                W("state_attentive_min_s", F(driverTwin.attentiveMin));
                W("state_distracted_below_s", F(driverTwin.distractedBelow));
                W("state_limits_note", "our design choice");
                W("hazard_dwell_min_s", F(driverTwin.hazardDwellMin));
                W("hazard_seen", driverTwin.HazardSeen ? "True" : "False");
                W("hazard_first_fixation_time_s", double.IsNaN(driverTwin.HazardFirstFixationTime) ? "" : driverTwin.HazardFirstFixationTime.ToString("F3", Inv));
                W("hazard_first_fixation_since_onset_s", double.IsNaN(driverTwin.HazardFixationSinceOnset) ? "" : driverTwin.HazardFixationSinceOnset.ToString("F3", Inv));
            }
            WritePupilMeta(W);
            var bloomVolume = FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            W("bloom_volume_active", bloomVolume != null && bloomVolume.isActiveAndEnabled ? "True" : "False");
            W("gamepad", string.IsNullOrEmpty(driverInput.GamepadName) ? "none" : driverInput.GamepadName.Replace(',', ' '));
            W("input_devices", DriverInput.AllDeviceNames.Replace(',', ' '));
        }

        /// <summary>Eye-tracker part of the meta file (Docs/EyeTracking.md explains each key).</summary>
        private void WritePupilMeta(Action<string, string> W)
        {
            if (!PupilInUse)
            {
                W("gaze_tracker", "none");
                return;
            }
            string F(double v, string f) => double.IsNaN(v) || double.IsInfinity(v) ? "" : v.ToString(f, Inv);
            string Clean(string v) => (v ?? "").Replace(',', ' ');
            W("gaze_tracker", pupil.IsFake ? "FakePupilServer (home test, NOT real gaze)" : Clean(pupil.trackerModel));
            W("pupil_capture_version", Clean(pupil.CaptureVersion));
            W("pupil_endpoint", Clean(pupil.Endpoint));
            W("tracker_eyes_expected", pupil.expectedEyes.ToString());
            W("tracker_eyes_observed", Clean(pupil.EyeShares()));
            W("tracker_eyes_note", "share of gaze samples per topic: gaze.3d.01 = binocular; gaze.3d.0 / .1 = one eye");
            var surf = new StringBuilder();
            for (int i = 0; i < pupil.surfaces.Count; i++)
            {
                PupilSurface ps = pupil.surfaces[i];
                if (ps == null) continue;
                if (surf.Length > 0) surf.Append(';');
                surf.Append($"{i}={ps.surfaceName}>{(ps.isZone ? "zone " + ps.zone : ps.camera != null ? ps.camera.name : "no camera")}");
            }
            W("pupil_surfaces", Clean(surf.ToString()));
            W("pupil_unknown_surfaces", Clean(pupil.UnknownSurfaces));
            W("confidence_threshold", pupil.confidenceThreshold.ToString("F2", Inv));
            W("confidence_threshold_note", "0.6 named as the usual filter in Pupil hmd-eyes GazeData.cs; Pupil Player default TO VERIFY; our choice");
            W("max_sample_age_s", pupil.maxSampleAge.ToString("F3", Inv));
            W("gaze_invalid_rule", "no sample or received longer ago than max_sample_age or confidence below threshold = invalid = OffScreen (dropout hold: part C; team decision)");
            W("blink_detector", pupil.subscribeBlinks ? (pupil.startBlinkDetector ? "subscribed; started from Unity" : "subscribed") : "off");
            W("blink_count", pupil.BlinkCount.ToString(Inv));
            W("world_camera_resolution_entered", Clean(pupil.worldCameraResolution));
            W("world_camera_fps_entered", pupil.worldCameraFps > 0 ? pupil.worldCameraFps.ToString(Inv) : "");
            W("world_camera_fps_measured", F(pupil.MeasuredWorldFps, "F1"));
            W("calibration_method", Clean(pupil.calibrationMethod));
            W("pupil_accuracy_deg", F(pupil.PupilAccuracyDeg, "F3"));
            W("pupil_precision_deg", F(pupil.PupilPrecisionDeg, "F3"));
            W("pupil_accuracy_note", "last value of Pupil Capture's Accuracy Visualizer received during this drive (log line; TO VERIFY); empty = none");
            PupilClockSync a = pupil.FirstSync, b = pupil.LastSync;
            W("clock_sync_first", a.ok ? $"offset_s {a.offset.ToString("F6", Inv)};round_trip_ms {a.roundTripMs.ToString("F3", Inv)};label {a.label}" : "");
            W("clock_sync_last", b.ok ? $"offset_s {b.offset.ToString("F6", Inv)};round_trip_ms {b.roundTripMs.ToString("F3", Inv)};label {b.label}" : "");
            W("clock_sync_count", pupil.SyncCount.ToString(Inv));
            W("clock_drift_ms_per_min", F(pupil.DriftMsPerMinute(), "F3"));
            W("clock_note", "offset = Pupil time - local clock (Stopwatch) at the middle of the shortest of 10 't' round trips; repeated every resync interval");
            pupil.LatencyStats(out double med, out double p95, out int n);
            W("latency_ms_median", F(med, "F1"));
            W("latency_ms_p95", F(p95, "F1"));
            W("latency_samples", n.ToString(Inv));
            W("latency_note", "receive time in Unity (main thread; up to one frame late) minus gaze time stamp mapped with the clock offset");
            W("gaze_samples_received", pupil.ReceivedSamples.ToString(Inv));
        }

        private void CloseFile()
        {
            if (writer == null) return;
            if ((object)pupil != null) pupil.InfoChanged -= OnPupilInfoChanged;
            if (gazeWriter != null)
            {
                if ((object)pupil != null) WriteRawGaze();
                gazeWriter.Flush(); gazeWriter.Dispose(); gazeWriter = null;
            }

            // Write any events still waiting (e.g. DriveEnd) to the events file.
            if (events != null)
                foreach (DriveEvent e in events.TakePending())
                    eventWriter.WriteLine($"{e.time.ToString("F3", Inv)},{e.step.ToString(Inv)},{e.type},{e.target},{e.detail}");

            // Drive stopped before the end of the road (R pressed or Play stopped).
            if (session != null && !session.DriveFinished)
            {
                string t = session.DriveTime.ToString("F3", Inv), step = session.StepCount.ToString(Inv);
                // Close the driver's open glance first, so every GlanceStart has a GlanceEnd.
                // (object) cast: compare the C# reference, because during shutdown Unity may already
                // report the component as destroyed although its data is still there.
                if ((object)driverTwin != null && driverTwin.TryCloseGlance(out GazeZoneType zone, out double duration))
                    eventWriter.WriteLine($"{t},{step},GlanceEnd,{zone},duration_s={duration.ToString("F3", Inv)};reason=drive_end");
                eventWriter.WriteLine($"{t},{step},DriveEnd,,reason=stopped");
            }

            writer.Flush(); writer.Dispose(); writer = null;
            eventWriter.Flush(); eventWriter.Dispose(); eventWriter = null;
            Debug.Log($"[DriveLogger] Closed {CurrentFilePath} ({RowsWritten} rows).");
        }

        // Called when the scene is reloaded (R), when Play mode stops, or when the app quits.
        private void OnDestroy() => CloseFile();
        private void OnApplicationQuit() => CloseFile();
    }
}

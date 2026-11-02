"""
check_drive.py - quick check of ONE drive of the Adaptive ADAS simulator.

Reads the three files of a drive written by DriveLogger.cs:
    drive_YYYYMMDD_HHMMSS.csv          time series (50 Hz)
    drive_YYYYMMDD_HHMMSS_events.csv   events (hazard onset, collisions, ...)
    drive_YYYYMMDD_HHMMSS_meta.csv     settings + hazard outcome
and prints: hazard onset time and criticality, brake reaction time, throttle release
time, minimum TTC after onset, collision events by target, gaze/attention measures
(Step 3), and warnings.

Usage (from the Analysis folder):
    python check_drive.py                      -> newest drive in ../DriveLogs
    python check_drive.py ..\\DriveLogs\\drive_20261031_225850.csv
    python check_drive.py --all                -> every drive in ../DriveLogs, one after another

Definitions (our choices, also listed in Docs/BaseScene_Parameters.md):
    Hazard onset          = the HazardOnset event (lead car starts braking).
    Brake reaction time   = time from onset to the first PRESS of the brake pedal after onset,
                            i.e. brake_raw rising from below to at/above BRAKE_THRESHOLD.
                            brake_raw is the pedal BEFORE any keyboard ramp (gamepad = trigger value).
    Throttle release time = time from onset until throttle_raw first drops below THROTTLE_THRESHOLD.
                            0 if the throttle was already released at onset.
    Analysis window       = onset until the HazardEnd event (or onset + WINDOW_FALLBACK_S).
    Pre-onset headway     = median time headway in the PRE_ONSET_S seconds before onset (rows where
                            the lead car was ahead in the same lane). Shows how the driver followed
                            naturally, compared with the headway at which the hazard was triggered.
    Gaze (Step 3, see Docs/DriverTwin.md):
    Glance                = consecutive rows with the same gaze_zone. A row at time t covers
                            (t - 0.02, t], so a glance of n rows lasts n * 0.02 s.
    Time to first fixation = from onset until the start of the first uninterrupted look at the
                            lead car of at least hazard_dwell_min_s (0.1 s) after onset
                            (HazardFirstFixation event; cross-checked from the rows).
                            Printed next to the brake reaction time; a warning is given when the
                            brake was pressed BEFORE the first fixation.
    Eyes off road (pre)   = share of the EYES_OFF_WINDOW_S seconds before onset with the gaze not
                            on Road/LeadCar (mirror and speedometer count as off-road).
    Rows between GazeTestStart and GazeTestEnd (scripted gaze test) are synthetic and are left
    out of the gaze measures.
Only pandas is needed (pip install pandas).
"""

import argparse
import sys
from pathlib import Path

import pandas as pd

# ---- Settings (our choices) -------------------------------------------------
BRAKE_THRESHOLD = 0.05        # brake pedal counted as pressed above 5 %
THROTTLE_THRESHOLD = 0.05     # throttle counted as released below 5 %
WINDOW_FALLBACK_S = 15.0      # analysis window if there is no HazardEnd event
PRE_ONSET_S = 20.0            # window before onset for the driver's "natural" following behaviour
EDGE_THW_S = 0.05             # onset headway this close to a window limit -> warning
EDGE_SPEED_KMH = 1.0          # onset speed this close to a window limit -> warning
EXPECTED_DT_S = 0.02          # 50 Hz logging
EYES_OFF_WINDOW_S = 10.0      # brief: eyes-off-road share in the 10 s before onset
ON_ROAD_ZONES = {"Road", "LeadCar"}

ANALYSIS_DIR = Path(__file__).resolve().parent
DEFAULT_LOG_DIR = ANALYSIS_DIR.parent / "DriveLogs"


# ---- Finding and loading files ----------------------------------------------
def main_csv_files(folder: Path):
    """All main drive CSVs (not _events / _meta) in a folder, oldest first."""
    files = [p for p in folder.glob("drive_*.csv")
             if not p.stem.endswith("_events") and not p.stem.endswith("_meta")]
    return sorted(files, key=lambda p: p.name)


def resolve_drive(arg):
    """Turns the command-line argument into the path of a main drive CSV."""
    if arg is None:
        files = main_csv_files(DEFAULT_LOG_DIR)
        if not files:
            sys.exit(f"No drive files found in {DEFAULT_LOG_DIR}")
        return files[-1]
    p = Path(arg)
    if p.is_dir():
        files = main_csv_files(p)
        if not files:
            sys.exit(f"No drive files found in {p}")
        return files[-1]
    # Accept the _events or _meta file too: map it to the main CSV.
    stem = p.stem.removesuffix("_events").removesuffix("_meta")
    main = p.with_name(stem + ".csv")
    if not main.exists():
        sys.exit(f"File not found: {main}")
    return main


def load_drive(main_path: Path):
    data = pd.read_csv(main_path)
    events_path = main_path.with_name(main_path.stem + "_events.csv")
    meta_path = main_path.with_name(main_path.stem + "_meta.csv")
    events = pd.read_csv(events_path, dtype=str, keep_default_na=False) if events_path.exists() else pd.DataFrame(
        columns=["t_s", "step", "event", "target", "detail"])
    if len(events):
        events["t_s"] = events["t_s"].astype(float)
    meta = {}
    if meta_path.exists():
        m = pd.read_csv(meta_path, dtype=str, keep_default_na=False)
        meta = dict(zip(m["key"], m["value"]))
    return data, events, meta


def parse_detail(detail: str):
    """'a=1;b=x' -> {'a': '1', 'b': 'x'}"""
    out = {}
    for part in str(detail).split(";"):
        if "=" in part:
            k, v = part.split("=", 1)
            out[k.strip()] = v.strip()
    return out


def num(x, fmt="{:.2f}", unit=""):
    """Formats a number, or '--' if it is missing."""
    try:
        v = float(x)
    except (TypeError, ValueError):
        return "--"
    if pd.isna(v):
        return "--"
    return fmt.format(v) + unit


# ---- The check -------------------------------------------------------------
def check_drive(main_path: Path):
    data, events, meta = load_drive(main_path)
    warnings = []

    if "events" not in data.columns:
        print(f"{main_path.name}: older log format (before Step 2), skipped.")
        return

    print("=" * 72)
    print(f"DRIVE  {main_path.name}")
    print("=" * 72)

    # ---- 1. Drive overview ----
    duration = data["t_s"].iloc[-1]
    dt = data["t_s"].diff().dropna().round(4)
    bad_dt = int((dt != EXPECTED_DT_S).sum())
    print(f"Duration {duration:.1f} s, {len(data)} rows, study mode: {meta.get('study_mode', '?')}")
    print(f"Sampling: {'OK, every step 0.02 s' if bad_dt == 0 else f'{bad_dt} irregular steps'}")
    if bad_dt:
        warnings.append(f"{bad_dt} rows are not 0.02 s apart (missing physics steps?)")

    devices = data["input_device"].value_counts(normalize=True) * 100
    print("Input used: " + ", ".join(f"{k} {v:.0f}%" for k, v in devices.items()))
    print(f"Gamepad at start: {meta.get('gamepad', '?')}")
    if meta.get("gamepad", "none") == "none":
        warnings.append("No gamepad detected at the start of the drive")
    if devices.get("Keyboard", 0) > 0:
        warnings.append("Keyboard was used (pedals are on/off and ramped: reaction times not comparable with gamepad/pedals)")
    if meta.get("study_mode", "False") != "True":
        warnings.append("Test mode (not study mode)")

    helpers = events[events["event"].isin(["Reset", "CruiseOn", "ReverseOn"])]
    if len(helpers):
        counts = helpers["event"].value_counts().to_dict()
        warnings.append("Test helpers used: " + ", ".join(f"{k} x{v}" for k, v in counts.items()))
    pad_events = events[events["event"].str.startswith(("Gamepad", "Joystick"))]
    for _, e in pad_events.iterrows():
        print(f"  input event at {e.t_s:.2f} s: {e.event} {e.target} {e.detail}")

    # ---- 2. Hazard onset and criticality ----
    print("\n-- Hazard --")
    onset_ev = events[events["event"] == "HazardOnset"]
    skipped_ev = events[events["event"] == "HazardSkipped"]
    print(f"Trigger settings: headway {meta.get('trigger_thw_window_s', '?')} s, "
          f"speed {meta.get('trigger_speed_window_kmh', '?')} km/h, same lane {meta.get('trigger_require_same_lane', '?')}, "
          f"max wait {meta.get('trigger_max_wait_s', '?')} s")

    if len(skipped_ev):
        e = skipped_ev.iloc[0]
        d = parse_detail(e["detail"])
        print(f"SKIPPED at {e.t_s:.2f} s after waiting {d.get('waited_s', '?')} s")
        print(f"  share of waiting time each condition was met: lane {d.get('lane_ok_pct', '?')}%, "
              f"headway {d.get('thw_ok_pct', '?')}%, speed {d.get('speed_ok_pct', '?')}%, "
              f"closing {d.get('closing_ok_pct', '?')}%")
        warnings.append("Hazard SKIPPED: trigger conditions not met in time")
    if len(onset_ev) == 0:
        if len(skipped_ev) == 0:
            print("No hazard onset in this drive (drive ended before the hazard).")
            warnings.append("No hazard onset")
        report_collisions(events, None, None)
        report_gaze(data, events, meta, None, warnings)
        print_warnings(warnings)
        return

    e = onset_ev.iloc[0]
    t0 = float(e["t_s"])
    d = parse_detail(e["detail"])
    print(f"{e['target']} onset at t = {t0:.2f} s (waited {d.get('waited_s', '?')} s for the trigger conditions)")
    print(f"  Criticality at onset: ego {num(d.get('ego_speed_kmh'), '{:.1f}', ' km/h')}, "
          f"lead {num(d.get('lead_speed_kmh'), '{:.1f}', ' km/h')}, gap {num(d.get('gap_m'), '{:.1f}', ' m')}, "
          f"time headway {num(d.get('thw_s'), '{:.2f}', ' s')}, TTC {num(d.get('ttc_s'), '{:.2f}', ' s')}, "
          f"closing {num(d.get('closing_mps'), '{:.2f}', ' m/s')}")

    # ---- Following behaviour before onset, and edge-of-window check ----
    pre = data[(data["t_s"] >= t0 - PRE_ONSET_S) & (data["t_s"] < t0)]
    thw_pre = pre["time_headway_s"].dropna()
    share_following = 100.0 * len(thw_pre) / len(pre) if len(pre) else 0.0
    if len(thw_pre):
        print(f"  Driver's headway in the {PRE_ONSET_S:.0f} s before onset: median {thw_pre.median():.2f} s "
              f"(25-75 %: {thw_pre.quantile(0.25):.2f}-{thw_pre.quantile(0.75):.2f} s), "
              f"lead ahead in lane {share_following:.0f} % of the time; "
              f"median speed {pre['ego_speed_mps'].median() * 3.6:.1f} km/h")
    else:
        print(f"  Driver's headway in the {PRE_ONSET_S:.0f} s before onset: -- (lead car never ahead in the lane)")

    def window(key):
        """'1-3' -> (1.0, 3.0); None if missing."""
        try:
            lo, hi = meta[key].split("-")
            return float(lo), float(hi)
        except (KeyError, ValueError):
            return None

    thw_on, spd_on = d.get("thw_s"), d.get("ego_speed_kmh")
    w_thw, w_spd = window("trigger_thw_window_s"), window("trigger_speed_window_kmh")
    if w_thw and thw_on not in (None, ""):
        v = float(thw_on)
        for limit in w_thw:
            if abs(v - limit) <= EDGE_THW_S:
                warnings.append(f"Onset headway {v:.2f} s is at the EDGE of the trigger window {w_thw[0]}-{w_thw[1]} s "
                                f"(the window, not the driver's natural following, set the criticality)")
    if w_spd and spd_on not in (None, ""):
        v = float(spd_on)
        for limit in w_spd:
            if abs(v - limit) <= EDGE_SPEED_KMH:
                warnings.append(f"Onset speed {v:.1f} km/h is at the EDGE of the trigger window {w_spd[0]}-{w_spd[1]} km/h")
    if len(thw_pre) and w_thw and not (w_thw[0] <= thw_pre.median() <= w_thw[1]):
        warnings.append(f"Driver's median pre-onset headway {thw_pre.median():.2f} s is OUTSIDE the trigger window")

    # Analysis window: onset -> HazardEnd (or fallback).
    end_ev = events[(events["event"] == "HazardEnd") & (events["t_s"] >= t0)]
    t_end = float(end_ev.iloc[0]["t_s"]) if len(end_ev) else t0 + WINDOW_FALLBACK_S
    end_reason = parse_detail(end_ev.iloc[0]["detail"]).get("reason", "") if len(end_ev) else "no HazardEnd, fallback window"
    win = data[(data["t_s"] >= t0) & (data["t_s"] <= t_end)]
    at_onset = data[data["t_s"] >= t0].iloc[0]
    print(f"  Analysis window: {t0:.2f} -> {t_end:.2f} s ({end_reason})")
    if "lead_brake_lights_on" in data.columns:
        lit = data[(data["lead_brake_lights_on"] == 1)]
        if len(lit) and abs(lit["t_s"].iloc[0] - t0) > 0.021:
            warnings.append(f"Brake lights came on at {lit['t_s'].iloc[0]:.2f} s, not at onset")
    print(f"  Settings: ego brake build-up {meta.get('ego_brake_build_up_s', '?')} s, "
          f"bloom {meta.get('bloom_volume_active', '?')}, lead decel {meta.get('lead_brake_decel_mps2', '?')} m/s^2")

    # ---- 3. Driver response ----
    print("\n-- Driver response --")
    if int(at_onset.get("cruise_on", 0)) == 1:
        warnings.append("Cruise control was ON at onset: throttle release is not meaningful")

    # Throttle release
    if at_onset["throttle_raw"] < THROTTLE_THRESHOLD:
        print("Throttle release time: 0 (throttle already released at onset)")
    else:
        rel = win[win["throttle_raw"] < THROTTLE_THRESHOLD]
        print("Throttle release time: " + (f"{rel['t_s'].iloc[0] - t0:.2f} s" if len(rel) else "never released in window"))

    # Brake reaction time = first rising edge of brake_raw after onset
    rt = None
    after = data[data["t_s"] >= t0].reset_index(drop=True)
    pressed = after["brake_raw"] >= BRAKE_THRESHOLD
    if pressed.iloc[0]:
        warnings.append("Brake was already pressed at onset: RT below = next NEW press")
    rising = pressed & ~pressed.shift(1, fill_value=pressed.iloc[0])
    if rising.any():
        t_brake = after.loc[rising.idxmax(), "t_s"]
        rt = t_brake - t0
        print(f"Brake reaction time: {rt:.2f} s  (brake pressed at t = {t_brake:.2f} s)")
        if t_brake > t_end:
            warnings.append("First brake press came after the hazard window ended")
    else:
        print("Brake reaction time: -- (no brake press after onset)")
        warnings.append("No brake press after onset")

    # Strongest braking deceleration, only BEFORE the first collision (a crash makes huge spikes).
    first_col = events[(events["event"] == "Collision") & (events["t_s"] >= t0)]
    t_cut = float(first_col.iloc[0]["t_s"]) if len(first_col) else t_end
    pre = win[win["t_s"] < t_cut]
    decel = max(0.0, -pre["ego_accel_mps2"].min()) if len(pre) else float("nan")
    print(f"Max brake pedal in window: {win['brake'].max():.2f}, "
          f"strongest deceleration before any collision: {num(decel, '{:.1f}', ' m/s^2')}")

    # ---- 4. Outcome ----
    print("\n-- Outcome --")
    ttc = win["ttc_s"].dropna()
    if len(ttc):
        i = ttc.idxmin()
        print(f"Min TTC after onset: {ttc.min():.2f} s at t = {data.loc[i, 't_s']:.2f} s")
    else:
        print("Min TTC after onset: -- (never closing in on the lead car in the window)")
    gap = win["gap_m"].dropna()
    print(f"Min gap after onset: {num(gap.min() if len(gap) else None, '{:.1f}', ' m')}")

    report_collisions(events, t0, t_end)
    lead_hits = events[(events["event"] == "Collision") & (events["target"] == "Lead")
                       & (events["t_s"] >= t0) & (events["t_s"] <= t_end)]
    if len(lead_hits):
        warnings.append(f"Collision with the lead car during the hazard ({len(lead_hits)} event(s))")

    has_gaze, t_fix = report_gaze(data, events, meta, t0, warnings)
    if has_gaze:
        report_response_timing(rt, t_fix, meta, warnings)
    print_warnings(warnings)


def report_response_timing(rt, t_fix, meta, warnings):
    """Brake reaction time and time to first fixation side by side (both from onset)."""
    print("\n-- Response timing (from onset) --")
    print(f"  {'first fixation on lead car':<28} {'brake press':<12} {'fixation -> brake':<18}")
    between = (rt - t_fix) if (rt is not None and t_fix is not None) else None
    print(f"  {num(t_fix, '{:+.2f}', ' s'):<28} {num(rt, '{:+.2f}', ' s'):<12} {num(between, '{:+.2f}', ' s'):<18}")
    mouse = meta.get("gaze_source", "Mouse").startswith("Mouse")
    if rt is not None and t_fix is not None and rt < t_fix:
        hint = ("with MOUSE gaze this means the mouse was not where the driver actually looked"
                if mouse else "the driver braked before looking at the lead car (peripheral vision?) "
                              "or the eye tracker / lead-car zone missed the look")
        warnings.append(f"Brake press (+{rt:.2f} s) came BEFORE the first fixation on the lead car (+{t_fix:.2f} s): {hint}")
    elif rt is not None and t_fix is None:
        warnings.append(f"Brake press (+{rt:.2f} s) but no fixation on the lead car after onset"
                        + (" (mouse gaze: the mouse was probably elsewhere)" if mouse else ""))


def synthetic_gaze_mask(data, events):
    """True for rows inside a scripted gaze test (GazeTestStart .. GazeTestEnd)."""
    mask = pd.Series(False, index=data.index)
    starts = events[events["event"] == "GazeTestStart"]["t_s"].tolist()
    ends = events[events["event"] == "GazeTestEnd"]["t_s"].tolist()
    for t_start in starts:
        later = [t for t in ends if t >= t_start]
        t_stop = later[0] if later else data["t_s"].iloc[-1]
        mask |= (data["t_s"] > t_start) & (data["t_s"] <= t_stop)
    return mask


def glance_runs(rows):
    """Consecutive rows with the same gaze_zone -> one glance (zone, start_s, end_s, duration_s)."""
    if len(rows) == 0:
        return pd.DataFrame(columns=["zone", "start", "end", "duration"])
    zone = rows["gaze_zone"].fillna("")
    # A new run starts where the zone changes or where rows are not consecutive (gap from a mask).
    gap = rows["t_s"].diff().fillna(EXPECTED_DT_S) > EXPECTED_DT_S * 1.5
    run_id = (zone.ne(zone.shift()) | gap).cumsum()
    g = rows.groupby(run_id)
    runs = pd.DataFrame({
        "zone": g["gaze_zone"].first(),
        "start": g["t_s"].first() - EXPECTED_DT_S,
        "end": g["t_s"].last(),
        "rows": g.size(),
    }).reset_index(drop=True)
    runs["duration"] = runs["rows"] * EXPECTED_DT_S
    return runs


def report_gaze(data, events, meta, t0, warnings):
    """Step 3: gaze zones, glances, attention buffer, first look at the braking lead car.
    Returns (has_gaze, time_to_first_fixation_s or None)."""
    print("\n-- Gaze and attention (driver twin) --")
    if "gaze_zone" not in data.columns:
        gaze_events = events["event"].isin(["GlanceStart", "HazardFirstFixation"]).any()
        if not gaze_events:
            print("No gaze data (log from before Step 3).")
            return False, None
        # Step 3 B-D logs: events only (per-row gaze columns came with Step 3E).
        print("No per-row gaze columns (log from Step 3 B-D): only the gaze events are used.")
        t_fix = first_fixation_from_events(events, meta, t0, warnings) if t0 is not None else None
        return True, t_fix

    source = meta.get("gaze_source", "?")
    interior_pct = 100.0 * data["interior_on"].mean() if "interior_on" in data.columns else float("nan")
    print(f"Gaze source: {source}   interior visible {num(interior_pct, '{:.0f}', ' %')} of the drive")
    if source.startswith("Mouse"):
        warnings.append("Gaze = MOUSE pointer (home testing), not eye-tracking data")
    window = meta.get("road_ahead_window_deg")
    if window:
        print(f"Road-ahead window: {window.replace(';', ', ')} deg (outside it = OffRoad)")
    else:
        print("Road-ahead window: none (log from before it was added): sky, barrier and roadside were counted as Road")
        warnings.append("Old gaze log: 'Road' includes sky, barrier and roadside (no road-ahead window yet)")
    if not pd.isna(interior_pct) and interior_pct < 100:
        warnings.append("Interior was switched off for part of the drive (mirror/speedometer/side display zones absent)")

    synthetic = synthetic_gaze_mask(data, events)
    for _, e in events[events["event"] == "GazeTestEnd"].iterrows():
        print(f"Scripted gaze test at {e.t_s:.2f} s: {e.target}  ({e.detail})")
    if synthetic.any():
        warnings.append(f"Scripted gaze test ran ({synthetic.sum() * EXPECTED_DT_S:.1f} s): those rows are left out of the gaze measures")
    real = data[~synthetic]

    # ---- Glances per zone (whole drive, real gaze only) ----
    runs = glance_runs(real)
    if len(runs):
        total = runs["duration"].sum()
        print("Glances per zone (whole drive; first/last glance cut by the drive start/end):")
        print(f"  {'zone':<12} {'glances':>7} {'total s':>8} {'% time':>7} {'mean s':>7} {'max s':>7}")
        for zone, r in runs.groupby("zone"):
            print(f"  {zone:<12} {len(r):>7d} {r['duration'].sum():>8.1f} {100 * r['duration'].sum() / total:>7.1f} "
                  f"{r['duration'].mean():>7.2f} {r['duration'].max():>7.2f}")
    no_gaze = real["gaze_x"].isna().sum() if "gaze_x" in real.columns else 0
    if no_gaze:
        print(f"Rows without a valid gaze sample (counted as OffScreen): {no_gaze} ({100 * no_gaze / len(real):.1f} %)")

    if t0 is None:
        return True, None

    # ---- Before onset ----
    window10 = data[(data["t_s"] > t0 - EYES_OFF_WINDOW_S) & (data["t_s"] <= t0)]
    if synthetic[window10.index].any():
        warnings.append(f"Scripted gaze test overlaps the {EYES_OFF_WINDOW_S:.0f} s before onset: "
                        "only the real-gaze rows of that window are used")
    pre10 = real[(real["t_s"] > t0 - EYES_OFF_WINDOW_S) & (real["t_s"] <= t0)]
    pre_all = real[real["t_s"] <= t0]
    if len(pre10):
        off = ~pre10["gaze_zone"].isin(ON_ROAD_ZONES)
        print(f"Eyes off road in the {EYES_OFF_WINDOW_S:.0f} s before onset: {100 * off.mean():.1f} % "
              f"({off.sum() * EXPECTED_DT_S:.2f} s)")
    if len(pre_all):
        i_all = pre_all["buffer_s"].idxmin()
        i_10 = pre10["buffer_s"].idxmin() if len(pre10) else None
        print(f"Minimum attention buffer before onset: {pre_all.loc[i_all, 'buffer_s']:.2f} s at t = {pre_all.loc[i_all, 't_s']:.2f} s"
              + (f"; in the last {EYES_OFF_WINDOW_S:.0f} s: {pre10.loc[i_10, 'buffer_s']:.2f} s" if i_10 is not None else ""))
        at = pre_all.iloc[-1]
        print(f"At onset: buffer {at['buffer_s']:.2f} s, state {at['attention_state']}, gaze on {at['gaze_zone']}")

    # ---- First look at the braking lead car ----
    dwell = float(meta.get("hazard_dwell_min_s", "0.1") or 0.1)
    t_fix_event = first_fixation_from_events(events, meta, t0, warnings)

    # Cross-check from the rows (same definition as DriverTwin).
    lead_runs = glance_runs(data[data["t_s"] >= t0 - 60])
    lead_runs = lead_runs[(lead_runs["zone"] == "LeadCar") & (lead_runs["end"] >= t0)]
    t_fix_rows = None
    for _, r in lead_runs.iterrows():
        start = max(r["start"], t0)
        if r["end"] - start >= dwell - 1e-6:
            t_fix_rows = start - t0
            break
    if (t_fix_rows is None) != (t_fix_event is None) or (
            t_fix_rows is not None and abs(t_fix_rows - t_fix_event) > EXPECTED_DT_S + 1e-6):
        warnings.append(f"First-fixation check from rows ({num(t_fix_rows)} s) differs from the event ({num(t_fix_event)} s)")
    return True, t_fix_event


def first_fixation_from_events(events, meta, t0, warnings):
    """Time from onset to the first >= 100 ms look at the lead car (HazardFirstFixation), or None."""
    dwell = float(meta.get("hazard_dwell_min_s", "0.1") or 0.1)
    fix_ev = events[events["event"] == "HazardFirstFixation"]
    if len(fix_ev):
        d = parse_detail(fix_ev.iloc[0]["detail"])
        t_fix = float(d.get("since_onset_s", "nan"))
        already = d.get("looking_at_lead_at_onset", "False") == "True"
        print(f"Time to first fixation on the lead car after onset: {t_fix:.2f} s"
              + ("  (already looking at it at onset)" if already else ""))
        return t_fix
    print(f"Time to first fixation on the lead car after onset: -- (no look of >= {dwell * 1000:.0f} ms after onset)")
    warnings.append("Braking lead car was never looked at after onset (hazard_seen = 0)")
    return None


def report_collisions(events, t0, t_end):
    col = events[events["event"] == "Collision"]
    if len(col) == 0:
        print("Collisions: none")
        return
    print("Collisions by target: " + ", ".join(f"{k} x{v}" for k, v in col["target"].value_counts().items()))
    for _, c in col.iterrows():
        d = parse_detail(c["detail"])
        rel = f", {c.t_s - t0:+.2f} s from onset" if t0 is not None else ""
        inside = " (during hazard)" if t0 is not None and t0 <= c.t_s <= t_end else ""
        print(f"  t = {c.t_s:.2f} s{rel}: {c.target}, relative speed {d.get('rel_speed_kmh', '?')} km/h{inside}")


def print_warnings(warnings):
    print("\n-- Warnings --")
    if not warnings:
        print("none")
    for w in warnings:
        print(f"! {w}")
    print()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Quick check of one Adaptive ADAS drive log.")
    parser.add_argument("drive", nargs="?", help="drive CSV or folder (default: newest drive in ../DriveLogs)")
    parser.add_argument("--all", action="store_true", help="check every drive in the folder")
    args = parser.parse_args()

    if args.all:
        folder = Path(args.drive) if args.drive else DEFAULT_LOG_DIR
        for f in main_csv_files(folder):
            check_drive(f)
    else:
        check_drive(resolve_drive(args.drive))

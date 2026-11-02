// DriveEvents.cs
// Collects discrete EVENTS during a drive (hazard onset, collision, reset,
// cruise on/off, ...). Any script can call Record(...). The DriveLogger then
//   - writes every event as one line to  drive_..._events.csv  (exact time), and
//   - puts the event names into the "events" column of the next row of the main CSV,
// so the analysis can find e.g. the hazard onset with one filter.
//
// Event types used so far:
//   DriveStart, DriveEnd, HazardOnset, HazardEnd, Collision, Reset,
//   CruiseOn, CruiseOff, ReverseOn, ReverseOff

using System.Collections.Generic;
using AdaptiveADAS.Experiment;
using UnityEngine;

namespace AdaptiveADAS.Logging
{
    public struct DriveEvent
    {
        public double time;     // drive time (s)
        public long step;       // physics step
        public string type;     // e.g. "Collision"
        public string target;   // e.g. "Lead" for a collision, hazard id for hazard events, "" otherwise
        public string detail;   // free text, "key=value;key=value" (no commas)
    }

    public class DriveEvents : MonoBehaviour
    {
        [Header("References (set by the scene builder)")]
        public DriveSession session;

        private readonly List<DriveEvent> pending = new List<DriveEvent>();

        /// <summary>Last recorded event, for the debug overlay.</summary>
        public string LastEventText { get; private set; } = "";

        public void Record(string type, string target = "", string detail = "")
        {
            var e = new DriveEvent
            {
                time = session != null ? session.DriveTime : Time.fixedTimeAsDouble,
                step = session != null ? session.StepCount : 0,
                type = type,
                target = Clean(target),
                detail = Clean(detail)
            };
            pending.Add(e);
            LastEventText = $"{e.time:F2} s  {type} {e.target}";
            Debug.Log($"[Event] t = {e.time:F2} s  {type}  {e.target}  {e.detail}");
        }

        /// <summary>Returns all events not yet written and clears the list (called by the logger).</summary>
        public List<DriveEvent> TakePending()
        {
            var copy = new List<DriveEvent>(pending);
            pending.Clear();
            return copy;
        }

        // Commas would break the CSV, so they are replaced.
        private static string Clean(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(',', ';');
    }
}

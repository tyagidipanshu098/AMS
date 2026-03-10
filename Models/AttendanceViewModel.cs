using System;
using System.Collections.Generic;

namespace YourApp.Models
{
    // ── One In/Out pair within a day ─────────────────────────────────────────
    public class AttendancePair
    {
        public TimeSpan? In  { get; set; }
        public TimeSpan? Out { get; set; }

        /// <summary>True when both In and Out are present.</summary>
        public bool IsComplete => In.HasValue && Out.HasValue;

        /// <summary>Duration in minutes for a complete pair (null if incomplete).</summary>
        public double? DurationMinutes =>
            IsComplete ? (Out!.Value - In!.Value).TotalMinutes : null;

        public string InDisplay  => In.HasValue  ? $"{(int)In.Value.TotalHours:D2}:{In.Value.Minutes:D2}"   : "—";
        public string OutDisplay => Out.HasValue ? $"{(int)Out.Value.TotalHours:D2}:{Out.Value.Minutes:D2}" : "—";
    }

    // ── One calendar day ────────────────────────────────────────────────────
    public class AttendanceDay
    {
        public DateTime          Date      { get; set; }
        public string            Floor     { get; set; } = "";
        public List<AttendancePair> Pairs  { get; set; } = new();

        /// <summary>
        /// Row is skipped when any pair has exactly one side missing.
        /// Pairs where both sides are null are simply unused slots.
        /// </summary>
        public bool IsSkipped => Pairs.Exists(p =>
            (p.In.HasValue && !p.Out.HasValue) ||
            (!p.In.HasValue && p.Out.HasValue));

        /// <summary>Total valid time in minutes (null when row is skipped).</summary>
        public double? TotalMinutes
        {
            get
            {
                if (IsSkipped) return null;
                double t = 0;
                foreach (var p in Pairs)
                    if (p.DurationMinutes.HasValue) t += p.DurationMinutes.Value;
                return t;
            }
        }

        public string TotalDisplay
        {
            get
            {
                if (IsSkipped) return "—";
                double m = TotalMinutes ?? 0;
                return $"{(int)(m / 60)}h {(int)(m % 60):D2}m";
            }
        }

        /// <summary>First In time of the day (for display).</summary>
        public string FirstIn => Pairs.Find(p => p.In.HasValue)?.InDisplay ?? "—";

        /// <summary>Last Out time of the day (for display).</summary>
        public string LastOut
        {
            get
            {
                AttendancePair? last = null;
                foreach (var p in Pairs)
                    if (p.Out.HasValue) last = p;
                return last?.OutDisplay ?? "—";
            }
        }
    }

    // ── One calendar month ──────────────────────────────────────────────────
    public class AttendanceMonth
    {
        public int              Year      { get; set; }
        public int              Month     { get; set; }
        public List<AttendanceDay> Days   { get; set; } = new();

        public string MonthLabel => new DateTime(Year, Month, 1).ToString("MMMM yyyy");

        public IEnumerable<AttendanceDay> ValidDays =>
            Days.FindAll(d => !d.IsSkipped && d.TotalMinutes.HasValue);

        public double? AverageMinutes
        {
            get
            {
                var valid = new List<double>();
                foreach (var d in Days)
                    if (!d.IsSkipped && d.TotalMinutes.HasValue)
                        valid.Add(d.TotalMinutes.Value);
                if (valid.Count == 0) return null;
                double sum = 0;
                foreach (var v in valid) sum += v;
                return sum / valid.Count;
            }
        }

        public string AverageDisplay
        {
            get
            {
                if (AverageMinutes == null) return "—";
                double m = AverageMinutes.Value;
                return $"{(int)(m / 60)}h {(int)(m % 60):D2}m";
            }
        }

        public int TotalDays      => Days.Count;
        public int ValidDayCount  => Days.FindAll(d => !d.IsSkipped && d.TotalMinutes.HasValue).Count;
        public int SkippedCount   => Days.FindAll(d => d.IsSkipped).Count;

        public double? MaxMinutes
        {
            get
            {
                double? max = null;
                foreach (var d in Days)
                    if (d.TotalMinutes.HasValue && (max == null || d.TotalMinutes > max))
                        max = d.TotalMinutes;
                return max;
            }
        }

        public double? MinMinutes
        {
            get
            {
                double? min = null;
                foreach (var d in Days)
                    if (!d.IsSkipped && d.TotalMinutes.HasValue && (min == null || d.TotalMinutes < min))
                        min = d.TotalMinutes;
                return min;
            }
        }

        private static string MinToDisplay(double? m) =>
            m == null ? "—" : $"{(int)(m.Value / 60)}h {(int)(m.Value % 60):D2}m";

        public string MaxDisplay => MinToDisplay(MaxMinutes);
        public string MinDisplay => MinToDisplay(MinMinutes);
    }

    // ── Top-level view model ─────────────────────────────────────────────────
    public class AttendanceViewModel
    {
        public string                    EmployeeCode { get; set; } = "";
        public string                    EmployeeName { get; set; } = "";
        public List<AttendanceMonth>     Months       { get; set; } = new();

        public int TotalWorkDays  => Months.Sum(m => m.ValidDayCount);
        public int TotalSkipped   => Months.Sum(m => m.SkippedCount);

        public double? OverallAverageMinutes
        {
            get
            {
                var all = new List<double>();
                foreach (var mo in Months)
                    foreach (var d in mo.Days)
                        if (!d.IsSkipped && d.TotalMinutes.HasValue)
                            all.Add(d.TotalMinutes.Value);
                if (all.Count == 0) return null;
                return all.Sum() / all.Count;
            }
        }

        public string OverallAverageDisplay
        {
            get
            {
                if (OverallAverageMinutes == null) return "—";
                double m = OverallAverageMinutes.Value;
                return $"{(int)(m / 60)}h {(int)(m % 60):D2}m";
            }
        }
    }
}

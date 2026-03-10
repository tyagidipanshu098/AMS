namespace YourApp.Models
{
    /// <summary>
    /// Persisted via JSON in appsettings or a simple flat file.
    /// All threshold fields are nullable — null means "check disabled".
    /// </summary>
    public class TraineeSettings
    {
        // ── Training period ───────────────────────────────────────────────────
        public int    TrainingMonths          { get; set; } = 3;

        // ── Eligibility thresholds ────────────────────────────────────────────
        public double? MinAttendancePct       { get; set; } = 85.0;   // e.g. 85 = 85%
        public int?    MaxLateArrivals        { get; set; } = 10;
        public int?    MaxEarlyDepartures     { get; set; } = 10;
        public double? MinAvgDailyHours       { get; set; } = 7.5;    // decimal hours
        public int?    MaxSkippedDays         { get; set; } = 5;
        public int?    MinPerfectDays         { get; set; } = 20;

        // ── Derived display helpers ───────────────────────────────────────────
        public string MinAvgDailyHoursDisplay =>
            MinAvgDailyHours.HasValue
                ? $"{(int)MinAvgDailyHours.Value}h {(int)((MinAvgDailyHours.Value % 1) * 60):D2}m"
                : "—";
        public string MinAttendancePctDisplay => MinAttendancePct.HasValue
    ? $"{MinAttendancePct.Value.ToString("F1")}%"
    : "0%";
    }
}

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ExcelDataReader;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using YourApp.Models;

namespace YourApp.Controllers
{
    public class AttendanceController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        public AttendanceController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        // ════════════════════════════════════════════════════════════════════
        //  MAIN DASHBOARD
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Upload page — shows the file picker form.</summary>
        [HttpGet]
        public IActionResult Index() => View();

        /// <summary>
        /// Receives the uploaded Excel file, parses it and renders the main
        /// attendance dashboard.  The parsed ViewModel is also stashed in
        /// TempData (as JSON) so the Trainee Dashboard can reload it without
        /// requiring a second upload.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> File(IFormFile ExcelFile)
        {
            if (ExcelFile == null || ExcelFile.Length == 0)
                return View("Index");

            string filePath = await SaveUpload(ExcelFile);

            var vm = ParseExcelFile(filePath);

            // Stash for trainee route — keep the file path in TempData so
            // TraineeDashboard can re-parse without re-upload.
            TempData["LastUploadPath"] = filePath;

            return View("Dashboard", vm);
        }

        // ════════════════════════════════════════════════════════════════════
        //  TRAINEE DASHBOARD
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Shows the Trainee Eligibility dashboard using the last uploaded
        /// file.  If no file has been uploaded yet this session the user is
        /// sent back to the upload page.
        /// </summary>
        [HttpGet]
        public IActionResult TraineeDashboard()
        {
            if (TempData.Peek("LastUploadPath") is not string path
                || !System.IO.File.Exists(path))
            {
                TempData["InfoMessage"] =
                    "Please upload an attendance file first.";
                return RedirectToAction(nameof(Index));
            }

            // Keep the key alive for subsequent requests in the same session.
            TempData.Keep("LastUploadPath");

            var vm       = ParseExcelFile(path);
            var settings = LoadSettings();

            return View((Attendance: vm, Settings: settings));
        }

        /// <summary>
        /// Alternative POST entry-point: upload a file specifically for the
        /// trainee evaluation (skips the main dashboard).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> TraineeDashboardFile(IFormFile ExcelFile)
        {
            if (ExcelFile == null || ExcelFile.Length == 0)
                return RedirectToAction(nameof(Index));

            string filePath = await SaveUpload(ExcelFile);
            TempData["LastUploadPath"] = filePath;

            var vm       = ParseExcelFile(filePath);
            var settings = LoadSettings();

            return View("TraineeDashboard", (Attendance: vm, Settings: settings));
        }

        // ════════════════════════════════════════════════════════════════════
        //  TRAINEE SETTINGS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Renders the settings configuration page.</summary>
        [HttpGet]
        public IActionResult TraineeSettings()
        {
            return View(LoadSettings());
        }

        /// <summary>Saves the submitted settings and redirects back.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SaveTraineeSettings(
            int     TrainingMonths,
            string? MinAttendancePct,
            string? MinAvgDailyHours,
            string? MaxLateArrivals,
            string? MaxEarlyDepartures,
            string? MaxSkippedDays,
            string? MinPerfectDays)
        {
            // Parse each nullable field — empty / missing = feature disabled
            var settings = new TraineeSettings
            {
                TrainingMonths      = Math.Max(1, TrainingMonths),
                MinAttendancePct    = ParseNullableDouble(MinAttendancePct),
                MinAvgDailyHours    = ParseNullableDouble(MinAvgDailyHours),
                MaxLateArrivals     = ParseNullableInt(MaxLateArrivals),
                MaxEarlyDepartures  = ParseNullableInt(MaxEarlyDepartures),
                MaxSkippedDays      = ParseNullableInt(MaxSkippedDays),
                MinPerfectDays      = ParseNullableInt(MinPerfectDays),
            };

            SaveSettingsToFile(settings);
            TempData["Saved"] = true;
            return RedirectToAction(nameof(TraineeSettings));
        }

        // ════════════════════════════════════════════════════════════════════
        //  PRIVATE — FILE HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Saves the uploaded file to wwwroot/UploadedFiles and returns its
        /// full path.  A unique GUID prefix is added to avoid collisions.
        /// </summary>
        private async Task<string> SaveUpload(IFormFile file)
        {
            string uploadDir = Path.Combine(_environment.WebRootPath, "UploadedFiles");
            Directory.CreateDirectory(uploadDir);

            string safeName = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";
            string filePath = Path.Combine(uploadDir, safeName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            return filePath;
        }

        // ════════════════════════════════════════════════════════════════════
        //  PRIVATE — EXCEL PARSING
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Parses an Excel file (.xls or .xlsx) into an AttendanceViewModel.
        ///
        /// Expected layout
        ///   Row 0 : "E4681 - DIVYANSH TYAGI"       (employee code + name)
        ///   Row 1 : Date | Floor | In1 | Out1 | In2 | Out2 | …  (headers)
        ///   Row 2+: data rows
        /// </summary>
        private AttendanceViewModel ParseExcelFile(string filePath)
        {
            System.Text.Encoding.RegisterProvider(
                System.Text.CodePagesEncodingProvider.Instance);

            DataTable dt;
            using (var stream = System.IO.File.Open(
                       filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                dt = reader
                    .AsDataSet(new ExcelDataSetConfiguration
                    {
                        ConfigureDataTable = _ =>
                            new ExcelDataTableConfiguration { UseHeaderRow = false }
                    })
                    .Tables[0];
            }

            // ── Employee identity (row 0) ────────────────────────────────
            string raw          = dt.Rows[0][0]?.ToString() ?? "";
            string employeeCode = raw.Contains(" - ")
                ? raw.Split(" - ")[0].Trim() : raw;
            string employeeName = raw.Contains(" - ")
                ? raw.Split(" - ")[1].Trim() : raw;

            // ── Detect In/Out column pairs from headers (row 1) ──────────
            var headerRow = dt.Rows[1];
            const int pairStart = 2;   // col 0=Date, col 1=Floor, col 2+=pairs
            int numPairs = 0;
            for (int c = pairStart; c + 1 < dt.Columns.Count; c += 2)
            {
                string inH  = headerRow[c]?.ToString()     ?? "";
                string outH = headerRow[c + 1]?.ToString() ?? "";
                if (inH.StartsWith("In",  StringComparison.OrdinalIgnoreCase) &&
                    outH.StartsWith("Out", StringComparison.OrdinalIgnoreCase))
                    numPairs++;
                else
                    break;
            }

            // ── Build day list (rows 2+) ─────────────────────────────────
            var days = new List<AttendanceDay>();
            for (int r = 2; r < dt.Rows.Count; r++)
            {
                var row  = dt.Rows[r];
                var date = ParseDate(row[0]);
                if (date == null) continue;

                var day = new AttendanceDay
                {
                    Date  = date.Value,
                    Floor = row[1]?.ToString() ?? ""
                };

                for (int p = 0; p < numPairs; p++)
                {
                    int ic  = pairStart + p * 2;
                    int oc  = ic + 1;
                    var pair = new AttendancePair
                    {
                        In  = ParseTime(row[ic]),
                        Out = ParseTime(row[oc])
                    };
                    // Only add if at least one side present; both null = unused slot
                    if (pair.In.HasValue || pair.Out.HasValue)
                        day.Pairs.Add(pair);
                }

                days.Add(day);
            }

            // ── Group into calendar months ───────────────────────────────
            var months = days
                .GroupBy(d => new { d.Date.Year, d.Date.Month })
                .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                .Select(g => new AttendanceMonth
                {
                    Year  = g.Key.Year,
                    Month = g.Key.Month,
                    Days  = g.OrderBy(d => d.Date).ToList()
                })
                .ToList();

            return new AttendanceViewModel
            {
                EmployeeCode = employeeCode,
                EmployeeName = employeeName,
                Months       = months
            };
        }

        // ════════════════════════════════════════════════════════════════════
        //  PRIVATE — TRAINEE SETTINGS PERSISTENCE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Full path to the JSON file that stores trainee eligibility settings.
        /// Stored in App_Data (never served by the web server).
        /// </summary>
        private string SettingsFilePath =>
            Path.Combine(_environment.ContentRootPath, "App_Data",
                         "trainee-settings.json");

        private static readonly JsonSerializerOptions _jsonOpts =
            new JsonSerializerOptions { WriteIndented = true };

        /// <summary>
        /// Reads the persisted TraineeSettings.  Returns defaults if the file
        /// does not exist yet or cannot be read.
        /// </summary>
        private TraineeSettings LoadSettings()
        {
            try
            {
                if (System.IO.File.Exists(SettingsFilePath))
                {
                    string json = System.IO.File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<TraineeSettings>(json)
                           ?? new TraineeSettings();
                }
            }
            catch
            {
                // Swallow — fall through to defaults
            }
            return new TraineeSettings();
        }

        /// <summary>Writes the given settings to disk as formatted JSON.</summary>
        private void SaveSettingsToFile(TraineeSettings settings)
        {
            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(
                SettingsFilePath,
                JsonSerializer.Serialize(settings, _jsonOpts));
        }

        // ════════════════════════════════════════════════════════════════════
        //  PRIVATE — VALUE PARSING HELPERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Converts an Excel cell value to a nullable DateTime.
        /// Handles OA-date doubles, DateTime objects, and ISO strings.
        /// </summary>
        private static DateTime? ParseDate(object v)
        {
            if (v is DateTime dt) return dt.Date;
            if (v is double d)    return DateTime.FromOADate(d).Date;
            if (v is string s && DateTime.TryParse(s, out var p)) return p.Date;
            return null;
        }

        /// <summary>
        /// Converts an Excel cell value to a nullable TimeSpan.
        /// Handles OA-date fraction doubles (time-only), DateTime, TimeSpan,
        /// and "HH:mm" / "H:mm" strings.
        /// </summary>
        private static TimeSpan? ParseTime(object v)
        {
            if (v == null || v is DBNull) return null;

            if (v is double d)
            {
                // OA fractions: 0.0 = midnight, 0.5 = noon
                // Fractional part only (strip date component)
                double frac = d - Math.Floor(d);
                return TimeSpan.FromMinutes(Math.Round(frac * 24 * 60));
            }

            if (v is DateTime dt) return dt.TimeOfDay;
            if (v is TimeSpan ts) return ts;

            if (v is string s && !string.IsNullOrWhiteSpace(s))
            {
                s = s.Trim();
                if (TimeSpan.TryParseExact(
                        s,
                        new[] { @"hh\:mm", @"h\:mm", @"hh\:mm\:ss", @"h\:mm\:ss" },
                        null,
                        out var p))
                    return p;
            }

            return null;
        }

        /// <summary>
        /// Parses a form string to a nullable double.
        /// Returns null if the string is null, empty, or not numeric.
        /// </summary>
        private static double? ParseNullableDouble(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(
                s.Trim(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double v) ? v : null;
        }

        /// <summary>
        /// Parses a form string to a nullable int.
        /// Returns null if the string is null, empty, or not numeric.
        /// </summary>
        private static int? ParseNullableInt(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return int.TryParse(s.Trim(), out int v) ? v : null;
        }
    }
}

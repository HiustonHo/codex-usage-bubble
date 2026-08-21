using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexUsageBubble
{
    internal sealed class UsageSnapshot
    {
        public bool Available;
        public int RemainingPercent;
        public int RemainingDays;
        public DateTimeOffset ResetAt;
        public DateTimeOffset UpdatedAt;
        public string Error;
    }

    internal sealed class UsageBubbleForm : Form
    {
        private readonly Timer refreshTimer;
        private readonly string statusPath;
        private UsageSnapshot snapshot;
        private bool refreshRunning;

        private const int WmNcLeftButtonDown = 0x00A1;
        private const int HtCaption = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

        public UsageBubbleForm()
        {
            Text = "Codex Weekly Usage";
            ClientSize = new Size(196, 246);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            TopMost = true;
            BackColor = Color.FromArgb(1, 1, 1);
            TransparencyKey = Color.FromArgb(1, 1, 1);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;

            Rectangle workArea = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(workArea.Right - Width - 18, workArea.Top + 18);

            string executableDirectory = AppDomain.CurrentDomain.BaseDirectory;
            statusPath = Path.Combine(executableDirectory, "codex-weekly-usage-bubble.status.json");
            snapshot = new UsageSnapshot
            {
                Available = false,
                UpdatedAt = DateTimeOffset.Now,
                Error = "Loading current usage"
            };

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem refreshItem = new ToolStripMenuItem("Refresh now");
            refreshItem.Click += async delegate { await RefreshUsageAsync(); };
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit usage bubble");
            exitItem.Click += delegate { Close(); };
            menu.Items.Add(refreshItem);
            menu.Items.Add(exitItem);
            ContextMenuStrip = menu;

            ToolTip tip = new ToolTip();
            tip.SetToolTip(this, "Weekly Codex usage. Click to refresh; right-click to exit.");

            MouseDown += async delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    Point locationBefore = Location;
                    ReleaseCapture();
                    SendMessage(Handle, WmNcLeftButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
                    if (Location != locationBefore)
                    {
                        SaveStatus();
                    }
                    else
                    {
                        await RefreshUsageAsync();
                    }
                }
            };

            refreshTimer = new Timer();
            refreshTimer.Interval = 60000;
            refreshTimer.Tick += async delegate { await RefreshUsageAsync(); };

            Load += async delegate
            {
                ApplyRoundedRegion();
                SaveStatus();
                refreshTimer.Start();
                await RefreshUsageAsync();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(TransparencyKey);

            int percent = 0;
            string percentText = "…";
            string weeklyText = "WEEKLY LEFT";
            string daysText = "Loading";
            string resetText = "Please wait";

            if (snapshot.Available)
            {
                percent = snapshot.RemainingPercent;
                percentText = string.Format(CultureInfo.InvariantCulture, "{0}%", percent);
                daysText = snapshot.RemainingDays == 1
                    ? "1 day left"
                    : string.Format(CultureInfo.InvariantCulture, "{0} days left", snapshot.RemainingDays);
                resetText = "Reset " + snapshot.ResetAt.ToString("MMM d", CultureInfo.InvariantCulture);
            }
            else if (!string.Equals(snapshot.Error, "Loading current usage", StringComparison.Ordinal))
            {
                percentText = "—";
                weeklyText = "UNAVAILABLE";
                daysText = "Click to retry";
                resetText = "Usage not read";
            }

            RectangleF shadowBounds = new RectangleF(28f, 166f, 140f, 20f);
            using (GraphicsPath shadowPath = new GraphicsPath())
            {
                shadowPath.AddEllipse(shadowBounds);
                using (PathGradientBrush shadowBrush = new PathGradientBrush(shadowPath))
                {
                    shadowBrush.CenterColor = Color.FromArgb(90, 0, 72, 68);
                    shadowBrush.SurroundColors = new[] { Color.FromArgb(0, 0, 72, 68) };
                    graphics.FillPath(shadowBrush, shadowPath);
                }
            }

            RectangleF sphereBounds = new RectangleF(12f, 4f, 172f, 172f);
            using (GraphicsPath spherePath = new GraphicsPath())
            {
                spherePath.AddEllipse(sphereBounds);

                using (LinearGradientBrush emptySphereBrush = new LinearGradientBrush(
                    sphereBounds,
                    Color.FromArgb(43, 59, 70),
                    Color.FromArgb(9, 25, 37),
                    LinearGradientMode.Vertical))
                {
                    graphics.FillPath(emptySphereBrush, spherePath);
                }

                float fillRatio = snapshot.Available ? Math.Max(0f, Math.Min(1f, percent / 100f)) : 0.65f;
                float waterTop = sphereBounds.Bottom - (sphereBounds.Height * fillRatio);
                Color usageTone = snapshot.Available ? GetUsageTone(percent) : Color.FromArgb(0, 184, 137);
                Color liquidTop = BlendColor(usageTone, Color.White, 0.28f);
                Color liquidBottom = BlendColor(usageTone, Color.Black, 0.34f);

                GraphicsState clipState = graphics.Save();
                graphics.SetClip(spherePath);
                RectangleF liquidBounds = new RectangleF(
                    sphereBounds.Left,
                    waterTop,
                    sphereBounds.Width,
                    Math.Max(1f, sphereBounds.Bottom - waterTop));
                using (LinearGradientBrush liquidBrush = new LinearGradientBrush(
                    liquidBounds,
                    liquidTop,
                    liquidBottom,
                    LinearGradientMode.Vertical))
                {
                    graphics.FillRectangle(liquidBrush, liquidBounds);
                }

                RectangleF surfaceBounds = new RectangleF(sphereBounds.Left, waterTop - 7f, sphereBounds.Width, 14f);
                using (SolidBrush surfaceBrush = new SolidBrush(BlendColor(usageTone, Color.White, 0.42f)))
                {
                    graphics.FillEllipse(surfaceBrush, surfaceBounds);
                }
                graphics.Restore(clipState);

                using (Pen rimPen = new Pen(BlendColor(usageTone, Color.White, 0.60f), 3f))
                {
                    graphics.DrawEllipse(rimPen, sphereBounds);
                }
            }

            RectangleF highlightBounds = new RectangleF(32f, 20f, 128f, 66f);
            using (GraphicsPath highlightPath = new GraphicsPath())
            {
                highlightPath.AddEllipse(highlightBounds);
                using (LinearGradientBrush highlightBrush = new LinearGradientBrush(
                    highlightBounds,
                    Color.FromArgb(205, 255, 255, 255),
                    Color.FromArgb(12, 255, 255, 255),
                    LinearGradientMode.Vertical))
                {
                    graphics.FillPath(highlightBrush, highlightPath);
                }
            }

            using (Font percentFont = new Font("Segoe UI", 43f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font weeklyFont = new Font("Segoe UI", 11.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (Brush whiteBrush = new SolidBrush(Color.White))
            using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(percentText, percentFont, whiteBrush, new RectangleF(17f, 54f, 162f, 72f), centered);
                TextRenderer.DrawText(
                    graphics,
                    weeklyText,
                    weeklyFont,
                    new Rectangle(17, 121, 162, 27),
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                );
            }

            Rectangle labelBounds = new Rectangle(14, 185, 168, 58);
            using (GraphicsPath labelPath = CreateRoundedRectanglePath(labelBounds, 20))
            using (LinearGradientBrush labelBrush = new LinearGradientBrush(
                labelBounds,
                Color.FromArgb(252, 255, 255, 255),
                Color.FromArgb(238, 225, 244, 242),
                LinearGradientMode.Vertical))
            using (Pen labelBorder = new Pen(Color.FromArgb(210, 255, 255, 255), 1.5f))
            {
                graphics.FillPath(labelBrush, labelPath);
                graphics.DrawPath(labelBorder, labelPath);
            }

            using (Font infoFont = new Font("Segoe UI", 15.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (Brush infoBrush = new SolidBrush(Color.FromArgb(29, 51, 49)))
            using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(daysText, infoFont, infoBrush, new RectangleF(16f, 188f, 164f, 27f), centered);
                graphics.DrawString(resetText, infoFont, infoBrush, new RectangleF(16f, 214f, 164f, 27f), centered);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refreshTimer.Stop();
            refreshTimer.Dispose();
            base.OnFormClosed(e);
        }

        internal void RenderSample(string outputPath)
        {
            snapshot = new UsageSnapshot
            {
                Available = true,
                RemainingPercent = 86,
                RemainingDays = 6,
                ResetAt = new DateTimeOffset(2026, 8, 27, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = DateTimeOffset.Now,
                Error = null
            };

            string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            using (Bitmap bitmap = new Bitmap(ClientSize.Width, ClientSize.Height))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(TransparencyKey);
                OnPaint(new PaintEventArgs(graphics, ClientRectangle));
                bitmap.MakeTransparent(TransparencyKey);
                bitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private void ApplyRoundedRegion()
        {
            Region = new Region(ClientRectangle);
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color GetUsageTone(int remainingPercent)
        {
            int clamped = Math.Max(0, Math.Min(100, remainingPercent));
            if (clamped >= 50)
            {
                float amount = (clamped - 50) / 50f;
                return BlendColor(Color.FromArgb(246, 164, 38), Color.FromArgb(0, 184, 137), amount);
            }

            return BlendColor(Color.FromArgb(224, 54, 73), Color.FromArgb(246, 164, 38), clamped / 50f);
        }

        private static Color BlendColor(Color from, Color to, float amount)
        {
            float clamped = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb(
                (int)Math.Round(from.A + ((to.A - from.A) * clamped)),
                (int)Math.Round(from.R + ((to.R - from.R) * clamped)),
                (int)Math.Round(from.G + ((to.G - from.G) * clamped)),
                (int)Math.Round(from.B + ((to.B - from.B) * clamped))
            );
        }

        private async Task RefreshUsageAsync()
        {
            if (refreshRunning)
            {
                return;
            }

            refreshRunning = true;
            try
            {
                snapshot = await Task.Run(new Func<UsageSnapshot>(ReadWeeklyUsage));
                Invalidate();
                SaveStatus();
            }
            finally
            {
                refreshRunning = false;
            }
        }

        private static UsageSnapshot ReadWeeklyUsage()
        {
            Process process = null;
            try
            {
                string codexPath = FindCodexExecutable();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = codexPath,
                    Arguments = "app-server",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                process = new Process { StartInfo = startInfo };
                if (!process.Start())
                {
                    throw new InvalidOperationException("Codex App Server did not start.");
                }

                JavaScriptSerializer json = new JavaScriptSerializer();
                WriteMessage(process, json, new Dictionary<string, object>
                {
                    { "method", "initialize" },
                    { "id", 0 },
                    { "params", new Dictionary<string, object>
                        {
                            { "clientInfo", new Dictionary<string, object>
                                {
                                    { "name", "codex_usage_bubble" },
                                    { "title", "Codex Weekly Usage Bubble" },
                                    { "version", "1.0.0" }
                                }
                            }
                        }
                    }
                });

                Dictionary<string, object> message;
                bool initialized = false;
                for (int i = 0; i < 40 && !initialized; i++)
                {
                    message = ReadMessage(process, json, 15000);
                    object id;
                    object result;
                    initialized = message.TryGetValue("id", out id)
                        && Convert.ToInt32(id, CultureInfo.InvariantCulture) == 0
                        && message.TryGetValue("result", out result)
                        && result != null;
                }

                if (!initialized)
                {
                    throw new InvalidOperationException("Codex App Server initialization did not complete.");
                }

                WriteMessage(process, json, new Dictionary<string, object>
                {
                    { "method", "initialized" },
                    { "params", new Dictionary<string, object>() }
                });
                WriteMessage(process, json, new Dictionary<string, object>
                {
                    { "method", "account/rateLimits/read" },
                    { "id", 1 }
                });

                Dictionary<string, object> response = null;
                for (int i = 0; i < 60 && response == null; i++)
                {
                    message = ReadMessage(process, json, 15000);
                    object id;
                    if (message.TryGetValue("id", out id)
                        && Convert.ToInt32(id, CultureInfo.InvariantCulture) == 1)
                    {
                        response = message;
                    }
                }

                if (response == null)
                {
                    throw new InvalidOperationException("Codex App Server did not return rate limits.");
                }

                object error;
                if (response.TryGetValue("error", out error) && error != null)
                {
                    throw new InvalidOperationException("Codex rate-limit query failed.");
                }

                Dictionary<string, object> resultDictionary = GetDictionary(response, "result");
                Dictionary<string, object> bucket = null;
                object bucketsObject;
                if (resultDictionary.TryGetValue("rateLimitsByLimitId", out bucketsObject))
                {
                    Dictionary<string, object> buckets = bucketsObject as Dictionary<string, object>;
                    object codexBucket;
                    if (buckets != null && buckets.TryGetValue("codex", out codexBucket))
                    {
                        bucket = codexBucket as Dictionary<string, object>;
                    }
                }
                if (bucket == null)
                {
                    bucket = GetDictionary(resultDictionary, "rateLimits");
                }

                Dictionary<string, object> weekly = null;
                foreach (string key in new[] { "primary", "secondary" })
                {
                    object windowObject;
                    if (!bucket.TryGetValue(key, out windowObject) || windowObject == null)
                    {
                        continue;
                    }
                    Dictionary<string, object> window = windowObject as Dictionary<string, object>;
                    if (window == null)
                    {
                        continue;
                    }
                    object minutesObject;
                    if (window.TryGetValue("windowDurationMins", out minutesObject)
                        && Math.Abs(Convert.ToDouble(minutesObject, CultureInfo.InvariantCulture) - 10080d) <= 1d)
                    {
                        weekly = window;
                        break;
                    }
                }

                if (weekly == null)
                {
                    throw new InvalidOperationException("The account did not return a seven-day usage window.");
                }

                double usedPercent = Convert.ToDouble(weekly["usedPercent"], CultureInfo.InvariantCulture);
                int remainingPercent = (int)Math.Max(0d, Math.Min(100d, Math.Round(100d - usedPercent)));
                long resetsAt = Convert.ToInt64(weekly["resetsAt"], CultureInfo.InvariantCulture);
                DateTimeOffset resetTime = DateTimeOffset.FromUnixTimeSeconds(resetsAt).ToLocalTime();
                int remainingDays = (int)Math.Max(0d, Math.Ceiling((resetTime - DateTimeOffset.Now).TotalDays));

                return new UsageSnapshot
                {
                    Available = true,
                    RemainingPercent = remainingPercent,
                    RemainingDays = remainingDays,
                    ResetAt = resetTime,
                    UpdatedAt = DateTimeOffset.Now,
                    Error = null
                };
            }
            catch (Exception exception)
            {
                return new UsageSnapshot
                {
                    Available = false,
                    UpdatedAt = DateTimeOffset.Now,
                    Error = exception.Message
                };
            }
            finally
            {
                if (process != null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch
                    {
                    }
                    process.Dispose();
                }
            }
        }

        private static string FindCodexExecutable()
        {
            string configuredPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in pathValue.Split(Path.PathSeparator))
            {
                string trimmedDirectory = directory.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(trimmedDirectory))
                {
                    continue;
                }
                string pathCandidate = Path.Combine(trimmedDirectory, "codex.exe");
                if (File.Exists(pathCandidate))
                {
                    return pathCandidate;
                }
            }

            string extensionsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".vscode",
                "extensions"
            );

            if (Directory.Exists(extensionsRoot))
            {
                string[] extensionDirectories = Directory.GetDirectories(extensionsRoot, "openai.chatgpt-*");
                foreach (string extensionDirectory in extensionDirectories.OrderByDescending(Directory.GetLastWriteTimeUtc))
                {
                    string candidate = Path.Combine(extensionDirectory, "bin", "windows-x86_64", "codex.exe");
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            throw new FileNotFoundException("Could not find an accessible Codex executable.");
        }

        private static void WriteMessage(Process process, JavaScriptSerializer json, Dictionary<string, object> message)
        {
            process.StandardInput.WriteLine(json.Serialize(message));
            process.StandardInput.Flush();
        }

        private static Dictionary<string, object> ReadMessage(Process process, JavaScriptSerializer json, int timeoutMilliseconds)
        {
            Task<string> readTask = process.StandardOutput.ReadLineAsync();
            if (!readTask.Wait(timeoutMilliseconds))
            {
                throw new TimeoutException("Timed out while waiting for Codex App Server.");
            }
            string line = readTask.Result;
            if (line == null)
            {
                throw new EndOfStreamException("Codex App Server closed its output stream.");
            }
            return json.Deserialize<Dictionary<string, object>>(line);
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> parent, string key)
        {
            object value;
            if (!parent.TryGetValue(key, out value) || value == null)
            {
                throw new InvalidDataException("Missing field: " + key);
            }
            Dictionary<string, object> dictionary = value as Dictionary<string, object>;
            if (dictionary == null)
            {
                throw new InvalidDataException("Invalid object field: " + key);
            }
            return dictionary;
        }

        private void SaveStatus()
        {
            try
            {
                JavaScriptSerializer json = new JavaScriptSerializer();
                Dictionary<string, object> status = new Dictionary<string, object>
                {
                    { "processId", Process.GetCurrentProcess().Id },
                    { "windowHandle", Handle.ToInt64() },
                    { "windowTitle", Text },
                    { "isVisible", Visible },
                    { "topmost", TopMost },
                    { "left", Left },
                    { "top", Top },
                    { "width", Width },
                    { "height", Height },
                    { "available", snapshot.Available },
                    { "remainingPercent", snapshot.Available ? (object)snapshot.RemainingPercent : null },
                    { "remainingDays", snapshot.Available ? (object)snapshot.RemainingDays : null },
                    { "resetsAt", snapshot.Available ? (object)snapshot.ResetAt.ToString("o") : null },
                    { "updatedAt", snapshot.UpdatedAt.ToString("o") },
                    { "error", snapshot.Error }
                };
                File.WriteAllText(statusPath, json.Serialize(status));
            }
            catch
            {
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            if (arguments.Length == 3 && string.Equals(arguments[1], "--render-sample", StringComparison.OrdinalIgnoreCase))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (UsageBubbleForm previewForm = new UsageBubbleForm())
                {
                    previewForm.RenderSample(arguments[2]);
                }
                return;
            }

            bool createdNew;
            using (System.Threading.Mutex singleton = new System.Threading.Mutex(true, "Local\\CodexUsageBubble.Singleton", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new UsageBubbleForm());
                GC.KeepAlive(singleton);
            }
        }
    }
}

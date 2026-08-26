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
    internal sealed class UsageWindowSnapshot
    {
        public bool Available;
        public int RemainingPercent;
        public int RemainingDays;
        public DateTimeOffset ResetAt;
    }

    internal sealed class UsageSnapshot
    {
        public UsageWindowSnapshot FiveHour = new UsageWindowSnapshot();
        public UsageWindowSnapshot Weekly = new UsageWindowSnapshot();
        public DateTimeOffset UpdatedAt;
        public string Error;
    }

    internal enum UsageWindowKind
    {
        FiveHour,
        Weekly
    }

    internal sealed class UsageBubbleForm : Form
    {
        private readonly Timer refreshTimer;
        private readonly string statusPath;
        private readonly NotifyIcon trayIcon;
        private UsageSnapshot snapshot;
        private UsageWindowKind selectedWindow;
        private bool selectionInitialized;
        private bool refreshRunning;
        private Icon trayIconImage;

        private const int WmNcLeftButtonDown = 0x00A1;
        private const int HtCaption = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr iconHandle);

        public UsageBubbleForm(bool enableTrayIcon = true)
        {
            Text = "Codex Usage";
            ClientSize = new Size(196, 292);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
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
                UpdatedAt = DateTimeOffset.Now,
                Error = "Loading current usage"
            };
            selectedWindow = UsageWindowKind.FiveHour;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem refreshItem = new ToolStripMenuItem("Refresh now");
            refreshItem.Click += async delegate { await RefreshUsageAsync(); };
            ToolStripMenuItem hideItem = new ToolStripMenuItem("Hide to tray");
            hideItem.Click += delegate { HideToTray(); };
            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit usage bubble");
            exitItem.Click += delegate { ExitApplication(); };
            menu.Items.Add(refreshItem);
            menu.Items.Add(hideItem);
            menu.Items.Add(exitItem);
            ContextMenuStrip = menu;

            if (enableTrayIcon)
            {
                ContextMenuStrip trayMenu = new ContextMenuStrip();
                ToolStripMenuItem showHideItem = new ToolStripMenuItem("Show / Hide bubble");
                showHideItem.Click += delegate { ToggleBubbleVisibility(); };
                ToolStripMenuItem trayRefreshItem = new ToolStripMenuItem("Refresh now");
                trayRefreshItem.Click += async delegate { await RefreshUsageAsync(); };
                ToolStripMenuItem trayExitItem = new ToolStripMenuItem("Exit");
                trayExitItem.Click += delegate { ExitApplication(); };
                trayMenu.Items.Add(showHideItem);
                trayMenu.Items.Add(trayRefreshItem);
                trayMenu.Items.Add(new ToolStripSeparator());
                trayMenu.Items.Add(trayExitItem);

                trayIcon = new NotifyIcon
                {
                    ContextMenuStrip = trayMenu,
                    Text = "Codex usage bubble",
                    Visible = true
                };
                trayIcon.MouseClick += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        ToggleBubbleVisibility();
                    }
                };
                UpdateTrayPresentation();
            }

            ToolTip tip = new ToolTip();
            tip.SetToolTip(this, "Click to switch 5-hour and weekly usage. Right-click for more options.");

            MouseDown += delegate(object sender, MouseEventArgs e)
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
                        ToggleSelectedWindow();
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

            UsageWindowSnapshot activeWindow = GetSelectedUsageWindow();
            int percent = 0;
            string percentText = "…";
            string activeWindowText = selectedWindow == UsageWindowKind.FiveHour ? "5-HOUR LEFT" : "WEEKLY LEFT";
            string fiveHourValueText = "5H …";
            string fiveHourResetText = "Loading";
            string weeklyValueText = "WEEK …";
            string weeklyResetText = "Loading";

            if (activeWindow.Available)
            {
                percent = activeWindow.RemainingPercent;
                percentText = string.Format(CultureInfo.InvariantCulture, "{0}%", percent);
            }
            else if (!string.Equals(snapshot.Error, "Loading current usage", StringComparison.Ordinal))
            {
                percentText = "—";
                activeWindowText = "UNAVAILABLE";
            }

            if (snapshot.FiveHour.Available)
            {
                fiveHourValueText = string.Format(CultureInfo.InvariantCulture, "5H {0}%", snapshot.FiveHour.RemainingPercent);
                fiveHourResetText = "Reset " + snapshot.FiveHour.ResetAt.ToString("h:mm tt", CultureInfo.InvariantCulture);
            }
            else if (!string.Equals(snapshot.Error, "Loading current usage", StringComparison.Ordinal))
            {
                fiveHourValueText = "5H —";
                fiveHourResetText = "Unavailable";
            }

            if (snapshot.Weekly.Available)
            {
                weeklyValueText = string.Format(CultureInfo.InvariantCulture, "WEEK {0}%", snapshot.Weekly.RemainingPercent);
                string daysText = snapshot.Weekly.RemainingDays == 1
                    ? "1 day left"
                    : string.Format(CultureInfo.InvariantCulture, "{0} days left", snapshot.Weekly.RemainingDays);
                weeklyResetText = daysText + " · Reset " + snapshot.Weekly.ResetAt.ToString("MMM d", CultureInfo.InvariantCulture);
            }
            else if (!string.Equals(snapshot.Error, "Loading current usage", StringComparison.Ordinal))
            {
                weeklyValueText = "WEEK —";
                weeklyResetText = "Unavailable";
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

                float fillRatio = activeWindow.Available ? Math.Max(0f, Math.Min(1f, percent / 100f)) : 0.65f;
                float waterTop = sphereBounds.Bottom - (sphereBounds.Height * fillRatio);
                Color usageTone = activeWindow.Available ? GetUsageTone(percent) : Color.FromArgb(0, 184, 137);
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
                    activeWindowText,
                    weeklyFont,
                    new Rectangle(17, 121, 162, 27),
                    Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine
                );
            }

            Rectangle labelBounds = new Rectangle(14, 185, 168, 104);
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

            using (Pen dividerPen = new Pen(Color.FromArgb(35, 29, 51, 49), 1f))
            {
                graphics.DrawLine(dividerPen, 29f, 237f, 167f, 237f);
            }

            using (Font valueFont = new Font("Segoe UI", 13.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font detailFont = new Font("Segoe UI", 10.8f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font weeklyDetailFont = new Font("Segoe UI", 9.8f, FontStyle.Bold, GraphicsUnit.Point))
            using (Brush infoBrush = new SolidBrush(Color.FromArgb(29, 51, 49)))
            using (StringFormat centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(fiveHourValueText, valueFont, infoBrush, new RectangleF(16f, 188f, 164f, 25f), centered);
                graphics.DrawString(fiveHourResetText, detailFont, infoBrush, new RectangleF(16f, 211f, 164f, 23f), centered);
                graphics.DrawString(weeklyValueText, valueFont, infoBrush, new RectangleF(16f, 240f, 164f, 25f), centered);
                graphics.DrawString(weeklyResetText, weeklyDetailFont, infoBrush, new RectangleF(16f, 263f, 164f, 23f), centered);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            refreshTimer.Stop();
            refreshTimer.Dispose();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            if (trayIconImage != null)
            {
                trayIconImage.Dispose();
                trayIconImage = null;
            }
            base.OnFormClosed(e);
        }

        internal void RenderSample(string outputPath)
        {
            snapshot = new UsageSnapshot
            {
                FiveHour = new UsageWindowSnapshot
                {
                    Available = true,
                    RemainingPercent = 57,
                    ResetAt = new DateTimeOffset(2026, 8, 26, 17, 32, 0, TimeSpan.FromHours(8))
                },
                Weekly = new UsageWindowSnapshot
                {
                    Available = true,
                    RemainingPercent = 87,
                    RemainingDays = 7,
                    ResetAt = new DateTimeOffset(2026, 9, 2, 8, 16, 0, TimeSpan.FromHours(8))
                },
                UpdatedAt = DateTimeOffset.Now,
                Error = null
            };
            selectedWindow = UsageWindowKind.FiveHour;
            selectionInitialized = true;

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

        private UsageWindowSnapshot GetSelectedUsageWindow()
        {
            return selectedWindow == UsageWindowKind.FiveHour ? snapshot.FiveHour : snapshot.Weekly;
        }

        private void ToggleSelectedWindow()
        {
            if (!snapshot.FiveHour.Available || !snapshot.Weekly.Available)
            {
                return;
            }

            selectedWindow = selectedWindow == UsageWindowKind.FiveHour
                ? UsageWindowKind.Weekly
                : UsageWindowKind.FiveHour;
            selectionInitialized = true;
            UpdateTrayPresentation();
            Invalidate();
            SaveStatus();
        }

        private void SelectInitialWindow()
        {
            if (snapshot.FiveHour.Available && snapshot.Weekly.Available)
            {
                selectedWindow = snapshot.FiveHour.RemainingPercent <= snapshot.Weekly.RemainingPercent
                    ? UsageWindowKind.FiveHour
                    : UsageWindowKind.Weekly;
            }
            else if (snapshot.FiveHour.Available)
            {
                selectedWindow = UsageWindowKind.FiveHour;
            }
            else if (snapshot.Weekly.Available)
            {
                selectedWindow = UsageWindowKind.Weekly;
            }
            selectionInitialized = snapshot.FiveHour.Available || snapshot.Weekly.Available;
        }

        private void EnsureSelectedWindowAvailable()
        {
            if (!selectionInitialized)
            {
                SelectInitialWindow();
                return;
            }

            if (selectedWindow == UsageWindowKind.FiveHour && !snapshot.FiveHour.Available && snapshot.Weekly.Available)
            {
                selectedWindow = UsageWindowKind.Weekly;
            }
            else if (selectedWindow == UsageWindowKind.Weekly && !snapshot.Weekly.Available && snapshot.FiveHour.Available)
            {
                selectedWindow = UsageWindowKind.FiveHour;
            }
        }

        private void HideToTray()
        {
            Hide();
            SaveStatus();
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            SaveStatus();
        }

        private void ToggleBubbleVisibility()
        {
            if (Visible)
            {
                HideToTray();
            }
            else
            {
                ShowFromTray();
            }
        }

        private void ExitApplication()
        {
            Close();
        }

        private void UpdateTrayPresentation()
        {
            if (trayIcon == null)
            {
                return;
            }

            string fiveHourText = snapshot.FiveHour.Available
                ? string.Format(CultureInfo.InvariantCulture, "5H {0}%", snapshot.FiveHour.RemainingPercent)
                : "5H unavailable";
            string weeklyText = snapshot.Weekly.Available
                ? string.Format(CultureInfo.InvariantCulture, "Week {0}%", snapshot.Weekly.RemainingPercent)
                : "Week unavailable";
            trayIcon.Text = "Codex: " + fiveHourText + ", " + weeklyText;

            UsageWindowSnapshot activeWindow = GetSelectedUsageWindow();
            Color tone = activeWindow.Available
                ? GetUsageTone(activeWindow.RemainingPercent)
                : Color.FromArgb(0, 184, 137);
            Icon newIcon = CreateTrayIcon(tone);
            Icon previousIcon = trayIconImage;
            trayIconImage = newIcon;
            trayIcon.Icon = newIcon;
            if (previousIcon != null)
            {
                previousIcon.Dispose();
            }
        }

        private static Icon CreateTrayIcon(Color tone)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                Rectangle bounds = new Rectangle(3, 3, 26, 26);
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    bounds,
                    BlendColor(tone, Color.White, 0.45f),
                    BlendColor(tone, Color.Black, 0.28f),
                    LinearGradientMode.Vertical))
                {
                    graphics.FillEllipse(brush, bounds);
                }
                using (Pen rim = new Pen(BlendColor(tone, Color.White, 0.72f), 2f))
                {
                    graphics.DrawEllipse(rim, bounds);
                }
                using (SolidBrush highlight = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                {
                    graphics.FillEllipse(highlight, new Rectangle(8, 7, 13, 7));
                }

                IntPtr iconHandle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(iconHandle).Clone();
                }
                finally
                {
                    DestroyIcon(iconHandle);
                }
            }
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
                snapshot = await Task.Run(new Func<UsageSnapshot>(ReadUsage));
                EnsureSelectedWindowAvailable();
                UpdateTrayPresentation();
                Invalidate();
                SaveStatus();
            }
            finally
            {
                refreshRunning = false;
            }
        }

        private static UsageSnapshot ReadUsage()
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
                                    { "title", "Codex Usage Bubble" },
                                    { "version", "1.1.0" }
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

                return CreateSnapshotFromRateLimitBucket(bucket);
            }
            catch (Exception exception)
            {
                return new UsageSnapshot
                {
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

        private static UsageSnapshot CreateSnapshotFromRateLimitBucket(Dictionary<string, object> bucket)
        {
            UsageSnapshot result = new UsageSnapshot
            {
                UpdatedAt = DateTimeOffset.Now,
                Error = null
            };

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
                if (!window.TryGetValue("windowDurationMins", out minutesObject) || minutesObject == null)
                {
                    continue;
                }

                double durationMinutes = Convert.ToDouble(minutesObject, CultureInfo.InvariantCulture);
                UsageWindowSnapshot parsedWindow;
                try
                {
                    parsedWindow = ParseUsageWindow(window);
                }
                catch
                {
                    continue;
                }

                if (Math.Abs(durationMinutes - 300d) <= 1d)
                {
                    result.FiveHour = parsedWindow;
                }
                else if (Math.Abs(durationMinutes - 10080d) <= 1d)
                {
                    result.Weekly = parsedWindow;
                }
            }

            if (!result.FiveHour.Available && !result.Weekly.Available)
            {
                throw new InvalidOperationException("The account did not return a 5-hour or seven-day usage window.");
            }

            return result;
        }

        private static UsageWindowSnapshot ParseUsageWindow(Dictionary<string, object> window)
        {
            double usedPercent = Convert.ToDouble(window["usedPercent"], CultureInfo.InvariantCulture);
            int remainingPercent = (int)Math.Max(0d, Math.Min(100d, Math.Round(100d - usedPercent)));
            long resetsAt = Convert.ToInt64(window["resetsAt"], CultureInfo.InvariantCulture);
            DateTimeOffset resetTime = DateTimeOffset.FromUnixTimeSeconds(resetsAt).ToLocalTime();
            int remainingDays = (int)Math.Max(0d, Math.Ceiling((resetTime - DateTimeOffset.Now).TotalDays));
            return new UsageWindowSnapshot
            {
                Available = true,
                RemainingPercent = remainingPercent,
                RemainingDays = remainingDays,
                ResetAt = resetTime
            };
        }

        private static string FindCodexExecutable()
        {
            string configuredPath = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                return configuredPath;
            }

            string codexInstallRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI",
                "Codex",
                "bin"
            );
            if (Directory.Exists(codexInstallRoot))
            {
                string[] installedCandidates = Directory.GetDirectories(codexInstallRoot)
                    .Select(directory => Path.Combine(directory, "codex.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToArray();
                if (installedCandidates.Length > 0)
                {
                    return installedCandidates[0];
                }

                string stableCandidate = Path.Combine(codexInstallRoot, "codex.exe");
                if (File.Exists(stableCandidate))
                {
                    return stableCandidate;
                }
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
                bool available = snapshot.FiveHour.Available || snapshot.Weekly.Available;
                Dictionary<string, object> status = new Dictionary<string, object>
                {
                    { "processId", Process.GetCurrentProcess().Id },
                    { "windowHandle", Handle.ToInt64() },
                    { "windowTitle", Text },
                    { "isVisible", Visible },
                    { "showInTaskbar", ShowInTaskbar },
                    { "topmost", TopMost },
                    { "left", Left },
                    { "top", Top },
                    { "width", Width },
                    { "height", Height },
                    { "available", available },
                    { "selectedWindow", selectedWindow == UsageWindowKind.FiveHour ? "fiveHour" : "weekly" },
                    { "hiddenToTray", !Visible },
                    { "trayIconVisible", trayIcon != null && trayIcon.Visible },
                    { "remainingPercent", snapshot.Weekly.Available ? (object)snapshot.Weekly.RemainingPercent : null },
                    { "remainingDays", snapshot.Weekly.Available ? (object)snapshot.Weekly.RemainingDays : null },
                    { "resetsAt", snapshot.Weekly.Available ? (object)snapshot.Weekly.ResetAt.ToString("o") : null },
                    { "fiveHour", CreateWindowStatus(snapshot.FiveHour, false) },
                    { "weekly", CreateWindowStatus(snapshot.Weekly, true) },
                    { "updatedAt", snapshot.UpdatedAt.ToString("o") },
                    { "error", snapshot.Error }
                };
                File.WriteAllText(statusPath, json.Serialize(status));
            }
            catch
            {
            }
        }

        private static Dictionary<string, object> CreateWindowStatus(UsageWindowSnapshot window, bool includeRemainingDays)
        {
            Dictionary<string, object> result = new Dictionary<string, object>
            {
                { "available", window.Available },
                { "remainingPercent", window.Available ? (object)window.RemainingPercent : null },
                { "resetsAt", window.Available ? (object)window.ResetAt.ToString("o") : null }
            };
            if (includeRemainingDays)
            {
                result.Add("remainingDays", window.Available ? (object)window.RemainingDays : null);
            }
            return result;
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
                using (UsageBubbleForm previewForm = new UsageBubbleForm(false))
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

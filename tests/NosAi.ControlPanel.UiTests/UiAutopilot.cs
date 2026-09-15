using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Perception;

namespace NosAi.ControlPanel.UiTests
{
    public sealed record UiAutopilotUnavailable(string Reason);

    public sealed class UiAutopilot : IDisposable
    {
        // SendInput clicks whatever is physically under the cursor, regardless of
        // which window has logical focus: without bringing the target window to the
        // foreground first, the click can land on whatever happens to be on top of
        // the screen at that pixel (found on the first real run: the click "succeeded"
        // but PageTitle never changed, because it never reached the ControlPanel).
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly Process? _process;
        private readonly bool _ownsProcess;
        private readonly AutomationElement _window;
        private readonly DxgiDesktopDuplicationSource? _capture;
        private readonly Win32InputBackend _inputBackend;
        private bool _disposed;

        private UiAutopilot(Process? process, bool ownsProcess, AutomationElement window, DxgiDesktopDuplicationSource? capture)
        {
            _process = process;
            _ownsProcess = ownsProcess;
            _window = window;
            _capture = capture;
            _inputBackend = new Win32InputBackend();
        }

        public static bool TryLaunch(string exePath, TimeSpan startupTimeout, out UiAutopilot? autopilot, out UiAutopilotUnavailable? unavailable)
        {
            autopilot = null;
            unavailable = null;

            if (!OperatingSystem.IsWindows())
            {
                unavailable = new UiAutopilotUnavailable("not_windows");
                return false;
            }

            var process = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
            if (process == null)
            {
                unavailable = new UiAutopilotUnavailable("process_start_failed");
                return false;
            }

            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < startupTimeout)
            {
                if (process.HasExited)
                {
                    unavailable = new UiAutopilotUnavailable("process_exited_before_window");
                    process.Dispose();
                    return false;
                }

                var window = AutomationElement.RootElement.FindFirst(
                    TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));

                if (window != null)
                {
                    DxgiDesktopDuplicationSource? capture = null;
                    CaptureUnavailable? captureUnavailable = null;
                    bool captureCreated = DxgiDesktopDuplicationSource.TryCreate(
                        out capture, out captureUnavailable, 0, 0, 250, () => DateTime.UtcNow);

                    if (!captureCreated)
                    {
                        // We still return a valid autopilot even if capture fails
                        // This is an optional feature, not a requirement
                    }

                    autopilot = new UiAutopilot(process, true, window, capture);
                    return true;
                }

                System.Threading.Thread.Sleep(100);
            }

            // Timeout reached
            unavailable = new UiAutopilotUnavailable("window_not_found_within_timeout");
            process.Kill();
            process.WaitForExit();
            process.Dispose();
            return false;
        }

        public static bool TryAttach(string processName, TimeSpan timeout, out UiAutopilot? autopilot, out UiAutopilotUnavailable? unavailable)
        {
            autopilot = null;
            unavailable = null;

            if (!OperatingSystem.IsWindows())
            {
                unavailable = new UiAutopilotUnavailable("not_windows");
                return false;
            }

            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            var window = AutomationElement.RootElement.FindFirst(
                                TreeScope.Children,
                                new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));

                            if (window != null)
                            {
                                // Try to create capture source
                                DxgiDesktopDuplicationSource? capture = null;
                                CaptureUnavailable? captureUnavailable = null;
                                bool captureCreated = DxgiDesktopDuplicationSource.TryCreate(
                                    out capture, out captureUnavailable, 0, 0, 250, () => DateTime.UtcNow);

                                if (!captureCreated)
                                {
                                    // We still return a valid autopilot even if capture fails
                                    // This is an optional feature, not a requirement
                                }

                                autopilot = new UiAutopilot(process, false, window, capture);
                                return true;
                            }
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Process might have exited or become inaccessible
                        continue;
                    }
                }

                System.Threading.Thread.Sleep(100);
            }

            unavailable = new UiAutopilotUnavailable("process_not_found");
            return false;
        }

        public bool TryFindByAutomationId(string automationId, TimeSpan timeout, out AutomationElement? element)
        {
            element = null;

            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout)
            {
                element = _window.FindFirst(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));

                if (element != null)
                {
                    return true;
                }

                System.Threading.Thread.Sleep(100);
            }

            return false;
        }

        public bool TryClickPhysical(AutomationElement element)
        {
            var boundingRect = element.Current.BoundingRectangle;
            if (boundingRect == System.Windows.Rect.Empty || element.Current.IsOffscreen)
            {
                return false;
            }

            SetForegroundWindow(new IntPtr(_window.Current.NativeWindowHandle));
            System.Threading.Thread.Sleep(50);

            int centerX = (int)Math.Round(boundingRect.Left + boundingRect.Width / 2.0);
            int centerY = (int)Math.Round(boundingRect.Top + boundingRect.Height / 2.0);

            if (!_inputBackend.MoveAbsolute(centerX, centerY))
            {
                return false;
            }

            return _inputBackend.Click(MouseButton.Left);
        }

        public string GetName(AutomationElement element)
        {
            return element.Current.Name ?? string.Empty;
        }

        public bool TryCaptureWindowScreenshot(out byte[]? pngBytes)
        {
            pngBytes = null;

            if (_capture == null)
            {
                return false;
            }

            if (!_capture.TryAcquire(out var frame))
            {
                return false;
            }

            var windowRect = _window.Current.BoundingRectangle;
            
            // Check if window rectangle is valid and within frame bounds
            if (windowRect.Left < 0 || windowRect.Top < 0 || 
                windowRect.Right > frame.Width || windowRect.Bottom > frame.Height)
            {
                return false;
            }

            int width = (int)windowRect.Width;
            int height = (int)windowRect.Height;
            int startX = (int)windowRect.Left;
            int startY = (int)windowRect.Top;

            // Create cropped buffer
            byte[] croppedBytes = new byte[width * height * 4];
            int srcStride = frame.Width * 4;
            int dstStride = width * 4;

            ReadOnlySpan<byte> srcSpan = frame.Bgra.Span;
            for (int y = 0; y < height; y++)
            {
                int srcOffset = (startY + y) * srcStride + startX * 4;
                int dstOffset = y * dstStride;
                srcSpan.Slice(srcOffset, dstStride).CopyTo(croppedBytes.AsSpan(dstOffset, dstStride));
            }

            // Convert to PNG
            try
            {
                var bitmapSource = BitmapSource.Create(
                    width, height, 96, 96, 
                    System.Windows.Media.PixelFormats.Bgra32, 
                    null, croppedBytes, dstStride);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                
                using var ms = new MemoryStream();
                encoder.Save(ms);
                pngBytes = ms.ToArray();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            _capture?.Dispose();

            if (_ownsProcess && _process is not null)
            {
                try
                {
                    _process.CloseMainWindow();
                    if (!_process.WaitForExit(2000))
                    {
                        _process.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already exited: nothing left to close.
                }
            }
        }
    }
}

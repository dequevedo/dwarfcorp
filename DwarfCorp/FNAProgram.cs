using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using DwarfCorp.GameStates;
using DwarfCorp.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using ZLogger;

namespace DwarfCorpCore
{
    
}

namespace DwarfCorp
{
#if WINDOWS || XBOX
    internal static class Program
    {
        public static string Version = "21.04.03_FNA";
        public static string[] CompatibleVersions = { "21.01.26_XNA", "21.01.26_FNA", "20.12.10_XNA", "20.12.10_FNA", "21.04.03_FNA", "21.04.03_XNA" };
        public static string Commit = "UNKNOWN";
        public static char DirChar = Path.DirectorySeparatorChar;
        
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        private static void Main(string[] args)
        {
            try
            {
                var cwd = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase);
                Directory.SetCurrentDirectory(new Uri(cwd).LocalPath);
                using (Stream stream = new FileStream("version.txt", FileMode.Open))
                using (StreamReader reader = new StreamReader(stream))
                    Commit = reader.ReadToEnd();
                Commit = Commit.Trim();
            }
            catch (Exception e) { Console.Error.WriteLine($"Failed to read version.txt: {e.Message}"); }
            System.Net.ServicePointManager.ServerCertificateValidationCallback = SSLCallback;

            // Global crash-capture handlers — installed unconditionally (no #if guard) so
            // Debug builds also get a crash log when the game dies unexpectedly. Managed
            // exceptions hit AppDomain.UnhandledException; ProcessExit always fires and
            // flushes our breadcrumb trail, which covers native fatal crashes too (heap
            // corruption, GL driver aborts) that don't raise managed exceptions at all.
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception
                        ?? new Exception("Non-Exception object: " + (e.ExceptionObject?.ToString() ?? "null"));
                    CrashBreadcrumbs.Push("AppDomain.UnhandledException: " + ex.GetType().Name + " — " + ex.Message);
                    WriteExceptionLog(ex);
                }
                catch { /* swallow — already crashing */ }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                try
                {
                    CrashBreadcrumbs.Push("TaskScheduler.UnobservedTaskException: " + e.Exception.Message);
                    WriteExceptionLog(e.Exception);
                    e.SetObserved();
                }
                catch { /* swallow */ }
            };
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try
                {
                    var path = DwarfGame.GetGameDirectory() + Path.DirectorySeparatorChar
                        + "Logging" + Path.DirectorySeparatorChar + "breadcrumbs_last.txt";
                    CrashBreadcrumbs.Push("ProcessExit");
                    CrashBreadcrumbs.Flush(path);
                }
                catch { /* swallow */ }
            };

            // Every single exception throw — even those caught and swallowed — gets a breadcrumb.
            // Crucial for debugging crashes whose real cause is a silently-handled exception
            // several stack frames away from the eventual native AV. ThreadAbortException and
            // OperationCanceledException are intentionally ignored (too noisy, usually benign).
            AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            {
                if (e.Exception is System.Threading.ThreadAbortException) return;
                if (e.Exception is System.OperationCanceledException) return;
                try
                {
                    var msg = e.Exception.Message ?? "<no msg>";
                    var newlineIdx = msg.IndexOf('\n');
                    if (newlineIdx > 0) msg = msg.Substring(0, newlineIdx);
                    CrashBreadcrumbs.Push("FirstChance: " + e.Exception.GetType().Name + ": " + msg);
                }
                catch { /* never throw from an exception handler */ }
            };

            // Periodic flush of the breadcrumb ring buffer, so a native-fatal crash that skips
            // the managed ProcessExit handler still leaves recent history on disk. Writes to a
            // separate "breadcrumbs_current.txt" so as not to clobber breadcrumbs_last.txt.
            var breadcrumbFlushPath = DwarfGame.GetGameDirectory() + Path.DirectorySeparatorChar
                + "Logging" + Path.DirectorySeparatorChar + "breadcrumbs_current.txt";
            _breadcrumbFlushTimer = new System.Threading.Timer(_ =>
            {
                try { CrashBreadcrumbs.Flush(breadcrumbFlushPath); } catch { }
            }, null, 5000, 5000);

            CrashBreadcrumbs.Push("Main start — v" + Version + " commit " + Commit);

            // L.2: bring up DI + structured logging before any game code runs. After
            // this point every subsystem can resolve ILogger<T> via Services.GetLogger<T>()
            // — raw Console.WriteLine / LogWriter stays around for now but is deprecated.
            Services.Initialize();
            var _startupLog = Services.GetLogger("DwarfCorp.Startup");
            _startupLog.ZLogInformation($"DwarfCorp starting — v{Version} commit {Commit}");
#if CREATE_CRASH_LOGS
            try
#endif
#if !DEBUG
            try
#endif
            {

                Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
                //fbDeprofiler.DeProfiler.Load();
                using (DwarfGame game = new DwarfGame())
                {
                    game.Run();
                }

                SignalShutdown();
            }
#if CREATE_CRASH_LOGS
            catch (Exception exception)
            {
                WriteExceptionLog(exception);
            }
#endif
#if !DEBUG
            catch (Exception exception)
            {
                ShowErrorMessageBox("Unhandled Exception!",
                    String.Format("An unhandled exception occurred in DwarfCorp. This has been reported to Completely Fair Games LLC.\n {0}", exception.ToString()));
                WriteExceptionLog(exception);
            }
#endif
        }

        public static void WriteExceptionLog(Exception exception)
        {
            SignalShutdown();
            DirectoryInfo worldDirectory = Directory.CreateDirectory(DwarfGame.GetGameDirectory() + Path.DirectorySeparatorChar + "Logging");
            var crashPath = worldDirectory.FullName + Path.DirectorySeparatorChar
                + DateTime.Now.ToString("yyyyMMddHHmmssffff") + "_" + "Crashlog.txt";
            StreamWriter file = new StreamWriter(crashPath, true);
            file.WriteLine("DwarfCorp Version " + Version + " (commit " + Commit + ")");
            file.WriteLine("Crash UTC: " + DateTime.UtcNow.ToString("o"));
            OperatingSystem os = Environment.OSVersion;
            if (os != null)
            {
                file.WriteLine("OS Version: " + os.Version);
                file.WriteLine("OS Platform: " + os.Platform);
                file.WriteLine("OS SP: " + os.ServicePack);
                file.WriteLine("OS Version String: " + os.VersionString);
            }

            if (GameState.Game != null && GameState.Game.GraphicsDevice != null)
            {
                GraphicsAdapter adapter = GameState.Game.GraphicsDevice.Adapter;
                file.WriteLine("Graphics Card: " + adapter.DeviceName + "->" + adapter.Description);
                file.WriteLine("Display Mode: " + adapter.CurrentDisplayMode.Width + "x" + adapter.CurrentDisplayMode.Height + " (" + adapter.CurrentDisplayMode.AspectRatio + ")");
                file.WriteLine("Supported display modes: ");

                foreach (var mode in adapter.SupportedDisplayModes)
                {
                    file.WriteLine(mode.Width + "x" + mode.Height + " (" + mode.AspectRatio + ")");
                }
            }

            // Exception + stack trace
            file.WriteLine();
            file.WriteLine("--- Exception ---");
            file.WriteLine(exception.ToString());

            // Breadcrumbs leading up to the crash (best effort — swallow errors so we never
            // double-fault while logging a crash).
            try
            {
                file.WriteLine();
                file.WriteLine("--- Breadcrumbs (most recent last) ---");
                foreach (var line in CrashBreadcrumbs.DumpToLines())
                    file.WriteLine(line);
            }
            catch (Exception e) { file.WriteLine("Breadcrumb dump failed: " + e.Message); }

            // PerformanceMonitor metrics + recent frames, if available.
            try
            {
                file.WriteLine();
                file.WriteLine("--- Metrics ---");
                foreach (var m in PerformanceMonitor.EnumerateMetrics())
                    file.WriteLine(m.Key + " = " + (m.Value == null ? "null" : m.Value.ToString()));

                file.WriteLine();
                file.WriteLine("--- Top functions (last captured frame, µs) ---");
                var swFreq = System.Diagnostics.Stopwatch.Frequency;
                foreach (var f in PerformanceMonitor.GetLastFrameFunctions())
                {
                    long micros = swFreq > 0 ? (f.FrameTicks * 1_000_000L) / swFreq : 0;
                    file.WriteLine(f.Name + " — " + f.FrameCalls + " calls, " + micros + " µs");
                }

                file.WriteLine();
                file.WriteLine("--- Frame history (oldest→newest, last 60) ---");
                file.WriteLine("t_seconds,fps,frame_ms");
                foreach (var s in PerformanceMonitor.GetFrameHistory(60))
                    file.WriteLine(s.WallClockSeconds.ToString("F3") + "," + s.Fps.ToString("F1") + "," + s.FrameTimeMs.ToString("F3"));
            }
            catch (Exception e) { file.WriteLine("Metrics dump failed: " + e.Message); }

            file.Close();

            // Also flush breadcrumbs to the canonical last-session file so the ProcessExit
            // handler doesn't overwrite with a trimmed trail.
            try { CrashBreadcrumbs.Flush(worldDirectory.FullName + Path.DirectorySeparatorChar + "breadcrumbs_last.txt"); }
            catch { /* swallow */ }

            throw exception;
        }

        public static string CreatePath(params string[] args)
        {
            string toReturn = "";

            for(int i = 0; i < args.Length; i++)
            {
                toReturn += args[i];

                if(i < args.Length - 1)
                {
                    toReturn += DirChar;
                }
            }

            return toReturn;
        }

        public static ManualResetEvent ShutdownEvent = new ManualResetEvent(false);

        // Held here (not locally in Main) so the Timer isn't GC'd while Main is still running.
        private static System.Threading.Timer _breadcrumbFlushTimer;

        public static void SignalShutdown()
        {
            DwarfGame.ExitGame = true;
            ShutdownEvent.Set();
        }

        // This is a very dangerous hack which forces DwarfCorp to accept all SSL certificates. This is to enable crash reporting on mac/linux.
        public static bool SSLCallback(System.Object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        public static void CaptureException(Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
#if DEBUG
            throw exception;
#endif
        }

        public static void CaptureSentryMessage(String Message)
        {
            Console.Error.WriteLine(Message);
        }


        public static void LogSentryBreadcrumb(string category, string message, BreadcrumbLevel level = BreadcrumbLevel.Info)
        {
            var line = String.Format("[{0}] {1} : {2}", level, category, message);
            Console.Out.WriteLine(line);
            CrashBreadcrumbs.Push(line);
        }

        public static bool ShowErrorDialog(String Message)
        {
            return true;
        }

        // Minimal native message box for Windows. Replaces the FNA-era
        // SDL2.SDL_ShowSimpleMessageBox call used on unhandled crashes in
        // release builds. Silently no-ops if the native call fails.
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        private const uint MB_OK = 0x0;
        private const uint MB_ICONERROR = 0x10;

        public static void ShowErrorMessageBox(string title, string body)
        {
            try
            {
                MessageBoxW(IntPtr.Zero, body, title, MB_OK | MB_ICONERROR);
            }
            catch
            {
                // Best effort. If user32 isn't reachable (non-Windows runtime with this
                // WindowsDX build shouldn't happen, but defend anyway), just fall through.
            }
        }
    }
#endif
        }
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace DwarfCorp
{
    public enum BreadcrumbLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Critical
    }

    /// <summary>
    /// Lightweight ring-buffer of timestamped breadcrumbs that we can dump when the game
    /// crashes. Works for both managed exceptions (via FNAProgram.WriteExceptionLog) and
    /// native/fatal crashes that skip managed exception handlers — a background flush on
    /// ProcessExit writes them to <c>Logging/breadcrumbs_last.txt</c>.
    ///
    /// Very intentionally cheap: lock + linked list truncation. Safe to call from any
    /// thread. Do NOT spam this from per-frame hot paths.
    /// </summary>
    public static class CrashBreadcrumbs
    {
        // 500 entries leaves enough room for a FirstChanceException storm without losing the
        // breadcrumbs that led up to a crash. Linked-list truncation is O(1) so size doesn't cost.
        private const int Capacity = 500;
        private static readonly LinkedList<string> Entries = new LinkedList<string>();
        private static readonly object Lock = new object();

        public static void Push(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            var t = Thread.CurrentThread;
            var tid = string.IsNullOrEmpty(t.Name) ? t.ManagedThreadId.ToString() : t.Name;
            var line = string.Format("[{0:HH:mm:ss.fff}] [T:{1}] {2}", DateTime.Now, tid, message);
            lock (Lock)
            {
                Entries.AddLast(line);
                while (Entries.Count > Capacity) Entries.RemoveFirst();
            }
        }

        public static List<string> DumpToLines()
        {
            lock (Lock)
                return new List<string>(Entries);
        }

        /// <summary>
        /// Writes the current breadcrumb trail to <paramref name="path"/>. Called on
        /// ProcessExit so it captures even native-fatal sessions.
        /// </summary>
        public static void Flush(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                using (var sw = new StreamWriter(path, false, Encoding.UTF8))
                {
                    sw.WriteLine("# DwarfCorp breadcrumb trail — last session");
                    sw.WriteLine("# flushed_utc," + DateTime.UtcNow.ToString("o"));
                    sw.WriteLine();
                    foreach (var line in DumpToLines())
                        sw.WriteLine(line);
                }
            }
            catch
            {
                // Never throw from a crash handler — we're already in trouble.
            }
        }
    }
}

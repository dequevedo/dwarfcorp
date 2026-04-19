using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace DwarfCorp.Infrastructure
{
    /// <summary>
    /// Process-wide DI container + logger factory for DwarfCorp.
    ///
    /// Bootstrapped from Program.Main before any game code runs so that
    /// every subsequent system can resolve <see cref="ILogger{T}"/> or
    /// any registered singleton without reaching for static globals.
    ///
    /// This is intentionally small: one container, no scoping, no
    /// configuration provider. Enough to take logging off of raw
    /// Console.WriteLine and LogWriter and replace them piecewise.
    /// Feature registrations (WorldManager, Pathfinder, ChunkManager…)
    /// land here as each subsystem migrates.
    /// </summary>
    public static class Services
    {
        private static IServiceProvider _provider;
        private static ILoggerFactory _loggerFactory;

        public static IServiceProvider Provider =>
            _provider ?? throw new InvalidOperationException("Services.Initialize() not called");

        /// <summary>
        /// Build the container. Idempotent — calling twice is a no-op.
        /// Call from Program.Main before DwarfGame is constructed.
        /// </summary>
        public static void Initialize()
        {
            if (_provider != null) return;

            var services = new ServiceCollection();

            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddZLoggerConsole();
                logging.AddZLoggerFile(ResolveLogPath());
            });

            // Future: world/chunk/pathfinder/etc. singletons register here
            // as their subsystems migrate to the new stack.

            _provider = services.BuildServiceProvider();
            _loggerFactory = _provider.GetRequiredService<ILoggerFactory>();
        }

        /// <summary>Get a category logger. Fully static — no instance needed.</summary>
        public static ILogger<T> GetLogger<T>() =>
            Provider.GetRequiredService<ILogger<T>>();

        /// <summary>Get a logger with a named category — useful for static classes
        /// (like Program) that can't be used as a generic type argument.</summary>
        public static ILogger GetLogger(string categoryName) =>
            _loggerFactory.CreateLogger(categoryName);

        /// <summary>Shut down the host cleanly so ZLogger flushes its buffers.</summary>
        public static void Shutdown()
        {
            if (_provider is IDisposable d) d.Dispose();
            _provider = null;
            _loggerFactory = null;
        }

        private static string ResolveLogPath()
        {
            // Best-effort: drop the log next to the save directory. If the
            // game hasn't wired that up yet (e.g. during tests), fall back
            // to the temp dir so Initialize never throws.
            try
            {
                string dir = DwarfGame.GetGameDirectory();
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "dwarfcorp.log");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "dwarfcorp.log");
            }
        }
    }
}

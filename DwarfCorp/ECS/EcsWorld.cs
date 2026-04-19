using System;
using Arch.Core;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace DwarfCorp.ECS
{
    /// <summary>
    /// DwarfCorp's root Arch ECS world. Singleton, owned by the DI container,
    /// lifecycle tied to a <see cref="DwarfCorp.WorldManager"/> game session.
    ///
    /// L.4 foundation only. The legacy <see cref="ComponentManager"/> keeps owning
    /// all entities right now — this type just stands up Arch alongside it so
    /// subsystem migrations (Transform first, then Physics, Health, AI…) have a
    /// target to land on one at a time. The eventual goal is that every
    /// GameComponent-family type has an Arch equivalent and ComponentManager is
    /// deleted. See TODO_LIST item 32 for the cutover plan.
    /// </summary>
    public sealed class EcsWorld : IDisposable
    {
        /// <summary>
        /// Raw Arch world. Exposed so systems can create queries / components
        /// against it directly. We don't try to wrap Arch's fluent API here —
        /// the whole point of choosing Arch was getting its ergonomic API; a
        /// thick wrapper would just re-hide it.
        /// </summary>
        public World World { get; }

        private readonly ILogger<EcsWorld> _log;
        private bool _disposed;

        public EcsWorld(ILogger<EcsWorld> log)
        {
            _log = log;
            World = World.Create();
            _log.ZLogInformation($"Arch World created (id={World.Id}, capacity={World.Capacity})");
        }

        /// <summary>Entity count — cheap call, useful for diagnostics panels.</summary>
        public int Size => World.Size;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                World.Dispose();
                _log.ZLogInformation($"Arch World disposed");
            }
            catch (Exception ex)
            {
                _log.ZLogError($"Arch World dispose threw: {ex.Message}");
            }
        }
    }
}

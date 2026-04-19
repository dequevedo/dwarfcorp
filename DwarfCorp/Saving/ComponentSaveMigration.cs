using System;
using Arch.Core;
using DwarfCorp.ECS;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace DwarfCorp.Saving
{
    /// <summary>
    /// Reads a legacy v1 <see cref="ComponentManager.ComponentSaveData"/> JSON blob and
    /// populates an <see cref="EcsWorld"/> with equivalent Arch entities, one component
    /// family at a time.
    ///
    /// This is the L.4 save-migration shim skeleton. Right now it only sets up the
    /// control flow — each GameComponent family (Transform, Physics, Health, AI,
    /// inventory, animation, …) gets its own translation method in later commits as
    /// the matching Arch component type lands. When all families are covered, the
    /// old <see cref="ComponentManager"/> code is deleted and this file becomes the
    /// one-time upgrade path for saved games.
    ///
    /// Idempotency contract: calling <see cref="Migrate"/> with a v1 save twice yields
    /// the same set of Arch entities both times. The xUnit harness pins this behaviour
    /// before any actual translation lands so we can't accidentally break it while
    /// we're wiring up the per-component code.
    ///
    /// Not used by the live game yet — <see cref="MetaData.SaveFormatVersion"/> stays
    /// at v1 and SaveGame keeps the old path. Swap in when all component families are
    /// covered.
    /// </summary>
    public sealed class ComponentSaveMigration
    {
        private readonly EcsWorld _target;
        private readonly ILogger<ComponentSaveMigration> _log;

        public ComponentSaveMigration(EcsWorld target, ILogger<ComponentSaveMigration> log)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _log = log;
        }

        /// <summary>
        /// Translate a legacy <see cref="ComponentManager.ComponentSaveData"/> into Arch entities
        /// on <paramref name="_target"/>. Returns the number of entities created so callers can
        /// log the delta.
        ///
        /// Currently a no-op beyond logging — per-family migrations land in later commits.
        /// </summary>
        public int Migrate(ComponentManager.ComponentSaveData legacy)
        {
            if (legacy == null)
            {
                _log.ZLogWarning($"Migrate called with null legacy payload — nothing to do");
                return 0;
            }

            int created = 0;
            int skipped = 0;

            // Per-family hooks. Each one is a separate commit's worth of work; listing
            // them here as TODOs so the shape of the eventual implementation is visible
            // and so the review of per-family commits can check itself against this list.
            //
            // TODO: MigrateTransforms(legacy, ref created, ref skipped);
            // TODO: MigratePhysics(legacy, ref created, ref skipped);
            // TODO: MigrateHealth(legacy, ref created, ref skipped);
            // TODO: MigrateInventory(legacy, ref created, ref skipped);
            // TODO: MigrateAI(legacy, ref created, ref skipped);
            // TODO: MigrateAnimation(legacy, ref created, ref skipped);
            // TODO: MigrateRendering(legacy, ref created, ref skipped);
            // TODO: MigrateFire / Sensor / Health / Equipment / Light / Tint … (rest of the 25)

            _log.ZLogInformation(
                $"ComponentSaveMigration: {created} entities created, {skipped} components skipped. " +
                $"(per-family migrations not yet implemented — the legacy ComponentManager still loads saves)");

            return created;
        }

        /// <summary>
        /// Sanity-check helper for tests: does this world hold at least one entity with <typeparamref name="T"/>?
        /// Kept here rather than on EcsWorld itself because it's only needed by migration tests.
        /// </summary>
        public bool HasAny<T>() where T : struct
        {
            var q = new QueryDescription().WithAll<T>();
            int count = 0;
            _target.World.Query(in q, (Entity _) => count++);
            return count > 0;
        }
    }
}

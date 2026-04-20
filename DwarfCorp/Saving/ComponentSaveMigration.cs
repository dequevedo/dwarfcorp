using System;
using System.Collections.Generic;
using Arch.Core;
using DwarfCorp.ECS;
using DwarfCorp.ECS.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
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

        /// <summary>
        /// Mapping from the legacy <see cref="GameComponent.GlobalID"/> to the Arch
        /// <see cref="Entity"/> that inherited its root-level state. Populated by
        /// <see cref="MigrateTransforms"/> — the first family's pass establishes one
        /// Arch entity per root-level legacy component, and every subsequent family
        /// commit (Physics, Health, Sprite, …) folds its data INTO the existing
        /// entity via this lookup. Families that want to run standalone and create
        /// fresh entities can ignore it.
        /// </summary>
        public IReadOnlyDictionary<uint, Entity> LegacyIdToEntity => _legacyIdToEntity;
        private readonly Dictionary<uint, Entity> _legacyIdToEntity = new();

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

            // Per-family migration hooks. The full ordered list of 22 families with their
            // dependency DAG lives in docs/l4_arch_migration_plan.md. Single source of
            // truth for ordering — adding a family = add a call here + tick the row in
            // that doc + update tests.
            MigrateTransforms(legacy, ref created, ref skipped);
            // MigratePhysics(legacy, ref created, ref skipped);      // commit #2
            // …continues through #22.

            _log.ZLogInformation(
                $"ComponentSaveMigration: {created} entities created, {skipped} components skipped");

            return created;
        }

        /// <summary>
        /// Family #1 of the L.4 migration. For every root-level legacy
        /// <see cref="GameComponent"/> (i.e. direct child of the save's RootComponent),
        /// decompose its <c>LocalTransform</c> matrix into position/rotation/scale and
        /// attach it to a freshly created Arch entity. Record the legacy→entity
        /// mapping so later family migrations (Physics, Health, Sprite…) can fold
        /// their data onto the same entity.
        ///
        /// Why only root-level. The legacy GameComponent tree packs data AND
        /// archetype composition into a parent/child hierarchy — a Dwarf holds a
        /// Sprite child, a Health child, an Inventory child, etc. In ECS every Dwarf
        /// is ONE entity with many components, so only the top-level "thing in the
        /// world" node becomes an Arch entity. Child-node data folds onto the
        /// parent's entity when the matching family commit lands. Grandchildren and
        /// deeper are the same — they contribute state to an ancestor's entity, not
        /// their own.
        ///
        /// If the legacy has no root component id, or a component's LocalTransform
        /// doesn't decompose cleanly (degenerate matrix — shouldn't happen in
        /// practice), we skip that component rather than fail the whole migration.
        /// The save would still load via the legacy path during the coexistence
        /// phase, so a single bad component doesn't block the user.
        /// </summary>
        private void MigrateTransforms(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            if (legacy.SaveableComponents == null) return;

            foreach (var component in legacy.SaveableComponents)
            {
                if (component == null) continue;

                // Only root-level components become Arch entities. The root component
                // itself (the tree's synthetic RootComponent) is not a world object
                // and is skipped. Non-root descendants fold onto their ancestor's
                // entity in a later commit.
                if (component.GlobalID == legacy.RootComponent)
                {
                    skipped++;
                    continue;
                }
                if (ParentIdOf(component) != legacy.RootComponent)
                {
                    skipped++;
                    continue;
                }

                var local = component.LocalTransform;
                if (!local.Decompose(out var scale, out var rotation, out var translation))
                {
                    _log.ZLogWarning($"Transform of legacy component {component.GlobalID} ('{component.Name}') failed Matrix.Decompose — skipping");
                    skipped++;
                    continue;
                }

                var entity = _target.World.Create(new Transform
                {
                    Position = translation,
                    Rotation = rotation,
                    Scale = scale,
                });

                _legacyIdToEntity[component.GlobalID] = entity;
                created++;
            }
        }

        /// <summary>
        /// Reads <see cref="GameComponent.ParentID"/> without going through the
        /// live Parent resolver — we're operating on raw save data, no
        /// ComponentManager has rehydrated the tree yet, so the property getter
        /// would return null. The serialized ParentID field is what we want.
        /// </summary>
        private static uint ParentIdOf(GameComponent component)
        {
            // ParentID is private on GameComponent; reflect to read it. This runs
            // once per component during save load — not a hot path, and we're
            // about to delete the legacy GameComponent entirely once the L.4
            // migration finishes so broadening its access surface just to help
            // its own removal isn't worth the churn.
            var field = typeof(GameComponent).GetField("ParentID",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (uint)(field?.GetValue(component) ?? ComponentManager.InvalidID);
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

using System;
using System.Collections.Generic;
using Arch.Core;
using DwarfCorp.ECS;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using ZLogger;
// Aliases: the ECS.Components namespace deliberately mirrors legacy class names
// (Physics, Health, Inventory, …) because those ARE the same concept, just in
// value-type form. Inside this file we're constantly referring to both sides,
// so the legacy type keeps its plain name and the Arch side is prefixed Ecs*.
using EcsTransform = DwarfCorp.ECS.Components.Transform;
using EcsPhysics = DwarfCorp.ECS.Components.Physics;
using EcsHealth = DwarfCorp.ECS.Components.Health;
using EcsTinter = DwarfCorp.ECS.Components.Tinter;
using EcsSprite = DwarfCorp.ECS.Components.Sprite;
using EcsAnimatedSprite = DwarfCorp.ECS.Components.AnimatedSprite;
using EcsInstanceMeshRef = DwarfCorp.ECS.Components.InstanceMeshRef;
using EcsInventory = DwarfCorp.ECS.Components.Inventory;
using EcsInventoryItem = DwarfCorp.ECS.Components.InventoryItem;
using EcsEquipment = DwarfCorp.ECS.Components.Equipment;
using EcsLightEmission = DwarfCorp.ECS.Components.LightEmission;
using EcsRadiusSensor = DwarfCorp.ECS.Components.RadiusSensor;
using EcsEnemySensor = DwarfCorp.ECS.Components.EnemySensor;
using EcsSpawnOnExplored = DwarfCorp.ECS.Components.SpawnOnExplored;
using EcsVoxelListenerTag = DwarfCorp.ECS.Components.VoxelListenerTag;
using EcsFlammable = DwarfCorp.ECS.Components.Flammable;
using EcsFire = DwarfCorp.ECS.Components.Fire;
using EcsFollower = DwarfCorp.ECS.Components.Follower;
using EcsBobber = DwarfCorp.ECS.Components.Bobber;
using EcsMinimapIcon = DwarfCorp.ECS.Components.MinimapIcon;
using EcsDwarfThoughts = DwarfCorp.ECS.Components.DwarfThoughts;
using EcsEgg = DwarfCorp.ECS.Components.Egg;
using EcsCreatureAI = DwarfCorp.ECS.Components.CreatureAI;
using EcsDwarfAI = DwarfCorp.ECS.Components.DwarfAI;
using EcsKoboldAI = DwarfCorp.ECS.Components.KoboldAI;
using EcsGremlinAI = DwarfCorp.ECS.Components.GremlinAI;
using EcsNecromancerAI = DwarfCorp.ECS.Components.NecromancerAI;
using EcsFairyAITag = DwarfCorp.ECS.Components.FairyAITag;
using EcsBirdAITag = DwarfCorp.ECS.Components.BirdAITag;
using EcsBatAITag = DwarfCorp.ECS.Components.BatAITag;
using EcsSnakeAITag = DwarfCorp.ECS.Components.SnakeAITag;
using EcsGolemAITag = DwarfCorp.ECS.Components.GolemAITag;
using EcsPacingCreatureAITag = DwarfCorp.ECS.Components.PacingCreatureAITag;
using EcsResourceEntity = DwarfCorp.ECS.Components.ResourceEntity;
using EcsFixture = DwarfCorp.ECS.Components.Fixture;
using EcsBanner = DwarfCorp.ECS.Components.Banner;
using EcsMagicalObject = DwarfCorp.ECS.Components.MagicalObject;
using EcsElevatorShaft = DwarfCorp.ECS.Components.ElevatorShaft;
using EcsPipeNetworkObject = DwarfCorp.ECS.Components.PipeNetworkObject;
using EcsBuildBuff = DwarfCorp.ECS.Components.BuildBuff;
using EcsBalloonAI = DwarfCorp.ECS.Components.BalloonAI;

namespace DwarfCorp.Saving
{
    /// <summary>
    /// Reads a legacy v1 <see cref="ComponentManager.ComponentSaveData"/> JSON blob
    /// and populates an <see cref="EcsWorld"/> with equivalent Arch entities + per-
    /// family components.
    ///
    /// ## Migration model
    ///
    /// Legacy GameComponent uses a parent/child tree where each descendant
    /// contributes data/behaviour to a "thing in the world" — e.g. a Dwarf holds
    /// Health / Inventory / Sprite children, each a full GameComponent. ECS
    /// collapses that into ONE Entity per world-thing with N components. So only
    /// root-level (direct child of the save's RootComponent) GameComponents get
    /// their own Entity; descendants fold their data onto the ancestor's Entity.
    ///
    /// The <see cref="LegacyIdToEntity"/> mapping is populated by
    /// <see cref="MigrateTransforms"/> (family #1) and used by every family after
    /// to find the target Entity via <see cref="FindTargetEntity"/>.
    ///
    /// ## Idempotency
    ///
    /// Calling <see cref="Migrate"/> twice with the same legacy payload on the same
    /// shim-instance + world yields the same final entity state (the internal
    /// LegacyIdToEntity dict is the idempotency key). Calling twice on a FRESH
    /// shim pointing at the same world creates duplicates — the design contract
    /// is "one shim per load cycle", not "immortal singleton". Tests pin both.
    ///
    /// ## Coexistence phase
    ///
    /// The legacy ComponentManager still owns every entity during live play. This
    /// shim is only wired in when <see cref="MetaData.SaveFormatVersion"/> is bumped
    /// to v2 and SaveGame routes v1 loads through here. Families land one at a time;
    /// each one is minimal (snapshot of persisted data, not full system ownership).
    /// </summary>
    public sealed class ComponentSaveMigration
    {
        private readonly EcsWorld _target;
        private readonly ILogger<ComponentSaveMigration> _log;

        /// <summary>
        /// Mapping from the legacy <see cref="GameComponent.GlobalID"/> to the Arch
        /// <see cref="Entity"/> that inherited its root-level state. Populated by
        /// <see cref="MigrateTransforms"/> (family #1) — every subsequent family
        /// folds its data INTO the existing entity via this lookup.
        /// </summary>
        public IReadOnlyDictionary<uint, Entity> LegacyIdToEntity => _legacyIdToEntity;
        private readonly Dictionary<uint, Entity> _legacyIdToEntity = new();

        /// <summary>
        /// Lookup populated at the start of <see cref="Migrate"/> so per-family
        /// passes don't do O(N) scans to walk a component's parent chain. Cleared
        /// between calls.
        /// </summary>
        private readonly Dictionary<uint, GameComponent> _idToComponent = new();

        public ComponentSaveMigration(EcsWorld target, ILogger<ComponentSaveMigration> log)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _log = log;
        }

        public int Migrate(ComponentManager.ComponentSaveData legacy)
        {
            if (legacy == null)
            {
                _log.ZLogWarning($"Migrate called with null legacy payload — nothing to do");
                return 0;
            }

            _idToComponent.Clear();
            if (legacy.SaveableComponents != null)
                foreach (var c in legacy.SaveableComponents)
                    if (c != null) _idToComponent[c.GlobalID] = c;

            int created = 0;
            int skipped = 0;

            // Per-family migration hooks — ordered according to the DAG in
            // docs/l4_arch_migration_plan.md. One source of truth for order; edit
            // that doc if the dependency structure changes.
            MigrateTransforms(legacy, ref created, ref skipped);              // #1
            MigratePhysics(legacy, ref created, ref skipped);                 // #2
            MigrateHealth(legacy, ref created, ref skipped);                  // #3
            MigrateTinterAndSimpleSprite(legacy, ref created, ref skipped);   // #4
            MigrateAnimatedSprite(legacy, ref created, ref skipped);          // #5
            MigrateMeshFamily(legacy, ref created, ref skipped);              // #6
            MigrateInventory(legacy, ref created, ref skipped);               // #7
            MigrateEquipment(legacy, ref created, ref skipped);               // #8
            MigrateLightEmission(legacy, ref created, ref skipped);           // #9
            MigrateSensors(legacy, ref created, ref skipped);                 // #10
            MigrateVoxelListeners(legacy, ref created, ref skipped);          // #11
            MigrateFire(legacy, ref created, ref skipped);                    // #12
            MigrateMotion(legacy, ref created, ref skipped);                  // #13
            MigrateMinimapIcons(legacy, ref created, ref skipped);            // #14
            MigrateThoughtsAndEggs(legacy, ref created, ref skipped);         // #15
            MigrateCreatureAIs(legacy, ref created, ref skipped);             // #16 + #17
            MigrateMonsterAndAnimalAIs(legacy, ref created, ref skipped);     // #18
            MigrateResourceEntities(legacy, ref created, ref skipped);        // #19
            MigrateFixtures(legacy, ref created, ref skipped);                // #20
            MigratePipeNetwork(legacy, ref created, ref skipped);             // #21
            MigrateBalloonAI(legacy, ref created, ref skipped);               // #22

            _log.ZLogInformation(
                $"ComponentSaveMigration: {created} entities created, {skipped} components skipped");

            return created;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Family #1 — Transform (root-level only, creates the entity).
        // ──────────────────────────────────────────────────────────────────────

        private void MigrateTransforms(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            if (legacy.SaveableComponents == null) return;

            foreach (var component in legacy.SaveableComponents)
            {
                if (component == null) continue;
                if (component.GlobalID == legacy.RootComponent) { skipped++; continue; }
                if (ParentIdOf(component) != legacy.RootComponent) { skipped++; continue; }

                var local = component.LocalTransform;
                if (!local.Decompose(out var scale, out var rotation, out var translation))
                {
                    _log.ZLogWarning($"Transform of legacy component {component.GlobalID} ('{component.Name}') failed Matrix.Decompose — skipping");
                    skipped++;
                    continue;
                }

                var entity = _target.World.Create(new EcsTransform
                {
                    Position = translation,
                    Rotation = rotation,
                    Scale = scale,
                });

                _legacyIdToEntity[component.GlobalID] = entity;
                created++;
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Families #2..#11 — fold each component's data onto the Entity of its
        // nearest root-level ancestor (created by MigrateTransforms).
        // ──────────────────────────────────────────────────────────────────────

        private void MigratePhysics(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.Physics>(legacy, (p, entity) =>
            {
                _target.World.Add(entity, new EcsPhysics
                {
                    Velocity = p.Velocity,
                    Gravity = p.Gravity,
                    Mass = p.Mass,
                    LinearDamping = p.LinearDamping,
                    Friction = p.Friction,
                    CollideMode = (byte)p.CollideMode,
                    Orientation = (byte)p.Orientation,
                    IsInLiquid = p.IsInLiquid,
                    AllowPhysicsSleep = p.AllowPhysicsSleep,
                    IsSleeping = p.IsSleeping,
                });
            }, ref created, ref skipped);
        }

        private void MigrateHealth(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.Health>(legacy, (h, entity) =>
            {
                _target.World.Add(entity, new EcsHealth
                {
                    Hp = h.Hp,
                    MaxHealth = h.MaxHealth,
                    MinHealth = h.MinHealth,
                });
            }, ref created, ref skipped);
        }

        private void MigrateTinterAndSimpleSprite(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            // Tinter is a base class; every sprite/mesh component IS-a Tinter. Rather
            // than attach a Tinter component for every descendant type (which would
            // accumulate multiple instances on the same entity when a parent and
            // child both derive from Tinter), we attach it exactly once per target
            // entity — first one wins, others are skipped. SimpleSprite adds its
            // own struct on top.
            var tintersSeen = new HashSet<Entity>();

            ForEachMatching<DwarfCorp.Tinter>(legacy, (t, entity) =>
            {
                if (tintersSeen.Add(entity))
                {
                    _target.World.Add(entity, new EcsTinter
                    {
                        LightRamp = t.LightRamp,
                        VertexColorTint = t.VertexColorTint,
                        TintChangeRate = t.TintChangeRate,
                        LightsWithVoxels = t.LightsWithVoxels,
                        Stipple = t.Stipple,
                    });
                }
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.SimpleSprite>(legacy, (s, entity) =>
            {
                _target.World.Add(entity, new EcsSprite
                {
                    Orientation = (byte)s.OrientationType,
                    WorldWidth = s.WorldWidth,
                    WorldHeight = s.WorldHeight,
                });
            }, ref created, ref skipped);
        }

        private void MigrateAnimatedSprite(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            // AnimatedSprite + LayeredSimpleSprite + DwarfCharacterSprite all carry
            // the same "orientation + silhouette" knobs and legacy-side all throw on
            // serialization (Animations dict is marked unserializable). We therefore
            // only migrate the non-animation knobs. If a save somehow ends up here
            // with AnimatedSprite it hit the [OnSerializing] throw legacy-side and
            // never wrote — defensive but harmless.
            ForEachMatching<DwarfCorp.AnimatedSprite>(legacy, (a, entity) =>
            {
                _target.World.Add(entity, new EcsAnimatedSprite
                {
                    Orientation = (byte)a.OrientationType,
                    SilhouetteColor = a.SilhouetteColor,
                    DrawSilhouette = false,
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.LayeredSimpleSprite>(legacy, (l, entity) =>
            {
                // LayeredSimpleSprite has its own DrawSilhouette flag; the plain
                // AnimatedSprite doesn't. Overwrite if both are present — the layered
                // one wins as it's the more specific component.
                if (_target.World.Has<EcsAnimatedSprite>(entity))
                    _target.World.Remove<EcsAnimatedSprite>(entity);
                _target.World.Add(entity, new EcsAnimatedSprite
                {
                    Orientation = (byte)l.OrientationType,
                    SilhouetteColor = l.SilhouetteColor,
                    DrawSilhouette = l.DrawSilhouette,
                });
            }, ref created, ref skipped);
        }

        private void MigrateMeshFamily(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            // MeshComponent / PrimitiveComponent are Tinter-only legacy-side; no
            // unique persistent state. They're already covered by the Tinter
            // migration. Only InstanceMesh carries a unique ModelType string.
            ForEachMatching<DwarfCorp.InstanceMesh>(legacy, (m, entity) =>
            {
                _target.World.Add(entity, new EcsInstanceMeshRef
                {
                    ModelType = m.ModelType,
                });
            }, ref created, ref skipped);
        }

        private void MigrateInventory(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.Inventory>(legacy, (inv, entity) =>
            {
                var items = new List<EcsInventoryItem>(inv.Resources?.Count ?? 0);
                if (inv.Resources != null)
                    foreach (var legacyItem in inv.Resources)
                        items.Add(new EcsInventoryItem
                        {
                            Resource = legacyItem.Resource,
                            MarkedForRestock = legacyItem.MarkedForRestock,
                            MarkedForUse = legacyItem.MarkedForUse,
                        });
                _target.World.Add(entity, new EcsInventory { Items = items });
            }, ref created, ref skipped);
        }

        private void MigrateEquipment(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.Equipment>(legacy, (eq, entity) =>
            {
                var map = new Dictionary<string, Resource>(eq.EquippedItems?.Count ?? 0);
                if (eq.EquippedItems != null)
                    foreach (var kv in eq.EquippedItems)
                        map[kv.Key] = kv.Value;
                _target.World.Add(entity, new EcsEquipment { EquippedItems = map });
            }, ref created, ref skipped);
        }

        private void MigrateLightEmission(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            // Inline the iteration: we need to skip emitters with a null DynamicLight
            // (saved without light params — shouldn't happen in practice but defensive)
            // and that needs to increment `skipped`, which C# doesn't let us do inside a
            // closure that captures a ref parameter. One-off path doesn't need the
            // ForEachMatching helper.
            if (legacy.SaveableComponents == null) return;

            foreach (var component in legacy.SaveableComponents)
            {
                if (component is not DwarfCorp.LightEmitter emitter) continue;
                var entity = FindTargetEntity(emitter, legacy);
                if (entity == default) { skipped++; continue; }
                if (emitter.Light == null) { skipped++; continue; }

                _target.World.Add(entity, new EcsLightEmission
                {
                    Range = emitter.Light.Range,
                    Intensity = emitter.Light.Intensity,
                });
                created++;
            }
        }

        private void MigrateSensors(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.RadiusSensor>(legacy, (s, entity) =>
            {
                _target.World.Add(entity, new EcsRadiusSensor
                {
                    SenseRadiusSquared = s.SenseRadius,
                    CheckLineOfSight = s.CheckLineOfSight,
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.EnemySensor>(legacy, (s, entity) =>
            {
                _target.World.Add(entity, new EcsEnemySensor { DetectCloaked = s.DetectCloaked });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.SpawnOnExploredTrigger>(legacy, (s, entity) =>
            {
                _target.World.Add(entity, new EcsSpawnOnExplored
                {
                    EntityToSpawn = s.EntityToSpawn,
                    SpawnLocation = s.SpawnLocation,
                });
            }, ref created, ref skipped);
        }

        private void MigrateVoxelListeners(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            // All three legacy types (GenericVoxelListener, VoxelRevealer, DestroyOnTimer)
            // throw on serialization, so in practice saves never contain them. The tag
            // exists for completeness — if a future migration synthesizes one of these,
            // the archetype can be detected on the Arch side.
            ForEachMatching<DwarfCorp.GenericVoxelListener>(legacy, (_, entity) =>
            {
                if (!_target.World.Has<EcsVoxelListenerTag>(entity))
                    _target.World.Add(entity, new EcsVoxelListenerTag());
            }, ref created, ref skipped);
        }

        private void MigrateFire(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.Flammable>(legacy, (f, entity) =>
            {
                _target.World.Add(entity, new EcsFlammable
                {
                    Heat = f.Heat,
                    Flashpoint = f.Flashpoint,
                    Damage = f.Damage,
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.Fire>(legacy, (f, entity) =>
            {
                // Timer carries (ElapsedTime, Duration) but only the remaining amount
                // is semantically useful after a save/load — we store it flat. If the
                // Fire system eventually needs full Timer state, migrate it at that point.
                _target.World.Add(entity, new EcsFire
                {
                    LifeTimerRemaining = f.LifeTimer == null ? 0f
                        : System.MathF.Max(0f, f.LifeTimer.TargetTimeSeconds - f.LifeTimer.CurrentTimeSeconds),
                });
            }, ref created, ref skipped);
        }

        private void MigrateMotion(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.Follower>(legacy, (f, entity) =>
            {
                _target.World.Add(entity, new EcsFollower
                {
                    FollowRadius = f.FollowRadius,
                    TargetPos = f.TargetPos,
                    FollowRate = f.FollowRate,
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.Bobber>(legacy, (b, entity) =>
            {
                _target.World.Add(entity, new EcsBobber
                {
                    Magnitude = b.Magnitude,
                    Rate = b.Rate,
                    Offset = b.Offset,
                    OrigY = b.OrigY,
                });
            }, ref created, ref skipped);
        }

        private void MigrateMinimapIcons(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.MinimapIcon>(legacy, (m, entity) =>
            {
                _target.World.Add(entity, new EcsMinimapIcon
                {
                    Icon = m.Icon,
                    IconScale = m.IconScale,
                });
            }, ref created, ref skipped);
        }

        private void MigrateThoughtsAndEggs(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.DwarfThoughts>(legacy, (t, entity) =>
            {
                _target.World.Add(entity, new EcsDwarfThoughts
                {
                    Thoughts = t.Thoughts != null
                        ? new System.Collections.Generic.List<Thought>(t.Thoughts)
                        : new System.Collections.Generic.List<Thought>(),
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.Egg>(legacy, (e, entity) =>
            {
                _target.World.Add(entity, new EcsEgg
                {
                    Adult = e.Adult,
                    Birthday = e.Birthday,
                    ParentBody = e.ParentBody,
                    Hatched = e.Hatched,
                });
            }, ref created, ref skipped);
        }

        private void MigrateCreatureAIs(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            // #16 CreatureAI base. Every AI (Dwarf, monster, animal) IS-a CreatureAI,
            // so a single pass here covers every variant's base fields. Legacy
            // LastTaskFailureReason is private [JsonProperty] — reflect to read it.
            var failureReasonField = typeof(DwarfCorp.CreatureAI).GetField("LastTaskFailureReason",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            ForEachMatching<DwarfCorp.CreatureAI>(legacy, (ai, entity) =>
            {
                // Don't stack if multiple CreatureAIs landed on same entity (shouldn't
                // happen but cheap to guard).
                if (_target.World.Has<EcsCreatureAI>(entity)) return;
                _target.World.Add(entity, new EcsCreatureAI
                {
                    Biography = ai.Biography ?? "",
                    LastFailedAct = ai.LastFailedAct,
                    LastTaskFailureReason = (string)(failureReasonField?.GetValue(ai) ?? ""),
                    MinecartActive = ai.MinecartActive,
                });
            }, ref created, ref skipped);

            // #17 DwarfAI delta — all bookkeeping is private [JsonProperty], so
            // grab each via reflection.
            var dwarfAiType = typeof(DwarfCorp.DwarfAI);
            var fLastXp = dwarfAiType.GetField("lastXPAnnouncement",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fLastXpStat = dwarfAiType.GetField("lastXPAnnouncementStat",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fNotPaid = dwarfAiType.GetField("NumDaysNotPaid",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fUnhappy = dwarfAiType.GetField("UnhappinessTime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fSinceTask = dwarfAiType.GetField("TimeSinceLastAssignedTask",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            ForEachMatching<DwarfCorp.DwarfAI>(legacy, (dai, entity) =>
            {
                _target.World.Add(entity, new EcsDwarfAI
                {
                    LastXpAnnouncement = (int)(fLastXp?.GetValue(dai) ?? -1),
                    LastXpAnnouncementStat = (int)(fLastXpStat?.GetValue(dai) ?? -1),
                    NumDaysNotPaid = (int)(fNotPaid?.GetValue(dai) ?? 0),
                    UnhappinessTime = (double)(fUnhappy?.GetValue(dai) ?? 0.0),
                    TimeSinceLastAssignedTask = (double)(fSinceTask?.GetValue(dai) ?? 0.0),
                });
            }, ref created, ref skipped);
        }

        private void MigrateMonsterAndAnimalAIs(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            // Tags for AIs that carry no persistent state beyond CreatureAI.
            ForEachMatching<DwarfCorp.FairyAI>(legacy, (_, entity) => _target.World.Add(entity, new EcsFairyAITag()), ref created, ref skipped);
            ForEachMatching<DwarfCorp.BirdAI>(legacy, (_, entity) => _target.World.Add(entity, new EcsBirdAITag()), ref created, ref skipped);
            ForEachMatching<DwarfCorp.BatAI>(legacy, (_, entity) => _target.World.Add(entity, new EcsBatAITag()), ref created, ref skipped);
            ForEachMatching<DwarfCorp.SnakeAI>(legacy, (_, entity) => _target.World.Add(entity, new EcsSnakeAITag()), ref created, ref skipped);
            ForEachMatching<DwarfCorp.GolemAI>(legacy, (_, entity) => _target.World.Add(entity, new EcsGolemAITag()), ref created, ref skipped);
            ForEachMatching<DwarfCorp.PacingCreatureAI>(legacy, (_, entity) => _target.World.Add(entity, new EcsPacingCreatureAITag()), ref created, ref skipped);

            // AIs with actual persistent knobs.
            ForEachMatching<DwarfCorp.KoboldAI>(legacy, (k, entity) =>
            {
                _target.World.Add(entity, new EcsKoboldAI
                {
                    StealFromPlayerProbability = k.StealFromPlayerProbability,
                    LeaveWorldTimerRemaining = TimerRemaining(k.LeaveWorldTimer),
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.GremlinAI>(legacy, (g, entity) =>
            {
                _target.World.Add(entity, new EcsGremlinAI
                {
                    DestroyPlayerObjectProbability = g.DestroyPlayerObjectProbability,
                    PlantBomb = g.PlantBomb,
                    LeaveWorldTimerRemaining = TimerRemaining(g.LeaveWorldTimer),
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.NecromancerAI>(legacy, (n, entity) =>
            {
                _target.World.Add(entity, new EcsNecromancerAI
                {
                    Skeletons = n.Skeletons ?? new System.Collections.Generic.List<Skeleton>(),
                    MaxSkeletons = n.MaxSkeletons,
                    SummonTimerRemaining = TimerRemaining(n.SummonTimer),
                    AttackTimerRemaining = TimerRemaining(n.AttackTimer),
                    AttackRange = n.AttackRange,
                });
            }, ref created, ref skipped);
        }

        private void MigrateResourceEntities(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.ResourceEntity>(legacy, (r, entity) =>
            {
                _target.World.Add(entity, new EcsResourceEntity
                {
                    Resource = r.Resource,
                    LifeTimerRemaining = TimerRemaining(r.LifeTimer),
                });
            }, ref created, ref skipped);
        }

        private void MigrateFixtures(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.Fixture>(legacy, (f, entity) =>
            {
                if (_target.World.Has<EcsFixture>(entity)) return;
                _target.World.Add(entity, new EcsFixture
                {
                    Asset = f.Asset,
                    Frame = f.Frame,
                    OrientMode = (byte)f.OrientMode,
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.Banner>(legacy, (b, entity) =>
            {
                _target.World.Add(entity, new EcsBanner { Logo = b.Logo });
            }, ref created, ref skipped);

            // Flag is another Banner-like thing with a Logo — it extends Body not
            // Banner legacy-side but uses the same persisted field name. Treat as
            // the same family for migration purposes.
            ForEachMatching<DwarfCorp.Flag>(legacy, (b, entity) =>
            {
                if (_target.World.Has<EcsBanner>(entity)) return;
                _target.World.Add(entity, new EcsBanner { Logo = b.Logo });
            }, ref created, ref skipped);

            // MagicalObject uses a private _currentCharges backing field +
            // public CurrentCharges property — read via the property to stay
            // robust to field-name refactors.
            ForEachMatching<DwarfCorp.MagicalObject>(legacy, (m, entity) =>
            {
                _target.World.Add(entity, new EcsMagicalObject
                {
                    MaxCharges = m.MaxCharges,
                    CurrentCharges = m.CurrentCharges,
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.Elevators.ElevatorShaft>(legacy, (s, entity) =>
            {
                _target.World.Add(entity, new EcsElevatorShaft
                {
                    TrackAbove = s.TrackAbove,
                    TrackBelow = s.TrackBelow,
                });
            }, ref created, ref skipped);
        }

        private void MigratePipeNetwork(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.SteamPipes.PipeNetworkObject>(legacy, (p, entity) =>
            {
                if (_target.World.Has<EcsPipeNetworkObject>(entity)) return;
                _target.World.Add(entity, new EcsPipeNetworkObject
                {
                    LiquidType = p.LiquidType,
                    DrawPipes = p.DrawPipes,
                    Orientation = (byte)p.Orientation,
                });
            }, ref created, ref skipped);

            ForEachMatching<DwarfCorp.SteamPipes.BuildBuff>(legacy, (b, entity) =>
            {
                _target.World.Add(entity, new EcsBuildBuff { BuffMultiplier = b.BuffMultiplier });
            }, ref created, ref skipped);
        }

        private void MigrateBalloonAI(ComponentManager.ComponentSaveData legacy, ref int created, ref int skipped)
        {
            ForEachMatching<DwarfCorp.BalloonAI>(legacy, (b, entity) =>
            {
                _target.World.Add(entity, new EcsBalloonAI
                {
                    VelocityController = b.VelocityController,
                    TargetPosition = b.TargetPosition,
                    MaxVelocity = b.MaxVelocity,
                    MaxForce = b.MaxForce,
                    State = (byte)b.State,
                    Faction = b.Faction,
                    WaitTimerRemaining = TimerRemaining(b.WaitTimer),
                });
            }, ref created, ref skipped);
        }

        private static float TimerRemaining(Timer t) =>
            t == null ? 0f : System.MathF.Max(0f, t.TargetTimeSeconds - t.CurrentTimeSeconds);

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// For every legacy component that IS <typeparamref name="T"/> (either as
        /// the concrete type or a subclass), locate its Arch entity via
        /// <see cref="FindTargetEntity"/> and invoke <paramref name="handler"/>. If
        /// the entity can't be found (component's root ancestor didn't migrate),
        /// bumps <paramref name="skipped"/>.
        /// </summary>
        private void ForEachMatching<T>(
            ComponentManager.ComponentSaveData legacy,
            Action<T, Entity> handler,
            ref int created,
            ref int skipped)
            where T : GameComponent
        {
            if (legacy.SaveableComponents == null) return;

            foreach (var component in legacy.SaveableComponents)
            {
                if (component is not T typed) continue;
                var entity = FindTargetEntity(typed, legacy);
                if (entity == default) { skipped++; continue; }
                handler(typed, entity);
                created++;
            }
        }

        /// <summary>
        /// Walk up a legacy component's parent chain and return the Entity of the
        /// first ancestor that <see cref="MigrateTransforms"/> created. Returns
        /// <c>default</c> (the unassigned <see cref="Entity"/>) if the chain reaches
        /// <see cref="ComponentManager.InvalidID"/> or the save's RootComponent
        /// without hitting a mapped ancestor.
        /// </summary>
        private Entity FindTargetEntity(GameComponent component, ComponentManager.ComponentSaveData legacy)
        {
            var current = component;
            // 16 hops is plenty — legacy hierarchies are shallow (Dwarf → Sprite →
            // Layer, at worst). Cycles shouldn't exist but the bound is a safety net.
            for (int hops = 0; hops < 16 && current != null; hops++)
            {
                if (_legacyIdToEntity.TryGetValue(current.GlobalID, out var e))
                    return e;

                uint parentId = ParentIdOf(current);
                if (parentId == ComponentManager.InvalidID || parentId == legacy.RootComponent)
                    return default;

                _idToComponent.TryGetValue(parentId, out current);
            }
            return default;
        }

        private static uint ParentIdOf(GameComponent component)
        {
            var field = typeof(GameComponent).GetField("ParentID",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (uint)(field?.GetValue(component) ?? ComponentManager.InvalidID);
        }

        /// <summary>
        /// Sanity-check helper for tests: does this world hold at least one entity with <typeparamref name="T"/>?
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

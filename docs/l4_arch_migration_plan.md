# L.4 — Per-family Arch ECS Migration Plan

Living document tracking every GameComponent-derived type and the migration
strategy. Foundation landed in commit [`16b6832b6`](https://github.com/anthropics/claude-code/commit/16b6832b6)
— this doc is the scaffolding that prevents the remaining ~50 types from
getting forgotten piecemeal.

Referenced from [`PERF_PLAN_EXTREME.md`](../PERF_PLAN_EXTREME.md) Fase L.4
and [`TODO_LIST.md`](../TODO_LIST.md) item 30.

## Design intent

The current `GameComponent` hierarchy mixes three concerns that ECS
separates:

1. **Data** (Transform, Physics, Health, Inventory, Flammable, Fire, Tintable,
   Sensor, Equipment, LightEmission, …) — plain structs attached to entities.
2. **Entity archetypes** (Dwarf, Crate, Balloon, Projectile, Plant, Fixture,
   …) — compositions of components that together form a "thing in the world".
3. **Systems** (physics tick, health tick, AI evaluation, rendering, sensor
   update, …) — logic that operates on entities whose archetype matches a
   query.

Current GameComponent classes conflate 1 and 2; some also carry 3 in their
`Update()` override. The migration deliberately splits them. Arch components
are always `struct` (value types — the whole archetype allocation model
depends on it). Systems live in `DwarfCorp.ECS.Systems`. Archetypes are
just documented compositions — no class needed.

## Execution strategy

**One family per commit.** For each cluster below, a single commit does:

1. Adds the Arch component struct(s) in `DwarfCorp/ECS/Components/`.
2. Adds the system in `DwarfCorp/ECS/Systems/` if the family had logic.
3. Fills in the matching `Migrate*` method in `Saving/ComponentSaveMigration.cs`.
4. Writes xUnit tests covering:
   - Round-trip: legacy JSON → Arch → legacy JSON (if bidirectional save
     compat is needed during the transition).
   - One-way: legacy JSON → Arch (idempotent, no duplicates on re-run).
5. Flips the tripwire assertion in `EcsFoundationTests.cs` for that family
   (from `False` to `True`).
6. Does NOT delete the corresponding legacy GameComponent yet — legacy path
   keeps running so saves stay double-loadable. The legacy types are deleted
   only once ALL families have migrated.

**Ordering rule:** always migrate a family before any family that depends on
it. If AI reads Health, Health must migrate first.

## Dependency DAG

```
Transform (pure data, no deps)
  ├── Physics (Transform)
  │     ├── Flammable (Physics + Fire inheritance)
  │     ├── Fire
  │     ├── Projectile (Physics + lifetime)
  │     └── ResourceEntity (Physics)
  ├── Tintable (Transform + optional renderers)
  │     ├── SimpleSprite
  │     ├── AnimatedSprite
  │     ├── LayeredSimpleSprite / DwarfCharacterSprite
  │     ├── MeshComponent / InstanceMesh / PrimitiveComponent
  │     ├── Banner
  │     ├── ParticleTrigger
  │     └── SelectionCircle
  ├── LightEmission (Transform + radius + color)
  ├── MinimapIcon (Transform + sprite)
  ├── Sensor (Transform + radius)
  │     ├── RadiusSensor
  │     ├── EnemySensor
  │     └── SpawnOnExploredTrigger
  ├── VoxelListener (Transform + IVoxelListener events)
  │     ├── GenericVoxelListener
  │     ├── VoxelRevealer
  │     └── DestroyOnTimer
  ├── Follower (Transform + target entity ref)
  ├── Bobber (Transform + phase)
  └── PipeNetworkObject (Transform + hydraulic graph)

Health (usually on entities that also have Physics + Transform)
Inventory (backpack-style storage)
Equipment (weapon / armor slots)
Egg (lifetime + spawn)
DwarfThoughts (memory of events)

CreatureAI (depends on Transform + Physics + Health + Inventory + Equipment)
  ├── DwarfAI
  ├── FairyAI, BirdAI, BatAI, SnakeAI
  ├── KoboldAI, GremlinAI, GolemAI, NecromancerAI
  └── PacingCreatureAI

Archetypes (compositions, not their own Arch types):
  Dwarf = Transform + Physics + Health + Inventory + Equipment + Tintable +
          LayeredSprite + CreatureAI + DwarfThoughts + DwarfAI
  Crate = Transform + Physics + Inventory + Sprite
  Balloon = Transform + Physics + BalloonAI
  Fixture = Transform + (optional) Inventory + Sprite
  Projectile = Transform + Physics + lifetime
  Plant = Transform + Sprite + lifetime + (optional) Fire
  ElevatorPlatform / ElevatorShaft = Transform + Physics + linkage
  Egg = Transform + lifetime + spawn table
  MagicalObject / CraftedBody / CraftDetails = Transform + crafted data
```

## Migration order (recommended)

Work top-down through the dependency DAG. Each row below is one commit's
worth of scope:

| # | Family | Owns | Depends on | Status |
|---|---|---|---|---|
| 1 | **Transform** | Transform component | — | ✅ `MigrateTransforms` + 4 tests |
| 2 | **Physics** | Physics component + PhysicsSystem | Transform | ✅ snapshot (struct). Systems deferred. |
| 3 | **Health** | Health component + DamageSystem | — | ✅ snapshot. DamageType dict deferred. |
| 4 | **Tintable + SimpleSprite** | Tintable component + SpriteRenderSystem | Transform | ✅ snapshot. |
| 5 | **AnimatedSprite / LayeredSprites** | Animation components | Tintable | ✅ orientation/silhouette only (Animations dict throws on serialize legacy-side, nothing to migrate). |
| 6 | **MeshComponent / PrimitiveComponent / InstanceMesh** | Mesh rendering | Tintable | ✅ InstanceMeshRef (ModelType). Others are Tinter-only. |
| 7 | **Inventory** | Inventory component | — | ✅ item list migrated as snapshot. |
| 8 | **Equipment** | Equipment component | Inventory | ✅ slot dict migrated. |
| 9 | **LightEmission** | Light component + LightCollectionSystem | Transform | ✅ Range + Intensity. Position from Transform. |
| 10 | **Sensor family** (Radius, Enemy, SpawnOnExplored) | Sensor component + SensorSystem | Transform | ✅ 3 struct variants. Runtime-only fields deferred. |
| 11 | **VoxelListener family** | VoxelListener component + VoxelEventSystem | Transform | ✅ marker tag (legacy handlers are runtime-only, explicitly unserializable). |
| 12 | **Fire / Flammable** | Fire + Flammable components + FireSystem | Physics | ✅ Flammable (Heat/Flashpoint/Damage) + Fire (LifeTimer remaining). |
| 13 | **Follower / Bobber** | Follow + Bob components + animation systems | Transform | ✅ Follower (radius/target/rate) + Bobber (magnitude/rate/offset/origY). |
| 14 | **MinimapIcon** | MinimapIcon component + MinimapRenderSystem | Transform | ✅ Icon + IconScale. |
| 15 | **DwarfThoughts / Egg** | Memory / lifetime components | — | ✅ DwarfThoughts (Thoughts list) + Egg (Adult/Birthday/ParentBody/Hatched). |
| 16 | **CreatureAI core** | AI state machine + AISystem | Many — see DAG | ✅ Biography + LastFailedAct + LastTaskFailureReason + MinecartActive. Tasks/Blackboard/Sensor deferred (each is its own family or system). |
| 17 | **Dwarf archetype** | DwarfAI + composition | CreatureAI core | ✅ private [JsonProperty] bookkeeping (XP / pay / idle time). |
| 18 | **Animal / monster AIs** | per-species AI components | CreatureAI core |
| 19 | **Projectile / ResourceEntity** | Physics-derived archetypes | Physics |
| 20 | **Fixture family** (Crate, Banner, MagicalObject, Plant, ElevatorShaft, …) | Archetypes | Transform + Inventory + Sprite |
| 21 | **PipeNetworkObject / BuildBuff** | Hydraulic subsystem | Transform |
| 22 | **Balloon / BalloonAI** | — | Transform + Physics + AI |

## After the last family

- **Save format v2** starts being written (`MetaData.CurrentSaveFormatVersion`
  bumped to 2, `SaveGame.WriteJSON` emits Arch snapshot, loader branches on
  `SaveFormatVersion`).
- **ComponentManager is deleted**. `GameComponent` tree, `Body`,
  `TransformModule`, per-component Update hooks all go with it.
- **Fase D** (parallel component update) happens naturally because Arch
  archetype iteration is trivially `Parallel.ForEach`-able.
- Tracking item 27 (clustered lighting) and related downstream items
  become easier once the scene graph is archetype-queryable.

## Working with this doc

- When starting a family's commit, tick its row off in this doc in the
  same commit.
- When finding a component this doc missed, add it under the right cluster
  in the DAG and mark where it slots into the migration order.
- When a family turns out to split into multiple smaller commits (e.g.
  CreatureAI probably will), note the sub-steps here so the granularity
  stays visible.

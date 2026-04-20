using Microsoft.Xna.Framework;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.RadiusSensor"/>. Sense radius is
    /// stored as the squared value the legacy class used (SenseRadius = 15 * 15
    /// defaults) — downstream systems do range checks against LengthSquared() to
    /// match. The <c>Creatures</c> list kept by the legacy class is not migrated
    /// because it's runtime-reconstructed each tick, not a persisted fact.
    /// </summary>
    public struct RadiusSensor
    {
        public float SenseRadiusSquared;
        public bool CheckLineOfSight;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.EnemySensor"/>. The referenced
    /// faction, detected enemies, and owning CreatureAI are all runtime-only state
    /// or require the full CreatureAI migration (family #16) — we keep the one
    /// persistent knob, <c>DetectCloaked</c>, and wire the rest when AI lands.
    /// </summary>
    public struct EnemySensor
    {
        public bool DetectCloaked;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.SpawnOnExploredTrigger"/>.
    /// A one-shot voxel-exploration trigger that spawns an entity at a fixed
    /// location once fired. <see cref="EntityToSpawn"/> is an asset-name key
    /// resolved via <c>EntityFactory</c> (legacy-side, reference).
    /// </summary>
    public struct SpawnOnExplored
    {
        public string EntityToSpawn;
        public Vector3 SpawnLocation;
    }
}

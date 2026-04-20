namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.ResourceEntity"/> — a physical
    /// resource lying on the ground (log, ore, etc.) that ages out via LifeTimer.
    /// Extends Physics legacy-side; on the Arch side the same entity already has
    /// <see cref="Physics"/> from family #2 — this struct only adds the
    /// resource-specific fields.
    /// </summary>
    public struct ResourceEntity
    {
        public Resource Resource;
        public float LifeTimerRemaining;
    }
}

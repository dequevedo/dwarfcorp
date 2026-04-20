namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.LightEmitter"/>. The legacy
    /// class held a <c>DynamicLight</c> with <c>Range</c> + <c>Intensity</c> + a
    /// runtime-updated <c>Position</c>; this struct keeps the two serialized knobs.
    /// Position is taken from the entity's <see cref="Transform"/> at render time —
    /// duplicating it here would require two-way synchronization for no gain.
    ///
    /// When the eventual lighting system migrates, it queries entities with
    /// <see cref="LightEmission"/> + <see cref="Transform"/> and publishes them as
    /// dynamic lights each frame.
    /// </summary>
    public struct LightEmission
    {
        public float Range;
        public float Intensity;
    }
}

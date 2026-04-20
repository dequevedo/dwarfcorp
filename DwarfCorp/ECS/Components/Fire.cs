namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Flammable"/>. Timer objects
    /// (CheckLava / Sound / Damage) are runtime state and skipped; when the fire
    /// system migrates it'll track those in its own per-entity scratch rather than
    /// persist them.
    /// </summary>
    public struct Flammable
    {
        public float Heat;
        public float Flashpoint;
        public float Damage;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Fire"/>. Only the lifetime
    /// envelope survives — the sub-Flammable + Health children are separate
    /// concerns migrated by their own families.
    /// </summary>
    public struct Fire
    {
        public float LifeTimerRemaining;
    }
}

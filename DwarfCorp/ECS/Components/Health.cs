namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Health"/> component. Holds the
    /// HP envelope; the Resistances dictionary (legacy <c>Dictionary&lt;DamageType, float&gt;</c>)
    /// is deferred — damage type modeling belongs to the CreatureAI / DamageSystem
    /// migration and lives outside the Health struct for value-type cleanliness.
    ///
    /// Entities get this component if the legacy tree carried a Health node, directly
    /// or via a child, under a root-level ancestor. Systems that tick health (decay,
    /// regen) eventually query by <c>WithAll&lt;Health&gt;()</c>.
    /// </summary>
    public struct Health
    {
        public float Hp;
        public float MaxHealth;
        public float MinHealth;
    }
}

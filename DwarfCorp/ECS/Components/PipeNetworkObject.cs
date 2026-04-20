namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.SteamPipes.PipeNetworkObject"/>.
    /// The persisted knobs: liquid type (0 = empty), draw flag, orientation enum
    /// flattened to byte. Runtime neighbor linkage (Coordinate, NeighborPipes,
    /// Primitive) is JsonIgnored on the legacy side and reconstructed by the pipe
    /// system on load.
    /// </summary>
    public struct PipeNetworkObject
    {
        public byte LiquidType;
        public bool DrawPipes;
        /// <summary>Legacy <c>SteamPipes.Orientation</c> cast to byte.</summary>
        public byte Orientation;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.SteamPipes.BuildBuff"/>. One
    /// float — the crafting-speed multiplier the pipe system applies to the
    /// workstation entity this attaches to.
    /// </summary>
    public struct BuildBuff
    {
        public float BuffMultiplier;
    }
}

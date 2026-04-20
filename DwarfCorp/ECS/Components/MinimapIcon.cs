namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.MinimapIcon"/>. The legacy
    /// class stored <c>NamedImageFrame Icon</c> + a float <c>IconScale</c>. The
    /// icon object is a content-loaded reference type; during the coexistence
    /// phase we keep a reference snapshot (GC root through the Arch entity).
    /// When the minimap render system migrates, Icon becomes an atlas-key handle.
    /// </summary>
    public struct MinimapIcon
    {
        public NamedImageFrame Icon;
        public float IconScale;
    }
}

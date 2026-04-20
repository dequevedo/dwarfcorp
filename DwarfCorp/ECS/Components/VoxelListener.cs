namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Tag component for legacy <see cref="DwarfCorp.GenericVoxelListener"/>,
    /// <see cref="DwarfCorp.VoxelRevealer"/>, and
    /// <see cref="DwarfCorp.DestroyOnTimer"/>-style entities. The legacy classes carry
    /// runtime-only <c>Action&lt;VoxelEvent&gt;</c> handlers that explicitly throw on
    /// serialization (see <c>GenericVoxelListener.Serializer</c>) — there is nothing
    /// to persist except the fact that the entity wanted voxel-change callbacks.
    /// This tag marks the intent so future commits can re-register handlers from
    /// archetype-specific sources (entity factories, inventory contents).
    /// </summary>
    public struct VoxelListenerTag { }
}

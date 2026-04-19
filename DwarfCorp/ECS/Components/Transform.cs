using Microsoft.Xna.Framework;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Placeholder Arch component representing an entity's world-space transform.
    /// Deliberately minimal — matches the subset of <see cref="Body.GlobalTransform"/>
    /// that most code actually reads (position, orientation, scale) without dragging
    /// in the TransformModule cache, parent/child pointers, or BoundingBox.
    ///
    /// First real component in the ECS namespace. The Transform migration (moving
    /// every GameComponent's position state onto this Arch type) is its own focused
    /// commit and is where ComponentManager starts losing surface area.
    /// </summary>
    public struct Transform
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;

        public static Transform Identity => new Transform
        {
            Position = Vector3.Zero,
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
        };
    }
}

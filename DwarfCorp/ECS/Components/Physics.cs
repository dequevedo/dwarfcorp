using Microsoft.Xna.Framework;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch-side snapshot of a legacy <see cref="DwarfCorp.Physics"/> component. Holds
    /// the simulation state — velocity, gravity, damping — that the physics tick reads
    /// and writes every frame. Transform (position) lives in <see cref="Transform"/>;
    /// this struct is composed alongside it on the same entity.
    ///
    /// Enum fields are flattened to byte so the struct stays blittable. The legacy
    /// <see cref="DwarfCorp.Physics.CollisionMode"/> and <see cref="DwarfCorp.Physics.OrientMode"/>
    /// both fit in one byte; cast back when a system needs the semantics.
    /// </summary>
    public struct Physics
    {
        public Vector3 Velocity;
        public Vector3 Gravity;
        public float Mass;
        public float LinearDamping;
        public float Friction;
        /// <summary>Legacy <c>Physics.CollisionMode</c> cast to byte.</summary>
        public byte CollideMode;
        /// <summary>Legacy <c>Physics.OrientMode</c> cast to byte.</summary>
        public byte Orientation;
        public bool IsInLiquid;
        public bool AllowPhysicsSleep;
        public bool IsSleeping;
    }
}

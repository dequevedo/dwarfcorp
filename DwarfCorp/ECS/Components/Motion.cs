using Microsoft.Xna.Framework;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Follower"/>. Steering-like
    /// "chase a target point with radius + rate" behaviour used by camera followers
    /// and escort companions. TargetPos is cached here; a follower system will
    /// eventually refresh it from a target-entity reference, but the persisted
    /// shape is just the knobs.
    /// </summary>
    public struct Follower
    {
        public float FollowRadius;
        public Vector3 TargetPos;
        public float FollowRate;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Bobber"/>. Sinusoidal Y-bob
    /// animation — small decorative motion on floating items. OrigY is the rest
    /// height; Magnitude/Rate/Offset parameterise the sinusoid.
    /// </summary>
    public struct Bobber
    {
        public float Magnitude;
        public float Rate;
        public float Offset;
        public float OrigY;
    }
}

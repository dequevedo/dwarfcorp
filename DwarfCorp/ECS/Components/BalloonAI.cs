using Microsoft.Xna.Framework;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.BalloonAI"/>. Balloon
    /// courier bringing goods from orbit/sending them back. The PIDController
    /// (velocity controller) is a reference-type beast we keep as-is during
    /// coexistence — a future AI rewrite either replaces it with a value-type
    /// tuple (Kp/Ki/Kd floats) or owns it on the system side.
    /// </summary>
    public struct BalloonAI
    {
        public PIDController VelocityController;
        public Vector3 TargetPosition;
        public float MaxVelocity;
        public float MaxForce;
        /// <summary>Legacy <c>BalloonAI.BalloonState</c> cast to byte.</summary>
        public byte State;
        public Faction Faction;
        public float WaitTimerRemaining;
    }
}

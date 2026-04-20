using System.Collections.Generic;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.KoboldAI"/>. Kobolds are
    /// enemies that steal from the player. Probability + leave-world timer are
    /// the two knobs.
    /// </summary>
    public struct KoboldAI
    {
        public float StealFromPlayerProbability;
        public float LeaveWorldTimerRemaining;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.GremlinAI"/>. Same leave-world
    /// timer as Kobold plus destroy-object probability and optional bomb key.
    /// </summary>
    public struct GremlinAI
    {
        public float DestroyPlayerObjectProbability;
        public string PlantBomb;
        public float LeaveWorldTimerRemaining;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.NecromancerAI"/>. The
    /// skeleton list is reference-heavy; future AISystem migration will
    /// replace with entity handles once skeletons live in Arch.
    /// </summary>
    public struct NecromancerAI
    {
        public List<Skeleton> Skeletons;
        public int MaxSkeletons;
        public float SummonTimerRemaining;
        public float AttackTimerRemaining;
        public float AttackRange;
    }

    /// <summary>
    /// Tag components for AI archetypes that carry NO persisted fields beyond the
    /// base CreatureAI state. They're just markers so the archetype can be
    /// queried on the Arch side (e.g. <c>WithAll&lt;CreatureAI, FairyAITag&gt;</c>).
    /// </summary>
    public struct FairyAITag { }
    public struct BirdAITag { }
    public struct BatAITag { }
    public struct SnakeAITag { }
    public struct GolemAITag { }
    public struct PacingCreatureAITag { }
}

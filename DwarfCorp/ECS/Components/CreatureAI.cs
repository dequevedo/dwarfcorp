using System.Collections.Generic;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.CreatureAI"/>. Captures the
    /// persistent knobs (biography string, minecart flag, last-failure reason).
    /// Task list, Blackboard, Sensor reference, Movement profile, and the
    /// PlanSubscriber (rebuilt on deserialize via OnDeserialized hook legacy-side)
    /// are all deferred — each needs its own migration or belongs to a system
    /// rather than entity state. Follows the same "snapshot what persists, defer
    /// what a system reconstructs" rule the earlier families used.
    /// </summary>
    public struct CreatureAI
    {
        public string Biography;
        public string LastFailedAct;
        public string LastTaskFailureReason;
        public bool MinecartActive;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.DwarfAI"/>. Private-property
    /// bookkeeping the legacy class carried via [JsonProperty] — XP announcement
    /// tracking, idle-task timers, pay/unhappiness counters.
    /// </summary>
    public struct DwarfAI
    {
        public int LastXpAnnouncement;
        public int LastXpAnnouncementStat;
        public int NumDaysNotPaid;
        public double UnhappinessTime;
        public double TimeSinceLastAssignedTask;
    }
}

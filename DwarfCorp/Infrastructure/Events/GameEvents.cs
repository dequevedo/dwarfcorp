namespace DwarfCorp.Infrastructure.Events
{
    /// <summary>
    /// Strongly-typed event messages published through MessagePipe. Each record
    /// is a tiny DTO — publishers fire it into IPublisher&lt;T&gt;, subscribers
    /// pull it from ISubscriber&lt;T&gt; via DI. No string topics, no global
    /// EventHandlers, no allocation on the hot path.
    ///
    /// Pattern for new events: one record per event, named in past tense,
    /// fields are raw data only (no behavior, no live game references that
    /// outlive the frame). Handlers subscribe in their constructor or Init.
    ///
    /// Current events are just enough to prove the pipeline round-trips and
    /// give the Render Inspector something to tail. Real gameplay events
    /// (ChunkInvalidated, DwarfSpawned, TaskCompleted) get added as each
    /// corresponding subsystem migrates in Fases B / C / D.
    /// </summary>

    /// <summary>Fired once, right after <see cref="Services.Initialize"/> finishes building the container.</summary>
    public readonly record struct AppStarted(string Version, string Commit);

    /// <summary>Fired whenever <see cref="GameStates.GameStateManager"/> promotes a new state to active.</summary>
    public readonly record struct GameStateEntered(string StateTypeName);
}

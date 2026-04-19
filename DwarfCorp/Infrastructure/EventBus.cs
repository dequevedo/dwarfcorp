using System;
using MessagePipe;

namespace DwarfCorp.Infrastructure
{
    /// <summary>
    /// Thin static façade around MessagePipe for the rest of the codebase.
    ///
    /// Using DI-resolved <see cref="IPublisher{T}"/> everywhere is the right
    /// long-term pattern, but DwarfCorp has thousands of static call sites
    /// (GameStateManager, DwarfGame, static helpers) that can't easily take
    /// constructor injection. For those, this static helper resolves the
    /// publisher from the Services container on first use and forwards.
    ///
    /// Rules of the road:
    /// - New classes wired through DI should still inject IPublisher&lt;T&gt;
    ///   / ISubscriber&lt;T&gt; directly. This façade exists only for legacy
    ///   static call sites.
    /// - <see cref="PublishIfAvailable{T}"/> silently no-ops when Services
    ///   hasn't been initialized yet (e.g. very early process startup or a
    ///   test that didn't bootstrap the container). Never throws.
    /// </summary>
    public static class EventBus
    {
        /// <summary>Publish a message to any subscribers. No-op if DI isn't ready.</summary>
        public static void PublishIfAvailable<TMessage>(TMessage message)
        {
            try
            {
                var publisher = Services.ProviderOrNull?.GetService(typeof(IPublisher<TMessage>)) as IPublisher<TMessage>;
                publisher?.Publish(message);
            }
            catch
            {
                // Swallowed: publishing is never allowed to crash the caller.
                // If a subscriber throws, MessagePipe already catches and logs it.
            }
        }

        /// <summary>Subscribe directly against the DI container. Returns a disposable for teardown.</summary>
        public static IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        {
            var sub = (ISubscriber<TMessage>)Services.Provider.GetService(typeof(ISubscriber<TMessage>));
            return sub.Subscribe(handler);
        }
    }
}

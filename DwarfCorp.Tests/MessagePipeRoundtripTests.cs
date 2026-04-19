using System.Threading;
using DwarfCorp.Infrastructure;
using DwarfCorp.Infrastructure.Events;
using MessagePipe;
using Xunit;

namespace DwarfCorp.Tests;

/// <summary>
/// Pins the L.3 MessagePipe pipeline end-to-end: after <see cref="Services.Initialize"/>
/// publishers and subscribers resolve from the DI container, delivery is synchronous,
/// and the legacy-callsite façade <see cref="EventBus"/> routes messages through the
/// same bus.
/// </summary>
public class MessagePipeRoundtripTests
{
    [Fact]
    public void Publisher_And_Subscriber_ResolveFromContainer()
    {
        Services.Initialize();
        var pub = Services.Provider.GetService(typeof(IPublisher<AppStarted>));
        var sub = Services.Provider.GetService(typeof(ISubscriber<AppStarted>));
        Assert.NotNull(pub);
        Assert.NotNull(sub);
    }

    [Fact]
    public void EventBus_PublishIfAvailable_DeliversToSubscriber()
    {
        Services.Initialize();
        int received = 0;
        string lastVersion = null;
        using (EventBus.Subscribe<AppStarted>(msg => { Interlocked.Increment(ref received); lastVersion = msg.Version; }))
        {
            EventBus.PublishIfAvailable(new AppStarted("test-v", "deadbeef"));
        }
        Assert.Equal(1, received);
        Assert.Equal("test-v", lastVersion);
    }

    [Fact]
    public void EventBus_PublishIfAvailable_IsNoopWithNoSubscribers()
    {
        Services.Initialize();
        // Should not throw even though nobody subscribes. This is the static
        // call-site contract — publishing is always safe.
        EventBus.PublishIfAvailable(new GameStateEntered("NoOne_Listens"));
    }

    [Fact]
    public void Multiple_Subscribers_Each_Receive_The_Message()
    {
        Services.Initialize();
        int a = 0, b = 0;
        using (EventBus.Subscribe<GameStateEntered>(_ => Interlocked.Increment(ref a)))
        using (EventBus.Subscribe<GameStateEntered>(_ => Interlocked.Increment(ref b)))
        {
            EventBus.PublishIfAvailable(new GameStateEntered("Broadcast"));
        }
        Assert.Equal(1, a);
        Assert.Equal(1, b);
    }
}

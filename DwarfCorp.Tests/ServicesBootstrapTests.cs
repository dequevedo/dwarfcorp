using DwarfCorp.Infrastructure;
using Microsoft.Extensions.Logging;
using Xunit;
using ZLogger;

namespace DwarfCorp.Tests;

/// <summary>
/// Smoke-tests the L.2 DI + logging bootstrap. Catches regressions like
/// "ILogger&lt;T&gt; doesn't resolve anymore" or "ZLogger sink threw at
/// startup" before the live game would crash on them.
/// </summary>
public class ServicesBootstrapTests
{
    private sealed class SomeSubsystem { }

    [Fact]
    public void Initialize_IsIdempotent_DoesNotThrow()
    {
        Services.Initialize();
        Services.Initialize();
        Assert.NotNull(Services.Provider);
    }

    [Fact]
    public void GetLogger_ReturnsNonNullLoggerForAnyCategory()
    {
        Services.Initialize();
        ILogger<SomeSubsystem> log = Services.GetLogger<SomeSubsystem>();
        Assert.NotNull(log);
    }

    [Fact]
    public void Logger_CanEmitMessage_WithoutThrowing()
    {
        Services.Initialize();
        var log = Services.GetLogger<SomeSubsystem>();
        // Any of these must survive — if ZLogger's sink is wired wrong,
        // this is where we'll see it first.
        log.ZLogInformation($"test info message: {42}");
        log.ZLogWarning($"test warning: {true}");
        log.ZLogError($"test error: {"oops"}");
    }
}

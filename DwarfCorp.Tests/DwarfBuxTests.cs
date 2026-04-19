using Xunit;

namespace DwarfCorp.Tests;

/// <summary>
/// Pins the arithmetic contract of DwarfBux — the in-game currency value type.
/// Saves and economy balance tests will lean on this working identically after
/// any refactor.
/// </summary>
public class DwarfBuxTests
{
    [Fact]
    public void Addition_ProducesExpectedSum()
    {
        DwarfBux a = 10m;
        DwarfBux b = 5m;
        Assert.Equal(15m, (decimal)(a + b));
    }

    [Fact]
    public void Subtraction_ProducesExpectedDifference()
    {
        DwarfBux a = 10m;
        DwarfBux b = 3m;
        Assert.Equal(7m, (decimal)(a - b));
    }

    [Fact]
    public void Negation_FlipsSign()
    {
        DwarfBux v = 42m;
        Assert.Equal(-42m, (decimal)(-v));
    }

    [Fact]
    public void MultiplyByInt_Scales()
    {
        DwarfBux price = 7m;
        Assert.Equal(21m, (decimal)(price * 3));
    }

    [Fact]
    public void ImplicitFromDecimal_And_ImplicitToDecimal_Roundtrip()
    {
        decimal start = 123.45m;
        DwarfBux mid = start;
        decimal back = mid;
        Assert.Equal(start, back);
    }
}

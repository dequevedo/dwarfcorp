using Xunit;

namespace DwarfCorp.Tests;

/// <summary>
/// Pins the determinism contract of the noise generator. World gen and
/// vertex noise both depend on same-seed → same-value, so a future SIMD
/// rewrite of FastNoiseLite must not regress this.
/// NOTE: DwarfCorp namespace has its own legacy `Perlin` class (in
/// DwarfCorp/Tools/Math/Perlin.cs) with a different API. These tests
/// target the LibNoise.Perlin one, so we spell it out every time.
/// </summary>
public class PerlinDeterminismTests
{
    [Fact]
    public void SameSeed_SameInput_ReturnsIdenticalValue()
    {
        var a = new LibNoise.Perlin(seed: 1337);
        var b = new LibNoise.Perlin(seed: 1337);
        Assert.Equal(a.GetValue(0.5, 1.25, -3.75), b.GetValue(0.5, 1.25, -3.75));
    }

    [Fact]
    public void DifferentSeeds_ReturnDifferentValues()
    {
        var a = new LibNoise.Perlin(seed: 1);
        var b = new LibNoise.Perlin(seed: 2);
        // Any single point could collide, so sample a few and require at least one difference.
        double sumA = a.GetValue(0.1, 0.2, 0.3) + a.GetValue(10, 20, 30) + a.GetValue(-5, -5, -5);
        double sumB = b.GetValue(0.1, 0.2, 0.3) + b.GetValue(10, 20, 30) + b.GetValue(-5, -5, -5);
        Assert.NotEqual(sumA, sumB);
    }

    [Fact]
    public void MultipleCalls_SameInstanceSameInput_Stable()
    {
        var p = new LibNoise.Perlin(seed: 42);
        double first = p.GetValue(1.0, 2.0, 3.0);
        double second = p.GetValue(1.0, 2.0, 3.0);
        double third = p.GetValue(1.0, 2.0, 3.0);
        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }
}

using CowColonySim.Sim.Terrain;
using Xunit;

namespace CowColonySim.Tests;

public class HeightfieldGeneratorTests
{
    [Fact]
    public void Same_seed_yields_same_field()
    {
        var a = new Heightfield(32, 32);
        var b = new Heightfield(32, 32);
        HeightfieldGenerator.Generate(a, seed: 42, GenerationSettings.GentleHills);
        HeightfieldGenerator.Generate(b, seed: 42, GenerationSettings.GentleHills);
        for (var vy = 0; vy < a.VertHeight; vy++)
            for (var vx = 0; vx < a.VertWidth; vx++)
                Assert.Equal(a.Get(vx, vy), b.Get(vx, vy));
    }

    [Fact]
    public void Different_seeds_produce_different_fields()
    {
        var a = new Heightfield(32, 32);
        var b = new Heightfield(32, 32);
        HeightfieldGenerator.Generate(a, seed: 1, GenerationSettings.GentleHills);
        HeightfieldGenerator.Generate(b, seed: 2, GenerationSettings.GentleHills);

        var diffs = 0;
        for (var vy = 0; vy < a.VertHeight; vy++)
            for (var vx = 0; vx < a.VertWidth; vx++)
                if (a.Get(vx, vy) != b.Get(vx, vy)) diffs++;
        Assert.True(diffs > 100, $"expected meaningful divergence, only got {diffs}");
    }

    [Fact]
    public void All_samples_within_amplitude()
    {
        var f = new Heightfield(64, 64);
        var s = GenerationSettings.GentleHills;
        HeightfieldGenerator.Generate(f, seed: 7, s);
        for (var vy = 0; vy < f.VertHeight; vy++)
            for (var vx = 0; vx < f.VertWidth; vx++)
            {
                var q = f.Get(vx, vy);
                Assert.InRange(q, (short)-s.AmplitudeQuanta, (short)s.AmplitudeQuanta);
            }
    }

    [Fact]
    public void Generator_produces_some_variation()
    {
        var f = new Heightfield(64, 64);
        HeightfieldGenerator.Generate(f, seed: 3, GenerationSettings.GentleHills);
        short min = short.MaxValue, max = short.MinValue;
        for (var vy = 0; vy < f.VertHeight; vy++)
            for (var vx = 0; vx < f.VertWidth; vx++)
            {
                var q = f.Get(vx, vy);
                if (q < min) min = q;
                if (q > max) max = q;
            }
        Assert.True(max - min >= 4, $"flat field, range was {max - min}");
    }
}

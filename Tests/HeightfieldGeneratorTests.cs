using CowColonySim.Sim.Terrain;
using Xunit;

namespace CowColonySim.Tests;

public class HeightfieldGeneratorTests
{
    [Fact]
    public void Same_seed_gives_same_field()
    {
        var a = new Heightfield(16, 16);
        var b = new Heightfield(16, 16);
        var settings = new HeightfieldGenerator.Settings { Seed = 12345 };
        HeightfieldGenerator.Generate(a, settings);
        HeightfieldGenerator.Generate(b, settings);
        Assert.Equal(a.AsReadOnlySpan().ToArray(), b.AsReadOnlySpan().ToArray());
    }

    [Fact]
    public void Different_seed_gives_different_field()
    {
        var a = new Heightfield(16, 16);
        var b = new Heightfield(16, 16);
        HeightfieldGenerator.Generate(a, new HeightfieldGenerator.Settings { Seed = 1 });
        HeightfieldGenerator.Generate(b, new HeightfieldGenerator.Settings { Seed = 2 });
        Assert.NotEqual(a.AsReadOnlySpan().ToArray(), b.AsReadOnlySpan().ToArray());
    }

    [Fact]
    public void Output_stays_within_amplitude()
    {
        var hf = new Heightfield(32, 32);
        var settings = new HeightfieldGenerator.Settings { Amplitude = 20 };
        HeightfieldGenerator.Generate(hf, settings);
        foreach (var h in hf.AsReadOnlySpan())
        {
            Assert.InRange(h, (short)-20, (short)20);
        }
    }

    [Fact]
    public void Generate_bumps_version()
    {
        var hf = new Heightfield(8, 8);
        var v0 = hf.Version;
        HeightfieldGenerator.Generate(hf, new HeightfieldGenerator.Settings());
        Assert.True(hf.Version > v0);
    }

    [Fact]
    public void Default_settings_produce_real_relief()
    {
        var hf = new Heightfield(64, 64);
        HeightfieldGenerator.Generate(hf, new HeightfieldGenerator.Settings());
        short min = short.MaxValue, max = short.MinValue;
        foreach (var h in hf.AsReadOnlySpan())
        {
            if (h < min) min = h;
            if (h > max) max = h;
        }
        Assert.True(max - min > 2,
            $"default settings produced flat plane: range {min}..{max}. " +
            $"Probably hit the record-struct parameterless-ctor trap again.");
    }
}

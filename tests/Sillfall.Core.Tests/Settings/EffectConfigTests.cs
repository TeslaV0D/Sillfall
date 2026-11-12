using System.Numerics;
using Sillfall.Core.Settings;

namespace Sillfall.Core.Tests.Settings;

public class EffectConfigTests
{
    [Theory]
    [InlineData("#FF8000", 1f, 0.50196f, 0f)]
    [InlineData("00ff00", 0f, 1f, 0f)]
    [InlineData("  #000000 ", 0f, 0f, 0f)]
    public void ParseColor_ReadsHexColours(string text, float r, float g, float b)
    {
        Vector3 color = EffectConfig.ParseColor(text)!.Value;

        color.X.Should().BeApproximately(r, 1e-4f);
        color.Y.Should().BeApproximately(g, 1e-4f);
        color.Z.Should().BeApproximately(b, 1e-4f);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void ParseColor_RejectsAnythingElse(string? text) => EffectConfig.ParseColor(text).Should().BeNull();

    [Fact]
    public void FormatColor_RoundTrips()
    {
        EffectConfig.FormatColor(EffectConfig.ParseColor("#4FD8FF")!.Value).Should().Be("#4FD8FF");
    }

    [Fact]
    public void ToEffectSettings_UsesTheGivenIntensityAndTheColour()
    {
        var config = new EffectConfig { Intensity = 0.2f, Color = "#0000FF", Brightness = 0.5f };

        Sillfall.Core.Effects.EffectSettings settings = config.ToEffectSettings(0.7f);

        settings.Intensity.Should().Be(0.7f);
        settings.Color.Should().Be(new Vector3(0f, 0f, 1f));
        settings.Brightness.Should().Be(0.5f);
    }
}

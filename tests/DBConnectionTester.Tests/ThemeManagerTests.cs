using DBConnectionTester.Models;
using DBConnectionTester.UI;

namespace DBConnectionTester.Tests;

public sealed class ThemeManagerTests
{
    [Fact]
    public void DarkPaletteMaintainsReadableContrast()
    {
        var palette = ThemeManager.PaletteFor(ApplicationTheme.Dark);

        Assert.True(Luminance(palette.Text) > Luminance(palette.Surface));
        Assert.True(Luminance(palette.MutedText) > Luminance(palette.Surface));
        Assert.NotEqual(palette.Surface, palette.Selection);
    }

    [Fact]
    public void LightPaletteMaintainsReadableContrast()
    {
        var palette = ThemeManager.PaletteFor(ApplicationTheme.Light);

        Assert.True(Luminance(palette.Text) < Luminance(palette.Surface));
        Assert.True(Luminance(palette.MutedText) < Luminance(palette.Surface));
        Assert.NotEqual(palette.Surface, palette.Selection);
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
}

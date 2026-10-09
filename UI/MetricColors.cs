namespace DBConnectionTester.UI;

/// <summary>Metric tokens shared by results and chart; theme palettes override these standalone defaults.</summary>
public sealed record MetricColors
{
    public Color GridBackground { get; init; } = SystemColors.Window;
    public Color GridHeader { get; init; } = Color.FromArgb(240, 242, 245);
    public Color FailureBackground { get; init; } = Color.FromArgb(255, 242, 242);
    public Color NormalStatisticsBackground { get; init; } = SystemColors.Window;
    public Color Muted { get; init; } = SystemColors.GrayText;
    public Color Attention { get; init; } = Color.FromArgb(185, 45, 45);
    public Color Stable { get; init; } = Color.FromArgb(55, 105, 155);
    public Color Improving { get; init; } = Color.FromArgb(28, 120, 72);
    public Color Degrading { get; init; } = Color.FromArgb(200, 110, 25);
    public Color ChartGrid { get; init; } = Color.FromArgb(220, 224, 229);
    public Color Median { get; init; } = Color.FromArgb(65, 120, 180);
    public Color P95 { get; init; } = Color.FromArgb(220, 135, 45);
    public Color Series { get; init; } = Color.FromArgb(55, 105, 155);
    public Color Failure { get; init; } = Color.FromArgb(190, 50, 50);
    public Color Skipped { get; init; } = Color.FromArgb(160, 165, 170);
}

internal interface IThemePaletteAware
{
    // Called only when applying a theme, after native children have been styled.
    void ApplyPalette(ThemePalette palette);
}

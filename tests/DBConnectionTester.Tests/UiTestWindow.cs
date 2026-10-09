using System.Drawing;
using System.Windows.Forms;

namespace DBConnectionTester.Tests;

internal static class UiTestWindow
{
    // Hosted Windows desktops can be smaller than the layouts under test.
    // An explicit maximum overrides the native desktop tracking limit without
    // changing the application's sizing rules or skipping layout assertions.
    internal static Size MaximumSize => new(2400, 1600);

    internal static string Describe(Form form) =>
        $"bounds={form.Bounds}, client={form.ClientSize}, DPI={form.DeviceDpi}, " +
        $"workingArea={Screen.FromControl(form).WorkingArea}, nativeMaximum={SystemInformation.MaxWindowTrackSize}";
}

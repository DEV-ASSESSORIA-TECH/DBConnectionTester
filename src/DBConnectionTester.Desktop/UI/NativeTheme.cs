using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DBConnectionTester.UI;

/// <summary>Updates native chrome in place. Never creates handles, replaces controls or touches their data.</summary>
internal static class NativeTheme
{
    private sealed class State
    {
        public bool Dark;
        public bool HighContrast;
        public bool? AppliedDark;
        public bool AppliedHighContrast;
        public IntPtr AppliedHandle;
    }

    private static readonly ConditionalWeakTable<Control, State> states = new();

    internal static void Bind(Control control, ThemePalette palette)
    {
        if (!SupportsNativeTheme(control)) return;
        if (!states.TryGetValue(control, out var state))
        {
            state = new State();
            states.Add(control, state);
            control.HandleCreated += (_, _) => Apply(control, state);
            control.HandleDestroyed += (_, _) => state.AppliedHandle = IntPtr.Zero;
        }
        state.Dark = !palette.HighContrast && palette.Window.GetBrightness() < 0.5f;
        state.HighContrast = palette.HighContrast;
        Apply(control, state);
    }

    private static bool SupportsNativeTheme(Control control) => control is Form or ComboBox or CheckBox or RadioButton
        or TextBoxBase or UpDownBase or ListBox or ListView or DataGridView or ScrollBar
        || control is ScrollableControl { AutoScroll: true };

    private static void Apply(Control control, State state)
    {
        if (!control.IsHandleCreated || control.IsDisposed || control.Disposing) return;
        var handle = control.Handle;
        if (state.AppliedHandle == handle && state.AppliedDark == state.Dark && state.AppliedHighContrast == state.HighContrast) return;

        var explorer = state.HighContrast ? null : state.Dark ? "DarkMode_Explorer" : "Explorer";
        // ComboBox uses CFD for its button/field and Explorer for its native edit/list children.
        var theme = control is ComboBox ? state.HighContrast ? null : state.Dark ? "DarkMode_CFD" : "CFD" : explorer;
        _ = SetWindowTheme(handle, theme, null);
        if (control is ComboBox)
        {
            var info = new ComboInfo { Size = Marshal.SizeOf<ComboInfo>() };
            if (GetComboBoxInfo(handle, ref info))
            {
                if (info.Item != IntPtr.Zero && info.Item != handle) _ = SetWindowTheme(info.Item, explorer, null);
                if (info.List != IntPtr.Zero) _ = SetWindowTheme(info.List, explorer, null);
            }
        }
        if (control is Form)
        {
            var dark = state.Dark ? 1 : 0;
            _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        }
        state.AppliedHandle = handle;
        state.AppliedDark = state.Dark;
        state.AppliedHighContrast = state.HighContrast;
        // Include the native non-client area (scrollbars/frame) without a synchronous full-panel Refresh.
        _ = RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, 0x0401);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectangle { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboInfo
    {
        public int Size;
        public NativeRectangle ItemBounds, ButtonBounds;
        public uint ButtonState;
        public IntPtr Combo, Item, List;
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr window, string? app, string? ids);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetComboBoxInfo(IntPtr combo, ref ComboInfo info);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr window, IntPtr updateRectangle, IntPtr region, uint flags);
    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

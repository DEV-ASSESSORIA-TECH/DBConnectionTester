using System.Runtime.CompilerServices;

namespace DBConnectionTester.UI;

public enum UiRole
{
    Container, Card, Input, Text, Heading, SecondaryText,
    NeutralAction, PrimaryAction, DestructiveAction, Navigation, NavigationContainer,
    Status, Warning, Error, Success
}

public enum UiState { Normal, Selected, Busy, Warning, Error, Success }

/// <summary>Roles on existing controls; state changes style only that control, without layout or subscriptions.</summary>
public static class UiStyle
{
    private sealed class Metadata
    {
        public UiRole Role;
        public UiState State;
        public ThemePalette? Palette;
        public bool ButtonEventsBound;
    }

    private static readonly ConditionalWeakTable<Control, Metadata> metadata = new();

    public static T WithRole<T>(T control, UiRole role) where T : Control
    {
        SetRole(control, role);
        return control;
    }

    public static void SetRole(Control control, UiRole role)
    {
        var entry = metadata.GetOrCreateValue(control);
        if (entry.Role == role) return;
        entry.Role = role;
        ApplyChangedRoleOrState(control, entry);
    }
    public static void SetState(Control control, UiState state)
    {
        if (!metadata.TryGetValue(control, out var entry))
        {
            entry = metadata.GetOrCreateValue(control);
            entry.Role = DefaultRole(control);
        }
        if (entry.State == state) return;
        entry.State = state;
        ApplyChangedRoleOrState(control, entry);
    }
    public static UiRole GetRole(Control control) => metadata.TryGetValue(control, out var entry) ? entry.Role : DefaultRole(control);
    public static UiState GetState(Control control) => metadata.TryGetValue(control, out var entry) ? entry.State : UiState.Normal;

    internal static void BindPalette(Control control, ThemePalette palette)
    {
        if (!metadata.TryGetValue(control, out var entry))
        {
            entry = metadata.GetOrCreateValue(control);
            entry.Role = DefaultRole(control);
        }
        entry.Palette = palette;
        if (control is Button && !entry.ButtonEventsBound)
        {
            entry.ButtonEventsBound = true;
            control.EnabledChanged += (_, _) =>
            {
                if (entry.Palette is { } current) ThemeManager.ApplyControlColors(control, current);
            };
        }
    }

    private static void ApplyChangedRoleOrState(Control control, Metadata entry)
    {
        // Update only this control; native Enabled styling and theme changes use the same palette.
        if (entry.Palette is not { Roles.IsEmpty: false } palette) return;
        ThemeManager.ApplyControlColors(control, palette);
    }

    private static UiRole DefaultRole(Control control) => control switch
    {
        Button => UiRole.NeutralAction,
        GroupBox => UiRole.Card,
        TextBoxBase or ComboBox or NumericUpDown or ListBox or ListView or DataGridView
            or DateTimePicker or CheckBox or RadioButton => UiRole.Input,
        Label => UiRole.Text,
        _ => UiRole.Container
    };
}

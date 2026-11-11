namespace DBConnectionTester.UI;

// Native layout only: no custom preferred-size calculation or recursive measurement.
internal static class UiLayout
{
    public static TableLayoutPanel Fields(int labelWidth) => new()
    {
        Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        ColumnCount = 2, Padding = new Padding(8), Margin = Padding.Empty,
        ColumnStyles = { new ColumnStyle(SizeType.Absolute, labelWidth), new ColumnStyle(SizeType.Percent, 100) }
    };

    public static FieldRow AddField(TableLayoutPanel table, int row, string caption, Control input)
    {
        table.RowCount = Math.Max(table.RowCount, row + 1);
        while (table.RowStyles.Count <= row) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 6, 6) };
        input.Dock = DockStyle.Top;
        input.AccessibleName = caption.TrimEnd(':');
        input.Margin = new Padding(3, 4, 3, 4);
        table.Controls.Add(label, 0, row);
        table.Controls.Add(input, 1, row);
        return new FieldRow(table, row, label, input);
    }

    internal sealed record FieldRow(TableLayoutPanel Table, int Row, Label Label, Control Input)
    {
        public void SetVisible(bool visible)
        {
            Label.Visible = Input.Visible = visible;
            Table.RowStyles[Row].SizeType = visible ? SizeType.AutoSize : SizeType.Absolute;
            Table.RowStyles[Row].Height = 0;
        }
    }
}

internal sealed class BufferedPanel : Panel
{
    public BufferedPanel() => DoubleBuffered = true;
}

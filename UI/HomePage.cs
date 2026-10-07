using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

public sealed class HomePage : UserControl
{
    private readonly Label storeValue = new() { AutoSize = true };
    private readonly Label scopeValue = new() { AutoSize = true };
    private readonly Label statusValue = new() { AutoSize = true, Text = "Pronto para iniciar." };

    public HomePage(StoreDescriptor store)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Padding = new Padding(28);

        var title = new Label
        {
            Text = "DB Connection Tester",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 22, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        };
        var subtitle = new Label
        {
            Text = "Diagnóstico de conectividade com histórico persistente e portátil.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 24)
        };
        var start = new Button { Text = "Nova execução", AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
        start.Click += (_, _) => NewRunRequested?.Invoke(this, EventArgs.Empty);
        var history = new Button { Text = "Abrir histórico", AutoSize = true, Padding = new Padding(12, 6, 12, 6) };
        history.Click += (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty);

        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 20) };
        actions.Controls.Add(start);
        actions.Controls.Add(history);
        var info = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(14) };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddInfo(info, 0, "Armazenamento:", storeValue);
        AddInfo(info, 1, "Escopo:", scopeValue);
        AddInfo(info, 2, "Estado:", statusValue);

        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        content.Controls.Add(title);
        content.Controls.Add(subtitle);
        content.Controls.Add(actions);
        content.Controls.Add(info);
        Controls.Add(content);
        UpdateStore(store);
    }

    public event EventHandler? NewRunRequested;
    public event EventHandler? HistoryRequested;

    public void UpdateStore(StoreDescriptor store)
    {
        storeValue.Text = store.DatabasePath;
        scopeValue.Text = store.Scope.ToString();
    }

    public void UpdateRunStatus(string status) => statusValue.Text = status;

    private static void AddInfo(TableLayoutPanel table, int row, string caption, Control value)
    {
        table.Controls.Add(new Label { Text = caption, AutoSize = true, Font = new Font(table.Font, FontStyle.Bold), Margin = new Padding(3, 6, 14, 6) }, 0, row);
        value.Margin = new Padding(3, 6, 3, 6);
        table.Controls.Add(value, 1, row);
    }
}

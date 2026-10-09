using DBConnectionTester.Models;

namespace DBConnectionTester.UI;

internal sealed class StorageSelectionForm : Form
{
    private readonly ListView stores = new();
    private readonly Button selectButton = new ThemedButton() { Text = "Usar selecionado", AutoSize = true, Enabled = false };

    public StorageSelectionForm(IReadOnlyList<StoreDescriptor> candidates)
    {
        Font = UiTypography.Body;
        UiStyle.SetRole(selectButton, UiRole.PrimaryAction);

        Text = "Selecionar armazenamento";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 340);
        Size = new Size(900, 420);

        stores.Dock = DockStyle.Fill;
        stores.View = View.Details;
        stores.FullRowSelect = true;
        stores.MultiSelect = false;
        stores.HideSelection = false;
        stores.Columns.Add("Escopo", 130);
        stores.Columns.Add("Local", 480);
        stores.Columns.Add("Último uso", 150);
        foreach (var candidate in candidates.OrderByDescending(item => item.LastOpenedAt))
        {
            var item = new ListViewItem(DisplayScope(candidate.Scope)) { Tag = candidate };
            item.SubItems.Add(candidate.DatabasePath);
            item.SubItems.Add(candidate.LastOpenedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            stores.Items.Add(item);
        }

        stores.SelectedIndexChanged += (_, _) => selectButton.Enabled = stores.SelectedItems.Count == 1;
        stores.DoubleClick += (_, _) => AcceptSelection();
        selectButton.Click += (_, _) => AcceptSelection();
        var cancelButton = new ThemedButton { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 8, 0, 0)
        };
        actions.Controls.Add(cancelButton);
        actions.Controls.Add(selectButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Margin = new Padding(0, 0, 0, 10),
            Text = "Mais de um armazenamento válido foi encontrado. Selecione explicitamente qual histórico deve ser usado. Nenhum arquivo será alterado até a confirmação."
        }, 0, 0);
        layout.Controls.Add(stores, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        Controls.Add(layout);
        AcceptButton = selectButton;
        CancelButton = cancelButton;
    }

    public StoreDescriptor? SelectedStore { get; private set; }

    private void AcceptSelection()
    {
        if (stores.SelectedItems.Count != 1)
            return;
        SelectedStore = (StoreDescriptor)stores.SelectedItems[0].Tag!;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string DisplayScope(StorageScope scope) => scope switch
    {
        StorageScope.LocalUser => "Usuário local",
        StorageScope.SharedMachine => "Compartilhado",
        StorageScope.Portable => "Portátil",
        _ => "Personalizado"
    };
}

using SampleApp.Data;
using SampleApp.Models;

namespace SampleApp;

public partial class MainForm : Form
{
    private readonly CustomerRepository _repository = new();

    public MainForm()
    {
        InitializeComponent();

        menuFileExit.Click += (s, e) => Application.Exit();
        menuCustomerSearch.Click += (s, e) => OpenSearchForm();
        menuCustomerAdd.Click += (s, e) => OpenAddForm();
        menuHelpAbout.Click += (s, e) => MessageBox.Show("SampleApp v1.0", "バージョン情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
        tsbSearch.Click += (s, e) => OpenSearchForm();
        tsbAdd.Click += (s, e) => OpenAddForm();

        Load += (s, e) => LoadCustomers();
    }

    private void LoadCustomers()
    {
        dgvCustomers.DataSource = null;
        dgvCustomers.DataSource = _repository.GetAll().ToList();

        if (dgvCustomers.Columns.Count > 0)
        {
            dgvCustomers.Columns["Id"].HeaderText = "Id";
            dgvCustomers.Columns["Name"].HeaderText = "名前";
            dgvCustomers.Columns["Phone"].HeaderText = "電話番号";
            dgvCustomers.Columns["Email"].HeaderText = "メール";
            dgvCustomers.Columns["Category"].HeaderText = "区分";
            dgvCustomers.Columns["IsActive"].HeaderText = "有効";
        }

        toolStripStatusLabel.Text = $"{_repository.GetAll().Count}件";
    }

    private void OpenSearchForm()
    {
        using var searchForm = new SearchForm(_repository);
        if (searchForm.ShowDialog(this) == DialogResult.OK && searchForm.SelectedCustomerId.HasValue)
        {
            foreach (DataGridViewRow row in dgvCustomers.Rows)
            {
                if (row.DataBoundItem is Customer c && c.Id == searchForm.SelectedCustomerId.Value)
                {
                    row.Selected = true;
                    dgvCustomers.CurrentCell = row.Cells[0];
                    break;
                }
            }
        }
    }

    private void OpenAddForm()
    {
        using var editForm = new CustomerEditForm(_repository);
        if (editForm.ShowDialog(this) == DialogResult.OK)
        {
            LoadCustomers();
        }
    }
}

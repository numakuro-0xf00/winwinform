using SampleApp.Data;
using SampleApp.Models;

namespace SampleApp;

public partial class SearchForm : Form
{
    private readonly CustomerRepository _repository;

    public int? SelectedCustomerId { get; private set; }

    public SearchForm(CustomerRepository repository)
    {
        _repository = repository;
        InitializeComponent();

        btnSearch.Click += BtnSearch_Click;
        btnClear.Click += BtnClear_Click;
        btnSelect.Click += BtnSelect_Click;
    }

    private void BtnSearch_Click(object? sender, EventArgs e)
    {
        var field = cmbSearchField.SelectedItem?.ToString() ?? "名前";
        var condition = txtSearchCondition.Text;
        var partialMatch = chkPartialMatch.Checked;

        var results = _repository.Search(field, condition, partialMatch);
        dgvResults.DataSource = results.ToList();
        lblCount.Text = $"{results.Count}件";
    }

    private void BtnClear_Click(object? sender, EventArgs e)
    {
        txtSearchCondition.Text = "";
        dgvResults.DataSource = null;
        lblCount.Text = "0件";
    }

    private void BtnSelect_Click(object? sender, EventArgs e)
    {
        if (dgvResults.SelectedRows.Count > 0 && dgvResults.SelectedRows[0].DataBoundItem is Customer c)
        {
            SelectedCustomerId = c.Id;
            DialogResult = DialogResult.OK;
        }
    }

    private void BtnClose_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
    }
}

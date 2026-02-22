using SampleApp.Data;
using SampleApp.Models;

namespace SampleApp;

public partial class CustomerEditForm : Form
{
    private readonly CustomerRepository _repository;

    public CustomerEditForm(CustomerRepository repository)
    {
        _repository = repository;
        InitializeComponent();

        btnSave.Click += BtnSave_Click;
        btnCancel.Click += BtnCancel_Click;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var customer = new Customer
        {
            Name = txtName.Text,
            Phone = txtPhone.Text,
            Email = txtEmail.Text,
            Category = cmbCategory.SelectedItem?.ToString() ?? "個人",
            IsActive = chkIsActive.Checked,
        };

        _repository.Add(customer);
        DialogResult = DialogResult.OK;
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
    }
}

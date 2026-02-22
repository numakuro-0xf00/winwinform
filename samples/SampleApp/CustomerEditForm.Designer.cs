namespace SampleApp;

partial class CustomerEditForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        this.txtName = new TextBox();
        this.txtPhone = new TextBox();
        this.txtEmail = new TextBox();
        this.cmbCategory = new ComboBox();
        this.chkIsActive = new CheckBox();
        this.txtPassword = new TextBox();
        this.btnSave = new Button();
        this.btnCancel = new Button();
        this.lblName = new Label();
        this.lblPhone = new Label();
        this.lblEmail = new Label();
        this.lblCategory = new Label();
        this.lblPassword = new Label();
        this.tableLayoutPanel = new TableLayoutPanel();
        this.panelButtons = new Panel();
        this.tableLayoutPanel.SuspendLayout();
        this.panelButtons.SuspendLayout();
        this.SuspendLayout();
        //
        // tableLayoutPanel
        //
        this.tableLayoutPanel.Dock = DockStyle.Fill;
        this.tableLayoutPanel.ColumnCount = 2;
        this.tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        this.tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        this.tableLayoutPanel.RowCount = 7;
        this.tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
        this.tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
        this.tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
        this.tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
        this.tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
        this.tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
        this.tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        this.tableLayoutPanel.Padding = new Padding(10);
        this.tableLayoutPanel.Controls.Add(this.lblName, 0, 0);
        this.tableLayoutPanel.Controls.Add(this.txtName, 1, 0);
        this.tableLayoutPanel.Controls.Add(this.lblPhone, 0, 1);
        this.tableLayoutPanel.Controls.Add(this.txtPhone, 1, 1);
        this.tableLayoutPanel.Controls.Add(this.lblEmail, 0, 2);
        this.tableLayoutPanel.Controls.Add(this.txtEmail, 1, 2);
        this.tableLayoutPanel.Controls.Add(this.lblCategory, 0, 3);
        this.tableLayoutPanel.Controls.Add(this.cmbCategory, 1, 3);
        this.tableLayoutPanel.Controls.Add(this.chkIsActive, 1, 4);
        this.tableLayoutPanel.Controls.Add(this.lblPassword, 0, 5);
        this.tableLayoutPanel.Controls.Add(this.txtPassword, 1, 5);
        //
        // lblName
        //
        this.lblName.Text = "名前:";
        this.lblName.Anchor = AnchorStyles.Left;
        this.lblName.AutoSize = true;
        //
        // txtName
        //
        this.txtName.Name = "txtName";
        this.txtName.Dock = DockStyle.Fill;
        //
        // lblPhone
        //
        this.lblPhone.Text = "電話番号:";
        this.lblPhone.Anchor = AnchorStyles.Left;
        this.lblPhone.AutoSize = true;
        //
        // txtPhone
        //
        this.txtPhone.Name = "txtPhone";
        this.txtPhone.Dock = DockStyle.Fill;
        //
        // lblEmail
        //
        this.lblEmail.Text = "メール:";
        this.lblEmail.Anchor = AnchorStyles.Left;
        this.lblEmail.AutoSize = true;
        //
        // txtEmail
        //
        this.txtEmail.Name = "txtEmail";
        this.txtEmail.Dock = DockStyle.Fill;
        //
        // lblCategory
        //
        this.lblCategory.Text = "区分:";
        this.lblCategory.Anchor = AnchorStyles.Left;
        this.lblCategory.AutoSize = true;
        //
        // cmbCategory
        //
        this.cmbCategory.Name = "cmbCategory";
        this.cmbCategory.Dock = DockStyle.Fill;
        this.cmbCategory.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cmbCategory.Items.AddRange(new object[] { "個人", "法人" });
        this.cmbCategory.SelectedIndex = 0;
        //
        // chkIsActive
        //
        this.chkIsActive.Name = "chkIsActive";
        this.chkIsActive.Text = "有効";
        this.chkIsActive.Checked = true;
        this.chkIsActive.Anchor = AnchorStyles.Left;
        this.chkIsActive.AutoSize = true;
        //
        // lblPassword
        //
        this.lblPassword.Text = "パスワード:";
        this.lblPassword.Anchor = AnchorStyles.Left;
        this.lblPassword.AutoSize = true;
        //
        // txtPassword
        //
        this.txtPassword.Name = "txtPassword";
        this.txtPassword.Dock = DockStyle.Fill;
        this.txtPassword.PasswordChar = '*';
        //
        // panelButtons
        //
        this.panelButtons.Dock = DockStyle.Bottom;
        this.panelButtons.Height = 50;
        this.panelButtons.Controls.Add(this.btnSave);
        this.panelButtons.Controls.Add(this.btnCancel);
        //
        // btnSave
        //
        this.btnSave.Name = "btnSave";
        this.btnSave.Text = "保存";
        this.btnSave.Location = new Point(200, 10);
        this.btnSave.Size = new Size(90, 28);
        //
        // btnCancel
        //
        this.btnCancel.Name = "btnCancel";
        this.btnCancel.Text = "キャンセル";
        this.btnCancel.Location = new Point(300, 10);
        this.btnCancel.Size = new Size(90, 28);
        //
        // CustomerEditForm
        //
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(450, 330);
        this.Controls.Add(this.tableLayoutPanel);
        this.Controls.Add(this.panelButtons);
        this.Name = "CustomerEditForm";
        this.Text = "顧客追加";
        this.StartPosition = FormStartPosition.CenterParent;
        this.tableLayoutPanel.ResumeLayout(false);
        this.tableLayoutPanel.PerformLayout();
        this.panelButtons.ResumeLayout(false);
        this.ResumeLayout(false);
    }

    #endregion

    private TextBox txtName;
    private TextBox txtPhone;
    private TextBox txtEmail;
    private ComboBox cmbCategory;
    private CheckBox chkIsActive;
    private TextBox txtPassword;
    private Button btnSave;
    private Button btnCancel;
    private Label lblName;
    private Label lblPhone;
    private Label lblEmail;
    private Label lblCategory;
    private Label lblPassword;
    private TableLayoutPanel tableLayoutPanel;
    private Panel panelButtons;
}

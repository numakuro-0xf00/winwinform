namespace SampleApp;

partial class SearchForm
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
        this.cmbSearchField = new ComboBox();
        this.txtSearchCondition = new TextBox();
        this.chkPartialMatch = new CheckBox();
        this.btnSearch = new Button();
        this.btnClear = new Button();
        this.dgvResults = new DataGridView();
        this.lblCount = new Label();
        this.btnSelect = new Button();
        var btnClose = new Button();
        this.lblSearchField = new Label();
        this.lblSearchCondition = new Label();
        this.panelTop = new Panel();
        this.panelBottom = new Panel();
        ((System.ComponentModel.ISupportInitialize)this.dgvResults).BeginInit();
        this.panelTop.SuspendLayout();
        this.panelBottom.SuspendLayout();
        this.SuspendLayout();
        //
        // panelTop
        //
        this.panelTop.Dock = DockStyle.Top;
        this.panelTop.Height = 80;
        this.panelTop.Controls.Add(this.lblSearchField);
        this.panelTop.Controls.Add(this.cmbSearchField);
        this.panelTop.Controls.Add(this.lblSearchCondition);
        this.panelTop.Controls.Add(this.txtSearchCondition);
        this.panelTop.Controls.Add(this.chkPartialMatch);
        this.panelTop.Controls.Add(this.btnSearch);
        this.panelTop.Controls.Add(this.btnClear);
        //
        // lblSearchField
        //
        this.lblSearchField.Text = "検索項目:";
        this.lblSearchField.Location = new Point(12, 15);
        this.lblSearchField.AutoSize = true;
        //
        // cmbSearchField
        //
        this.cmbSearchField.Name = "cmbSearchField";
        this.cmbSearchField.Location = new Point(80, 12);
        this.cmbSearchField.Size = new Size(120, 23);
        this.cmbSearchField.DropDownStyle = ComboBoxStyle.DropDownList;
        this.cmbSearchField.Items.AddRange(new object[] { "名前", "電話番号", "メール" });
        this.cmbSearchField.SelectedIndex = 0;
        //
        // lblSearchCondition
        //
        this.lblSearchCondition.Text = "条件:";
        this.lblSearchCondition.Location = new Point(210, 15);
        this.lblSearchCondition.AutoSize = true;
        //
        // txtSearchCondition
        //
        this.txtSearchCondition.Name = "txtSearchCondition";
        this.txtSearchCondition.Location = new Point(250, 12);
        this.txtSearchCondition.Size = new Size(200, 23);
        //
        // chkPartialMatch
        //
        this.chkPartialMatch.Name = "chkPartialMatch";
        this.chkPartialMatch.Text = "部分一致";
        this.chkPartialMatch.Checked = true;
        this.chkPartialMatch.Location = new Point(80, 45);
        this.chkPartialMatch.AutoSize = true;
        //
        // btnSearch
        //
        this.btnSearch.Name = "btnSearch";
        this.btnSearch.Text = "検索";
        this.btnSearch.Location = new Point(250, 42);
        this.btnSearch.Size = new Size(90, 28);
        //
        // btnClear
        //
        this.btnClear.Name = "btnClear";
        this.btnClear.Text = "クリア";
        this.btnClear.Location = new Point(350, 42);
        this.btnClear.Size = new Size(90, 28);
        //
        // dgvResults
        //
        this.dgvResults.Name = "dgvResults";
        this.dgvResults.Dock = DockStyle.Fill;
        this.dgvResults.ReadOnly = true;
        this.dgvResults.AllowUserToAddRows = false;
        this.dgvResults.AllowUserToDeleteRows = false;
        this.dgvResults.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        this.dgvResults.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        //
        // panelBottom
        //
        this.panelBottom.Dock = DockStyle.Bottom;
        this.panelBottom.Height = 45;
        this.panelBottom.Controls.Add(this.lblCount);
        this.panelBottom.Controls.Add(this.btnSelect);
        this.panelBottom.Controls.Add(btnClose);
        //
        // lblCount
        //
        this.lblCount.Name = "lblCount";
        this.lblCount.Text = "0件";
        this.lblCount.Location = new Point(12, 14);
        this.lblCount.AutoSize = true;
        //
        // btnSelect
        //
        this.btnSelect.Name = "btnSelect";
        this.btnSelect.Text = "選択";
        this.btnSelect.Location = new Point(350, 8);
        this.btnSelect.Size = new Size(90, 28);
        //
        // btnClose (intentionally no Name property set)
        //
        btnClose.Text = "閉じる";
        btnClose.Location = new Point(450, 8);
        btnClose.Size = new Size(90, 28);
        btnClose.Click += BtnClose_Click;
        //
        // SearchForm
        //
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(600, 450);
        this.Controls.Add(this.dgvResults);
        this.Controls.Add(this.panelBottom);
        this.Controls.Add(this.panelTop);
        this.Name = "SearchForm";
        this.Text = "顧客検索";
        this.StartPosition = FormStartPosition.CenterParent;
        ((System.ComponentModel.ISupportInitialize)this.dgvResults).EndInit();
        this.panelTop.ResumeLayout(false);
        this.panelTop.PerformLayout();
        this.panelBottom.ResumeLayout(false);
        this.panelBottom.PerformLayout();
        this.ResumeLayout(false);
    }

    #endregion

    private ComboBox cmbSearchField;
    private TextBox txtSearchCondition;
    private CheckBox chkPartialMatch;
    private Button btnSearch;
    private Button btnClear;
    private DataGridView dgvResults;
    private Label lblCount;
    private Button btnSelect;
    private Label lblSearchField;
    private Label lblSearchCondition;
    private Panel panelTop;
    private Panel panelBottom;
}

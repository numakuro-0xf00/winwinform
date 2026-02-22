namespace SampleApp;

partial class MainForm
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
        menuStrip = new MenuStrip();
        menuFile = new ToolStripMenuItem();
        menuFileExit = new ToolStripMenuItem();
        menuCustomer = new ToolStripMenuItem();
        menuCustomerSearch = new ToolStripMenuItem();
        menuCustomerAdd = new ToolStripMenuItem();
        menuHelp = new ToolStripMenuItem();
        menuHelpAbout = new ToolStripMenuItem();
        toolStrip = new ToolStrip();
        tsbSearch = new ToolStripButton();
        tsbAdd = new ToolStripButton();
        dgvCustomers = new DataGridView();
        statusStrip = new StatusStrip();
        toolStripStatusLabel = new ToolStripStatusLabel();
        txtTest = new TextBox();
        btnTest = new Button();
        pnlTest = new Panel();
        menuStrip.SuspendLayout();
        toolStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)dgvCustomers).BeginInit();
        statusStrip.SuspendLayout();
        pnlTest.SuspendLayout();
        SuspendLayout();
        // 
        // menuStrip
        // 
        menuStrip.ImageScalingSize = new Size(24, 24);
        menuStrip.Items.AddRange(new ToolStripItem[] { menuFile, menuCustomer, menuHelp });
        menuStrip.Location = new Point(0, 0);
        menuStrip.Name = "menuStrip";
        menuStrip.Padding = new Padding(9, 3, 0, 3);
        menuStrip.Size = new Size(1143, 35);
        menuStrip.TabIndex = 3;
        // 
        // menuFile
        // 
        menuFile.DropDownItems.AddRange(new ToolStripItem[] { menuFileExit });
        menuFile.Name = "menuFile";
        menuFile.Size = new Size(98, 29);
        menuFile.Text = "ファイル(&F)";
        // 
        // menuFileExit
        // 
        menuFileExit.Name = "menuFileExit";
        menuFileExit.Size = new Size(150, 34);
        menuFileExit.Text = "終了";
        // 
        // menuCustomer
        // 
        menuCustomer.DropDownItems.AddRange(new ToolStripItem[] { menuCustomerSearch, menuCustomerAdd });
        menuCustomer.Name = "menuCustomer";
        menuCustomer.Size = new Size(85, 29);
        menuCustomer.Text = "顧客(&C)";
        // 
        // menuCustomerSearch
        // 
        menuCustomerSearch.Name = "menuCustomerSearch";
        menuCustomerSearch.Size = new Size(186, 34);
        menuCustomerSearch.Text = "検索";
        // 
        // menuCustomerAdd
        // 
        menuCustomerAdd.Name = "menuCustomerAdd";
        menuCustomerAdd.Size = new Size(186, 34);
        menuCustomerAdd.Text = "新規追加";
        // 
        // menuHelp
        // 
        menuHelp.DropDownItems.AddRange(new ToolStripItem[] { menuHelpAbout });
        menuHelp.Name = "menuHelp";
        menuHelp.Size = new Size(95, 29);
        menuHelp.Text = "ヘルプ(&H)";
        // 
        // menuHelpAbout
        // 
        menuHelpAbout.Name = "menuHelpAbout";
        menuHelpAbout.Size = new Size(216, 34);
        menuHelpAbout.Text = "バージョン情報";
        // 
        // toolStrip
        // 
        toolStrip.ImageScalingSize = new Size(24, 24);
        toolStrip.Items.AddRange(new ToolStripItem[] { tsbSearch, tsbAdd });
        toolStrip.Location = new Point(0, 35);
        toolStrip.Name = "toolStrip";
        toolStrip.Padding = new Padding(0, 0, 3, 0);
        toolStrip.Size = new Size(1143, 34);
        toolStrip.TabIndex = 2;
        // 
        // tsbSearch
        // 
        tsbSearch.DisplayStyle = ToolStripItemDisplayStyle.Text;
        tsbSearch.Name = "tsbSearch";
        tsbSearch.Size = new Size(52, 29);
        tsbSearch.Text = "検索";
        // 
        // tsbAdd
        // 
        tsbAdd.DisplayStyle = ToolStripItemDisplayStyle.Text;
        tsbAdd.Name = "tsbAdd";
        tsbAdd.Size = new Size(88, 29);
        tsbAdd.Text = "新規追加";
        // 
        // dgvCustomers
        // 
        dgvCustomers.AllowUserToAddRows = false;
        dgvCustomers.AllowUserToDeleteRows = false;
        dgvCustomers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        dgvCustomers.ColumnHeadersHeight = 34;
        dgvCustomers.Dock = DockStyle.Fill;
        dgvCustomers.Location = new Point(0, 69);
        dgvCustomers.Margin = new Padding(4, 5, 4, 5);
        dgvCustomers.Name = "dgvCustomers";
        dgvCustomers.ReadOnly = true;
        dgvCustomers.RowHeadersWidth = 62;
        dgvCustomers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dgvCustomers.Size = new Size(1143, 582);
        dgvCustomers.TabIndex = 0;
        // 
        // statusStrip
        // 
        statusStrip.ImageScalingSize = new Size(24, 24);
        statusStrip.Items.AddRange(new ToolStripItem[] { toolStripStatusLabel });
        statusStrip.Location = new Point(0, 718);
        statusStrip.Name = "statusStrip";
        statusStrip.Padding = new Padding(1, 0, 20, 0);
        statusStrip.Size = new Size(1143, 32);
        statusStrip.TabIndex = 4;
        // 
        // toolStripStatusLabel
        // 
        toolStripStatusLabel.Name = "toolStripStatusLabel";
        toolStripStatusLabel.Size = new Size(84, 25);
        toolStripStatusLabel.Text = "準備完了";
        // 
        // txtTest
        // 
        txtTest.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        txtTest.Location = new Point(6, 13);
        txtTest.Margin = new Padding(4, 5, 4, 5);
        txtTest.Name = "txtTest";
        txtTest.Size = new Size(906, 31);
        txtTest.TabIndex = 1;
        // 
        // btnTest
        // 
        btnTest.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        btnTest.Location = new Point(980, 7);
        btnTest.Margin = new Padding(4, 5, 4, 5);
        btnTest.Name = "btnTest";
        btnTest.Size = new Size(114, 42);
        btnTest.TabIndex = 0;
        btnTest.Text = "テスト";
        // 
        // pnlTest
        // 
        pnlTest.Controls.Add(btnTest);
        pnlTest.Controls.Add(txtTest);
        pnlTest.Dock = DockStyle.Bottom;
        pnlTest.Location = new Point(0, 651);
        pnlTest.Margin = new Padding(4, 5, 4, 5);
        pnlTest.Name = "pnlTest";
        pnlTest.Padding = new Padding(6, 7, 6, 7);
        pnlTest.Size = new Size(1143, 67);
        pnlTest.TabIndex = 1;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(10F, 25F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1143, 750);
        Controls.Add(dgvCustomers);
        Controls.Add(pnlTest);
        Controls.Add(toolStrip);
        Controls.Add(menuStrip);
        Controls.Add(statusStrip);
        MainMenuStrip = menuStrip;
        Margin = new Padding(4, 5, 4, 5);
        Name = "MainForm";
        Text = "SampleApp - 顧客管理";
        menuStrip.ResumeLayout(false);
        menuStrip.PerformLayout();
        toolStrip.ResumeLayout(false);
        toolStrip.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)dgvCustomers).EndInit();
        statusStrip.ResumeLayout(false);
        statusStrip.PerformLayout();
        pnlTest.ResumeLayout(false);
        pnlTest.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private MenuStrip menuStrip;
    private ToolStripMenuItem menuFile;
    private ToolStripMenuItem menuFileExit;
    private ToolStripMenuItem menuCustomer;
    private ToolStripMenuItem menuCustomerSearch;
    private ToolStripMenuItem menuCustomerAdd;
    private ToolStripMenuItem menuHelp;
    private ToolStripMenuItem menuHelpAbout;
    private ToolStrip toolStrip;
    private ToolStripButton tsbSearch;
    private ToolStripButton tsbAdd;
    private DataGridView dgvCustomers;
    private StatusStrip statusStrip;
    private ToolStripStatusLabel toolStripStatusLabel;
    private Panel pnlTest;
    private TextBox txtTest;
    private Button btnTest;
}

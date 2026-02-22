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
        this.menuStrip = new MenuStrip();
        this.menuFile = new ToolStripMenuItem();
        this.menuFileExit = new ToolStripMenuItem();
        this.menuCustomer = new ToolStripMenuItem();
        this.menuCustomerSearch = new ToolStripMenuItem();
        this.menuCustomerAdd = new ToolStripMenuItem();
        this.menuHelp = new ToolStripMenuItem();
        this.menuHelpAbout = new ToolStripMenuItem();
        this.toolStrip = new ToolStrip();
        this.tsbSearch = new ToolStripButton();
        this.tsbAdd = new ToolStripButton();
        this.dgvCustomers = new DataGridView();
        this.statusStrip = new StatusStrip();
        this.toolStripStatusLabel = new ToolStripStatusLabel();
        this.menuStrip.SuspendLayout();
        this.toolStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)this.dgvCustomers).BeginInit();
        this.statusStrip.SuspendLayout();
        this.SuspendLayout();
        //
        // menuStrip
        //
        this.menuStrip.Name = "menuStrip";
        this.menuStrip.Items.AddRange(new ToolStripItem[] { this.menuFile, this.menuCustomer, this.menuHelp });
        this.menuStrip.Location = new Point(0, 0);
        this.menuStrip.Size = new Size(800, 24);
        //
        // menuFile
        //
        this.menuFile.Name = "menuFile";
        this.menuFile.Text = "ファイル(&F)";
        this.menuFile.DropDownItems.AddRange(new ToolStripItem[] { this.menuFileExit });
        //
        // menuFileExit
        //
        this.menuFileExit.Name = "menuFileExit";
        this.menuFileExit.Text = "終了";
        //
        // menuCustomer
        //
        this.menuCustomer.Name = "menuCustomer";
        this.menuCustomer.Text = "顧客(&C)";
        this.menuCustomer.DropDownItems.AddRange(new ToolStripItem[] { this.menuCustomerSearch, this.menuCustomerAdd });
        //
        // menuCustomerSearch
        //
        this.menuCustomerSearch.Name = "menuCustomerSearch";
        this.menuCustomerSearch.Text = "検索";
        //
        // menuCustomerAdd
        //
        this.menuCustomerAdd.Name = "menuCustomerAdd";
        this.menuCustomerAdd.Text = "新規追加";
        //
        // menuHelp
        //
        this.menuHelp.Name = "menuHelp";
        this.menuHelp.Text = "ヘルプ(&H)";
        this.menuHelp.DropDownItems.AddRange(new ToolStripItem[] { this.menuHelpAbout });
        //
        // menuHelpAbout
        //
        this.menuHelpAbout.Name = "menuHelpAbout";
        this.menuHelpAbout.Text = "バージョン情報";
        //
        // toolStrip
        //
        this.toolStrip.Name = "toolStrip";
        this.toolStrip.Items.AddRange(new ToolStripItem[] { this.tsbSearch, this.tsbAdd });
        this.toolStrip.Location = new Point(0, 24);
        this.toolStrip.Size = new Size(800, 25);
        //
        // tsbSearch
        //
        this.tsbSearch.Name = "tsbSearch";
        this.tsbSearch.Text = "検索";
        this.tsbSearch.DisplayStyle = ToolStripItemDisplayStyle.Text;
        //
        // tsbAdd
        //
        this.tsbAdd.Name = "tsbAdd";
        this.tsbAdd.Text = "新規追加";
        this.tsbAdd.DisplayStyle = ToolStripItemDisplayStyle.Text;
        //
        // dgvCustomers
        //
        this.dgvCustomers.Name = "dgvCustomers";
        this.dgvCustomers.Dock = DockStyle.Fill;
        this.dgvCustomers.Location = new Point(0, 49);
        this.dgvCustomers.ReadOnly = true;
        this.dgvCustomers.AllowUserToAddRows = false;
        this.dgvCustomers.AllowUserToDeleteRows = false;
        this.dgvCustomers.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        this.dgvCustomers.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        //
        // pnlTest
        //
        this.pnlTest = new Panel();
        this.pnlTest.Name = "pnlTest";
        this.pnlTest.Dock = DockStyle.Bottom;
        this.pnlTest.Height = 40;
        this.pnlTest.Padding = new Padding(4);
        this.pnlTest.Controls.Add(this.btnTest);
        this.pnlTest.Controls.Add(this.txtTest);
        //
        // txtTest
        //
        this.txtTest = new TextBox();
        this.txtTest.Name = "txtTest";
        this.txtTest.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        this.txtTest.Location = new Point(4, 8);
        this.txtTest.Size = new Size(700, 23);
        //
        // btnTest
        //
        this.btnTest = new Button();
        this.btnTest.Name = "btnTest";
        this.btnTest.Text = "テスト";
        this.btnTest.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        this.btnTest.Location = new Point(710, 7);
        this.btnTest.Size = new Size(80, 25);
        //
        // statusStrip
        //
        this.statusStrip.Items.AddRange(new ToolStripItem[] { this.toolStripStatusLabel });
        this.statusStrip.Location = new Point(0, 428);
        this.statusStrip.Size = new Size(800, 22);
        //
        // toolStripStatusLabel (intentionally no Name set)
        //
        this.toolStripStatusLabel.Text = "準備完了";
        //
        // MainForm
        //
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(800, 450);
        this.Controls.Add(this.dgvCustomers);
        this.Controls.Add(this.pnlTest);
        this.Controls.Add(this.toolStrip);
        this.Controls.Add(this.menuStrip);
        this.Controls.Add(this.statusStrip);
        this.MainMenuStrip = this.menuStrip;
        this.Name = "MainForm";
        this.Text = "SampleApp - 顧客管理";
        this.menuStrip.ResumeLayout(false);
        this.menuStrip.PerformLayout();
        this.toolStrip.ResumeLayout(false);
        this.toolStrip.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)this.dgvCustomers).EndInit();
        this.statusStrip.ResumeLayout(false);
        this.statusStrip.PerformLayout();
        this.ResumeLayout(false);
        this.PerformLayout();
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

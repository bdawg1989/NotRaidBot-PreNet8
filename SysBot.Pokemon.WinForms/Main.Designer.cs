using SysBot.Pokemon.WinForms.Properties;
using System.Drawing;
using System.Windows.Forms;


namespace SysBot.Pokemon.WinForms
{
    partial class Main
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            TC_Main = new TabControl();
            Tab_Bots = new TabPage();
            CB_Protocol = new ComboBox();
            FLP_Bots = new FlowLayoutPanel();
            TB_IP = new TextBox();
            CB_Routine = new ComboBox();
            NUD_Port = new NumericUpDown();
            B_New = new Button();
            Tab_Hub = new TabPage();
            PG_Hub = new PropertyGrid();
            Tab_Logs = new TabPage();
            RTB_Logs = new RichTextBox();
            B_Stop = new Button();
            B_Start = new Button();
            TC_Main.SuspendLayout();
            Tab_Bots.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)NUD_Port).BeginInit();
            Tab_Hub.SuspendLayout();
            Tab_Logs.SuspendLayout();
            SuspendLayout();
            // Making categories pop
            PG_Hub.CategoryForeColor = Color.Red; // Category text color
            PG_Hub.CategorySplitterColor = Color.Red; // Color of the splitter between categories

            // Styling other elements for a subdued look
            PG_Hub.BackColor = Color.FromArgb(40, 40, 40); // Overall background color
            PG_Hub.ViewBackColor = Color.FromArgb(50, 50, 50); // View area background color
            PG_Hub.ViewForeColor = Color.LightGray; // View area text color
            PG_Hub.LineColor = Color.FromArgb(60, 60, 60); // Line separator color
            PG_Hub.SelectedItemWithFocusForeColor = Color.LightGray; // Selected item foreground color
            PG_Hub.SelectedItemWithFocusBackColor = Color.FromArgb(40, 40, 40); // Selected item background color
                                                                                // Change the text size for various controls
            Font largerFont = new Font("Microsoft Sans Serif", 12F, FontStyle.Regular, GraphicsUnit.Point); // Create a larger font
            TC_Main.Font = largerFont;
            RTB_Logs.Font = largerFont;
            PG_Hub.Font = largerFont;
            B_Stop.Font = largerFont;
            B_Start.Font = largerFont;
            B_New.Font = largerFont;

            // 
            // TC_Main
            // 
            TC_Main.Controls.Add(Tab_Bots);
            TC_Main.Controls.Add(Tab_Hub);
            TC_Main.Controls.Add(Tab_Logs);
            TC_Main.Dock = DockStyle.Fill;
            TC_Main.ForeColor = Color.Red;
            TC_Main.BackColor = Color.FromArgb(40, 40, 40);
            TC_Main.Location = new Point(0, 0);
            TC_Main.Name = "TC_Main";
            TC_Main.SelectedIndex = 0;
            TC_Main.Size = new Size(533, 357);
            TC_Main.TabIndex = 3;
            TC_Main.Padding = new Point(20, 4);

            // 
            // Tab_Bots
            // 
            Tab_Bots.BackColor = Color.FromArgb(40, 40, 40);
            Tab_Bots.Controls.Add(CB_Protocol);
            Tab_Bots.Controls.Add(FLP_Bots);
            Tab_Bots.Controls.Add(TB_IP);
            Tab_Bots.Controls.Add(CB_Routine);
            Tab_Bots.Controls.Add(NUD_Port);
            Tab_Bots.Controls.Add(B_New);
            Tab_Bots.Location = new Point(4, 24);
            Tab_Bots.Name = "Tab_Bots";
            Tab_Bots.Size = new Size(525, 329);
            Tab_Bots.TabIndex = 0;
            Tab_Bots.Text = "Bots";
            // 
            // CB_Protocol
            // 
            CB_Protocol.BackColor = Color.FromArgb(40, 40, 40);
            CB_Protocol.DropDownStyle = ComboBoxStyle.DropDownList;
            CB_Protocol.ForeColor = Color.Red;
            CB_Protocol.FormattingEnabled = true;
            CB_Protocol.Location = new Point(289, 6);
            CB_Protocol.Name = "CB_Protocol";
            CB_Protocol.Size = new Size(67, 23);
            CB_Protocol.TabIndex = 10;
            CB_Protocol.SelectedIndexChanged += CB_Protocol_SelectedIndexChanged;
            // 
            // FLP_Bots
            // 
            FLP_Bots.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            FLP_Bots.BorderStyle = BorderStyle.FixedSingle;
            FLP_Bots.Location = new Point(0, 37);
            FLP_Bots.Margin = new Padding(0);
            FLP_Bots.Name = "FLP_Bots";
            FLP_Bots.Size = new Size(524, 289);
            FLP_Bots.TabIndex = 9;
            FLP_Bots.Paint += FLP_Bots_Paint;
            FLP_Bots.Resize += FLP_Bots_Resize;
            // 
            // TB_IP
            // 
            TB_IP.BackColor = Color.FromArgb(40, 40, 40);
            TB_IP.Font = new Font("Courier New", 12F, FontStyle.Regular, GraphicsUnit.Point);
            TB_IP.ForeColor = Color.Red;
            TB_IP.Location = new Point(73, 7);
            TB_IP.Name = "TB_IP";
            TB_IP.Size = new Size(134, 20);
            TB_IP.TabIndex = 8;
            TB_IP.Text = "192.168.0.1";
            // 
            // CB_Routine
            // 
            CB_Routine.BackColor = Color.FromArgb(40, 40, 40);
            CB_Routine.DropDownStyle = ComboBoxStyle.DropDownList;
            CB_Routine.ForeColor = Color.Red;
            CB_Routine.FormattingEnabled = true;
            CB_Routine.Location = new Point(364, 6);
            CB_Routine.Name = "CB_Routine";
            CB_Routine.Size = new Size(117, 23);
            CB_Routine.TabIndex = 7;
            // 
            // NUD_Port
            // 
            NUD_Port.BackColor = Color.FromArgb(40, 40, 40);
            NUD_Port.Font = new Font("Courier New", 12F, FontStyle.Regular, GraphicsUnit.Point);
            NUD_Port.ForeColor = Color.Red;
            NUD_Port.Location = new Point(215, 7);
            NUD_Port.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
            NUD_Port.Name = "NUD_Port";
            NUD_Port.Size = new Size(68, 20);
            NUD_Port.TabIndex = 6;
            NUD_Port.Value = new decimal(new int[] { 6000, 0, 0, 0 });
            // 
            // B_New
            // 
            B_New.BackColor = Color.FromArgb(70, 70, 70);
            B_New.ForeColor = Color.Red;
            B_New.FlatStyle = FlatStyle.Flat;
            B_New.FlatAppearance.BorderSize = 0;
            B_New.Location = new Point(3, 7);
            B_New.Name = "B_New";
            B_New.Size = new Size(63, 23);
            B_New.TabIndex = 0;
            B_New.Text = "Add";
            B_New.Click += B_New_Click;
            // 
            // Tab_Hub
            // 
            Tab_Hub.BackColor = Color.FromArgb(40, 40, 40);
            Tab_Hub.Controls.Add(PG_Hub);
            Tab_Hub.Location = new Point(4, 24);
            Tab_Hub.Name = "Tab_Hub";
            Tab_Hub.Padding = new Padding(3, 3, 3, 3);
            Tab_Hub.Size = new Size(525, 329);
            Tab_Hub.TabIndex = 2;
            Tab_Hub.Text = "Hub";
            // 
            // PG_Hub
            // 
            PG_Hub.BackColor = Color.FromArgb(40, 40, 40);
            PG_Hub.Dock = DockStyle.Fill;
            PG_Hub.ForeColor = Color.Red;
            PG_Hub.Location = new Point(3, 3);
            PG_Hub.Name = "PG_Hub";
            PG_Hub.PropertySort = PropertySort.Categorized;
            PG_Hub.Size = new Size(519, 323);
            PG_Hub.TabIndex = 0;
            // 
            // Tab_Logs
            // 
            Tab_Logs.BackColor = Color.FromArgb(40, 40, 40);
            Tab_Logs.Controls.Add(RTB_Logs);
            Tab_Logs.Location = new Point(4, 24);
            Tab_Logs.Name = "Tab_Logs";
            Tab_Logs.Size = new Size(525, 329);
            Tab_Logs.TabIndex = 1;
            Tab_Logs.Text = "Logs";
            Tab_Logs.ForeColor = Color.Red;
            // 
            // RTB_Logs
            // 
            RTB_Logs.BackColor = Color.FromArgb(40, 40, 40);
            RTB_Logs.Dock = DockStyle.Fill;
            RTB_Logs.ForeColor = Color.Red;
            RTB_Logs.Location = new Point(0, 0);
            RTB_Logs.Name = "RTB_Logs";
            RTB_Logs.ReadOnly = true;
            RTB_Logs.Size = new Size(525, 329);
            RTB_Logs.TabIndex = 0;
            RTB_Logs.Text = "";
            // 
            // B_Stop
            // 
            B_Stop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            B_Stop.BackColor = Color.FromArgb(40, 40, 40);
            B_Stop.ForeColor = Color.Red;
            B_Stop.FlatStyle = FlatStyle.Flat;
            B_Stop.FlatAppearance.BorderSize = 0;
            B_Stop.Location = new Point(900, 0);
            B_Stop.Name = "B_Stop";
            B_Stop.Size = new Size(75, 30);
            B_Stop.TabIndex = 4;
            B_Stop.Text = "Stop All";
            B_Stop.Click += B_Stop_Click;
            // 
            // B_Start
            // 
            B_Start.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            B_Start.BackColor = Color.FromArgb(40, 40, 40);
            B_Start.ForeColor = Color.Red;
            B_Start.FlatStyle = FlatStyle.Flat;
            B_Start.FlatAppearance.BorderSize = 0;
            B_Start.Location = new Point(815, 0);
            B_Start.Name = "B_Start";
            B_Start.Size = new Size(75, 30);
            B_Start.TabIndex = 3;
            B_Start.Text = "Start All";
            B_Start.Click += B_Start_Click;
            // 
            // Main
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(40, 40, 40);
            ClientSize = new Size(1000, 800);
            Controls.Add(B_Stop);
            Controls.Add(B_Start);
            Controls.Add(TC_Main);
            Icon = Resources.icon;
            MaximizeBox = false;
            Name = "Main";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "NOT RaidBot";
            FormClosing += Main_FormClosing;
            TC_Main.ResumeLayout(false);
            Tab_Bots.ResumeLayout(false);
            Tab_Bots.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)NUD_Port).EndInit();
            Tab_Hub.ResumeLayout(false);
            Tab_Logs.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private TabControl TC_Main;
        private TabPage Tab_Bots;
        private TabPage Tab_Logs;
        private RichTextBox RTB_Logs;
        private TabPage Tab_Hub;
        private PropertyGrid PG_Hub;
        private Button B_Stop;
        private Button B_Start;
        private TextBox TB_IP;
        private ComboBox CB_Routine;
        private NumericUpDown NUD_Port;
        private Button B_New;
        private FlowLayoutPanel FLP_Bots;
        private ComboBox CB_Protocol;
    }
}


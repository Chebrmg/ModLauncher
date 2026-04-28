namespace Launcher
{
    partial class Form1
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

        private void InitializeComponent()
        {
            this.btnStart = new System.Windows.Forms.Button();
            this.btnStartMod = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // btnStart
            // 
            this.btnStart.Location = new System.Drawing.Point(63, 50);
            this.btnStart.Name = "btnStart";
            this.btnStart.Size = new System.Drawing.Size(94, 29);
            this.btnStart.TabIndex = 1;
            this.btnStart.Text = "Запуск";
            this.btnStart.UseVisualStyleBackColor = true;
            this.btnStart.Click += new System.EventHandler(this.btnStart_Click);
            // 
            // btnStartMod
            // 
            this.btnStartMod.Location = new System.Drawing.Point(225, 50);
            this.btnStartMod.Name = "btnStartMod";
            this.btnStartMod.Size = new System.Drawing.Size(124, 29);
            this.btnStartMod.TabIndex = 0;
            this.btnStartMod.Text = "Запуск мода";
            this.btnStartMod.UseVisualStyleBackColor = true;
            this.btnStartMod.Click += new System.EventHandler(this.btnStartMod_Click);
            // 
            // Form1
            // 
            this.ClientSize = new System.Drawing.Size(582, 403);
            this.Controls.Add(this.btnStartMod);
            this.Controls.Add(this.btnStart);
            this.Name = "Form1";
            this.Text = "ModLauncher";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);

        }
        private Label label1;
        private Label label2;
        private Label label3;
        private Button btnStart;
        private Button btnStartMod;
    }
}
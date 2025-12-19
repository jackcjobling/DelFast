namespace DelFast
{
    partial class Form1
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            button1 = new Button();
            openFileDialog1 = new OpenFileDialog();
            button2 = new Button();
            button3 = new Button();
            button4 = new Button();
            button5 = new Button();
            SuspendLayout();
            // 
            // button1
            // 
            button1.Enabled = false;
            button1.Location = new Point(122, 12);
            button1.Name = "button1";
            button1.Size = new Size(98, 23);
            button1.TabIndex = 0;
            button1.Text = "Delete Folders";
            button1.UseVisualStyleBackColor = true;
            button1.Click += btnDeleteFolder;
            // 
            // openFileDialog1
            // 
            openFileDialog1.FileName = "openFileDialog1";
            // 
            // button2
            // 
            button2.BackColor = Color.White;
            button2.ForeColor = Color.Black;
            button2.Location = new Point(12, 12);
            button2.Name = "button2";
            button2.Size = new Size(104, 23);
            button2.TabIndex = 3;
            button2.Text = "Select Folders";
            button2.UseVisualStyleBackColor = false;
            button2.Click += btnSelectFolder;
            // 
            // button3
            // 
            button3.BackColor = Color.White;
            button3.ForeColor = Color.Black;
            button3.Location = new Point(12, 41);
            button3.Name = "button3";
            button3.Size = new Size(104, 23);
            button3.TabIndex = 4;
            button3.Text = "Select Files";
            button3.UseVisualStyleBackColor = false;
            button3.Click += btnSelectFiles;
            // 
            // button4
            // 
            button4.Enabled = false;
            button4.Location = new Point(122, 41);
            button4.Name = "button4";
            button4.Size = new Size(98, 23);
            button4.TabIndex = 5;
            button4.Text = "Delete Files";
            button4.UseVisualStyleBackColor = true;
            button4.Click += btnDeleteFiles;
            // 
            // button5
            // 
            button5.Location = new Point(12, 70);
            button5.Name = "button5";
            button5.Size = new Size(208, 23);
            button5.TabIndex = 6;
            button5.Text = "Generate Benchmark Files";
            button5.UseVisualStyleBackColor = true;
            button5.Click += btnGenerateBenchmarkFiles;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(34, 34, 34);
            ClientSize = new Size(242, 110);
            Controls.Add(button5);
            Controls.Add(button4);
            Controls.Add(button3);
            Controls.Add(button2);
            Controls.Add(button1);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            Name = "Form1";
            Text = "DelFast";
            ResumeLayout(false);
        }

        #endregion

        private Button button1;
        private OpenFileDialog openFileDialog1;
        private Button button2;
        private Button button3;
        private Button button4;
        private Button button5;
    }
}

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
            btnDelFolders = new Button();
            openFileDialog1 = new OpenFileDialog();
            btnSelectFolders = new Button();
            btnFileSelection = new Button();
            btnDeleteAllFiles = new Button();
            btnGenBenchmarkFiles = new Button();
            SuspendLayout();
            // 
            // btnDelFolders
            // 
            btnDelFolders.Enabled = false;
            btnDelFolders.Location = new Point(122, 12);
            btnDelFolders.Name = "btnDelFolders";
            btnDelFolders.Size = new Size(98, 23);
            btnDelFolders.TabIndex = 0;
            btnDelFolders.Text = "Delete Folders";
            btnDelFolders.UseVisualStyleBackColor = true;
            btnDelFolders.Click += btnDeleteFolderClick;
            // 
            // openFileDialog1
            // 
            openFileDialog1.FileName = "openFileDialog1";
            // 
            // btnSelectFolders
            // 
            btnSelectFolders.BackColor = Color.White;
            btnSelectFolders.ForeColor = Color.Black;
            btnSelectFolders.Location = new Point(12, 12);
            btnSelectFolders.Name = "btnSelectFolders";
            btnSelectFolders.Size = new Size(104, 23);
            btnSelectFolders.TabIndex = 3;
            btnSelectFolders.Text = "Select Folders";
            btnSelectFolders.UseVisualStyleBackColor = false;
            btnSelectFolders.Click += btnSelectFolderClick;
            // 
            // btnFileSelection
            // 
            btnFileSelection.BackColor = Color.White;
            btnFileSelection.ForeColor = Color.Black;
            btnFileSelection.Location = new Point(12, 41);
            btnFileSelection.Name = "btnFileSelection";
            btnFileSelection.Size = new Size(104, 23);
            btnFileSelection.TabIndex = 4;
            btnFileSelection.Text = "Select Files";
            btnFileSelection.UseVisualStyleBackColor = false;
            btnFileSelection.Click += btnSelectFilesClick;
            // 
            // btnDeleteAllFiles
            // 
            btnDeleteAllFiles.Enabled = false;
            btnDeleteAllFiles.Location = new Point(122, 41);
            btnDeleteAllFiles.Name = "btnDeleteAllFiles";
            btnDeleteAllFiles.Size = new Size(98, 23);
            btnDeleteAllFiles.TabIndex = 5;
            btnDeleteAllFiles.Text = "Delete Files";
            btnDeleteAllFiles.UseVisualStyleBackColor = true;
            btnDeleteAllFiles.Click += btnDeleteFilesClick;
            // 
            // btnGenBenchmarkFiles
            // 
            btnGenBenchmarkFiles.Location = new Point(12, 70);
            btnGenBenchmarkFiles.Name = "btnGenBenchmarkFiles";
            btnGenBenchmarkFiles.Size = new Size(208, 23);
            btnGenBenchmarkFiles.TabIndex = 6;
            btnGenBenchmarkFiles.Text = "Generate Benchmark Files";
            btnGenBenchmarkFiles.UseVisualStyleBackColor = true;
            btnGenBenchmarkFiles.Click += btnGenerateBenchmarkFilesClick;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(34, 34, 34);
            ClientSize = new Size(242, 110);
            Controls.Add(btnGenBenchmarkFiles);
            Controls.Add(btnDeleteAllFiles);
            Controls.Add(btnFileSelection);
            Controls.Add(btnSelectFolders);
            Controls.Add(btnDelFolders);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            Name = "Form1";
            Text = "DelFast";
            ResumeLayout(false);
        }

        #endregion

        private Button btnDelFolders;
        private OpenFileDialog openFileDialog1;
        private Button btnSelectFolders;
        private Button btnFileSelection;
        private Button btnDeleteAllFiles;
        private Button btnGenBenchmarkFiles;
    }
}

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;

namespace DelFast
{
    public partial class Form1 : Form
    {
        private List<string> _selectedFolders = new List<string>();
        private List<string> _selectedFiles = new List<string>();

        public Form1()
        {
            InitializeComponent();
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            button1.Enabled = false;
            button4.Enabled = false;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog folderBrowserDialog1 = new FolderBrowserDialog())
            {
                folderBrowserDialog1.Description = "Select a folder to add to the list";
                if (folderBrowserDialog1.ShowDialog() == DialogResult.OK)
                {
                    _selectedFolders.Add(folderBrowserDialog1.SelectedPath);
                    MessageBox.Show($"Added folder: {folderBrowserDialog1.SelectedPath}\nTotal folders: {_selectedFolders.Count}", "Folder Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    button1.Enabled = true;
                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (_selectedFolders.Count == 0) return;

            DialogResult result = MessageBox.Show($"Are you sure you want to delete {_selectedFolders.Count} folders?", "Confirm Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                foreach (string folder in _selectedFolders)
                {
                    ExecuteCmd($"/C rd /s /q \"{folder}\"");
                }
                MessageBox.Show("Selected folders deleted successfully!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _selectedFolders.Clear();
                button1.Enabled = false;
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog1 = new OpenFileDialog())
            {
                openFileDialog1.Multiselect = true;
                openFileDialog1.Title = "Select files to delete";
                if (openFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    foreach (string file in openFileDialog1.FileNames)
                    {
                        if (!_selectedFiles.Contains(file))
                        {
                            _selectedFiles.Add(file);
                        }
                    }
                    MessageBox.Show($"Added {openFileDialog1.FileNames.Length} files.\nTotal files: {_selectedFiles.Count}", "Files Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    button4.Enabled = true;
                }
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (_selectedFiles.Count == 0) return;

            DialogResult result = MessageBox.Show($"Are you sure you want to delete {_selectedFiles.Count} files?", "Confirm Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                foreach (string file in _selectedFiles)
                {
                    ExecuteCmd($"/C del /f /q \"{file}\"");
                }
                MessageBox.Show("Selected files deleted successfully!", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                _selectedFiles.Clear();
                button4.Enabled = false;
            }
        }

        private void ExecuteCmd(string arguments)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    FileName = "cmd.exe",
                    Arguments = arguments
                };
                Process process = new Process { StartInfo = startInfo };
                process.Start();
                process.WaitForExit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
using System.Diagnostics;

namespace DelFast
{
    public partial class Form1 : Form
    {
        private readonly HashSet<string> _selectedFolders = [];
        private readonly HashSet<string> _selectedFiles = [];

        public Form1()
        {
            InitializeComponent();
            FormBorderStyle = FormBorderStyle.FixedSingle;
            btnDelFolders.Enabled = false;
            btnDeleteAllFiles.Enabled = false;
        }

        private void btnSelectFolderClick(object sender, EventArgs e)
        {
            using var folderBrowserDialog1 = new FolderBrowserDialog();
            folderBrowserDialog1.Description = "Select a folder to add to the list";
            if (folderBrowserDialog1.ShowDialog() != DialogResult.OK) 
                return;

            _selectedFolders.Add(folderBrowserDialog1.SelectedPath);
            MessageBox.Show($"Added folder: {folderBrowserDialog1.SelectedPath}\nTotal folders: {_selectedFolders.Count}", "Folder Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            btnDelFolders.Enabled = true;
        }

        private void btnDeleteFolderClick(object sender, EventArgs e)
        {
            if (_selectedFolders.Count == 0) return;

            var result = MessageBox.Show($"Are you sure you want to delete {_selectedFolders.Count} folders?", "Confirm Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) 
                return;
            
            var sw = Stopwatch.StartNew();
            foreach (var folder in _selectedFolders)
                Directory.Delete(folder, recursive: true);

            sw.Stop();
            MessageBox.Show($"Selected folders deleted successfully!\nTime elapsed: {sw.ElapsedMilliseconds} ms", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _selectedFolders.Clear();
            btnDelFolders.Enabled = false;
        }

        private void btnSelectFilesClick(object sender, EventArgs e)
        {
            openFileDialog1.Multiselect = true;
            openFileDialog1.Title = "Select files to delete";
            if (openFileDialog1.ShowDialog() != DialogResult.OK) 
                return;
            
            foreach (var file in openFileDialog1.FileNames) _selectedFiles.Add(file);

            MessageBox.Show($"Added {openFileDialog1.FileNames.Length} files.\nTotal files: {_selectedFiles.Count}", "Files Added", MessageBoxButtons.OK, MessageBoxIcon.Information);
            btnDeleteAllFiles.Enabled = true;
        }

        private void btnDeleteFilesClick(object sender, EventArgs e)
        {
            if (_selectedFiles.Count == 0) return;

            var result = MessageBox.Show($"Are you sure you want to delete {_selectedFiles.Count} files?", "Confirm Deletion", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) 
                return;
            var sw = Stopwatch.StartNew();
            foreach (var file in _selectedFiles)
            {
                // API Method
                File.SetAttributes(file, FileAttributes.Normal); // optional
                File.Delete(file);
            }
            sw.Stop();
            MessageBox.Show($"Selected files deleted successfully!\nTime elapsed: {sw.ElapsedMilliseconds} ms", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _selectedFiles.Clear();
            btnDeleteAllFiles.Enabled = false;
        }

        private void btnGenerateBenchmarkFilesClick(object sender, EventArgs e)
        {
            try
            {
                var baseDir = Path.Combine(Directory.GetCurrentDirectory(), "BenchmarkFiles");
                if (Directory.Exists(baseDir))
                {
                    var r = MessageBox.Show("BenchmarkFiles directory already exists. Delete it and recreate?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes)
                        Directory.Delete(baseDir, true);
                    else
                        return;
                }

                Directory.CreateDirectory(baseDir);

                var data = new byte[1024 * 1024]; // 1MB
                new Random().NextBytes(data);

                for (var i = 0; i < 100; i++)
                    File.WriteAllBytes(Path.Combine(baseDir, $"file_{i}.bin"), data);

                var subDirs = new[] { "A", "B", "C" };
                foreach (var sub in subDirs)
                {
                    var subPath = Path.Combine(baseDir, sub);
                    Directory.CreateDirectory(subPath);

                    for (var i = 0; i < 100; i++)
                        File.WriteAllBytes(Path.Combine(subPath, $"file_{i}.bin"), data);
                }

                MessageBox.Show($"Created 400 1MB files in {baseDir}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating benchmark files: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
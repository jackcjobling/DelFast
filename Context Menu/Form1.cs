using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace DelFastContextMenu
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            HandleDelete();
            Close();
        }

        private void HandleDelete()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length < 2) return;

            string path = args[1];

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = Directory.Exists(path)
                            ? $"/C rd /s /q \"{path}\""
                            : $"/C del /f /q \"{path}\"",
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    using var process = Process.Start(psi);
                    process?.WaitForExit();

                    if (Directory.Exists(path) || File.Exists(path))
                        throw new IOException("Delete failed");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"DelFast could not delete:\n{path}\n\n{ex.Message}",
                        "DelFast Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }
    }
}

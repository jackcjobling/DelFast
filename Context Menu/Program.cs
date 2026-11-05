using System.IO.Pipes;
using System.Runtime.InteropServices;
using DelFast.Core;

namespace DelFastContextMenu
{
    /// <summary>
    /// Explorer starts one process per selected item. The first becomes the leader and collects the
    /// others' paths over a pipe, so a multi-selection gets one prompt and one shared worker pool.
    /// </summary>
    internal static class Program
    {
        private const string Name = "DelFast.ContextMenu.Collector";
        private static readonly TimeSpan QuietWindow = TimeSpan.FromMilliseconds(300);

        private const uint MB_OK = 0x0, MB_YESNO = 0x4, MB_ICONERROR = 0x10, MB_ICONWARNING = 0x30, MB_DEFBUTTON2 = 0x100, MB_SETFOREGROUND = 0x10000;
        private const int IDYES = 6;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MessageBoxW(nint owner, string text, string caption, uint type);

        private static int Main(string[] args)
        {
            if (args.Length == 0) return 2;

            using var mutex = new Mutex(true, Name, out var isLeader);
            if (!isLeader && TrySendToLeader(args)) return 0;

            var paths = isLeader ? Collect(args) : args.ToList();
            if (!Confirm(paths)) return 1;

            var result = FastDeleter.Delete(paths);
            if (result.Success) return 0;

            var first = result.Failures[0];
            MessageBoxW(0, $"Could not delete {result.FailureCount:N0} item(s).\n\n{first.Path}\n\n{first.Reason}",
                "DelFast", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
            return 1;
        }

        private static bool TrySendToLeader(string[] args)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
                pipe.Connect(500);
                using var writer = new StreamWriter(pipe);
                foreach (var a in args) writer.WriteLine(a);
                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                return false; // the leader has moved on; this item prompts by itself
            }
        }

        private static List<string> Collect(string[] own)
        {
            var paths = new List<string>(own);
            while (true)
            {
                using var server = new NamedPipeServerStream(Name, PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                using var quiet = new CancellationTokenSource(QuietWindow);
                try
                {
                    server.WaitForConnectionAsync(quiet.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    return paths;
                }

                using var reader = new StreamReader(server);
                while (reader.ReadLine() is { } line)
                    if (line.Length > 0) paths.Add(line);
            }
        }

        private static bool Confirm(List<string> paths)
        {
            const int shown = 12;
            var list = string.Join("\n", paths.Take(shown));
            if (paths.Count > shown) list += $"\n...and {paths.Count - shown} more";

            return MessageBoxW(0, $"Permanently delete {paths.Count} item(s)? This skips the Recycle Bin.\n\n{list}",
                "DelFast", MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2 | MB_SETFOREGROUND) == IDYES;
        }
    }
}

using System.Diagnostics;
using DelFast.Core;

// dfast-bench selftest <scratchDir>
// dfast-bench bench <scratchDir> <fileCount> <fileBytes> <rounds> [threads,threads,...]
// All work happens in a new DelFastBench_* folder created inside scratchDir.

if (args.Length < 2) return Usage();
var scratch = Path.Combine(Path.GetFullPath(args[1]), "DelFastBench_" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(scratch);

try
{
    switch (args[0])
    {
        case "selftest":
            return SelfTest.Run(scratch);
        case "bench" when args.Length >= 5:
            var threads = args.Length > 5
                ? args[5].Split(',').Select(int.Parse).ToArray()
                : [1, 2, 4, 8, 16, 32];
            return Benchmark.Run(scratch, int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), threads);
        default:
            return Usage();
    }
}
finally
{
    if (Directory.Exists(scratch)) FastDeleter.Delete([scratch]);
}

static int Usage()
{
    Console.WriteLine("dfast-bench selftest <scratchDir>");
    Console.WriteLine("dfast-bench bench <scratchDir> <fileCount> <fileBytes> <rounds> [t1,t2,...]");
    return 2;
}

static class Benchmark
{
    public static int Run(string scratch, int fileCount, int fileBytes, int rounds, int[] threads)
    {
        var methods = new List<(string Name, Action<string> Delete)>
        {
            ("rd /s /q", RunRd),
            ("Directory.Delete (original)", p => Directory.Delete(p, true)),
        };
        foreach (var t in threads)
            methods.Add(($"FastDeleter t={t}", p => Check(FastDeleter.Delete([p], t))));

        var times = methods.ToDictionary(m => m.Name, _ => new List<double>());
        Console.WriteLine($"{fileCount:N0} files of {fileBytes:N0} bytes, {rounds} rounds, on {Path.GetPathRoot(scratch)}");

        for (var round = 0; round < rounds; round++)
        {
            foreach (var (name, delete) in methods)
            {
                var tree = Path.Combine(scratch, "tree");
                Generate(tree, fileCount, fileBytes);
                Thread.Sleep(1500); // let the lazy writer calm down from generation
                var sw = Stopwatch.StartNew();
                delete(tree);
                sw.Stop();
                if (Directory.Exists(tree)) throw new Exception($"{name} left the tree behind");
                times[name].Add(sw.Elapsed.TotalSeconds);
                Console.WriteLine($"  round {round + 1}  {name,-30} {sw.Elapsed.TotalSeconds,8:N3} s");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{"method",-30} {"median s",9} {"files/s",10} {"vs original",12}");
        var baseline = Median(times["Directory.Delete (original)"]);
        foreach (var (name, _) in methods)
        {
            var m = Median(times[name]);
            Console.WriteLine($"{name,-30} {m,9:N3} {fileCount / m,10:N0} {baseline / m,11:N2}x");
        }
        return 0;
    }

    private static void Check(DeleteResult r)
    {
        if (!r.Success) throw new Exception($"{r.FailureCount} failures, first: {r.Failures[0]}");
    }

    private static void RunRd(string path)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c rd /s /q \"{path}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        p.WaitForExit();
    }

    private static double Median(List<double> values)
    {
        var s = values.Order().ToArray();
        return s[s.Length / 2];
    }

    /// <summary>A build-output shape: three levels of folders with 50 files each.</summary>
    private static void Generate(string root, int fileCount, int fileBytes)
    {
        const int filesPerDir = 50;
        var data = new byte[fileBytes];
        new Random(42).NextBytes(data);
        var dirCount = Math.Max(1, fileCount / filesPerDir);

        Parallel.For(0, dirCount, new ParallelOptions { MaxDegreeOfParallelism = 16 }, d =>
        {
            var dir = Path.Combine(root, $"a{d % 20}", $"b{d % 400}", $"c{d}");
            Directory.CreateDirectory(dir);
            for (var f = 0; f < filesPerDir; f++)
                File.WriteAllBytes(Path.Combine(dir, $"file_{f}.dat"), data);
        });
    }
}

static class SelfTest
{
    private static int _failed;

    public static int Run(string scratch)
    {
        Test("nested tree removed, counts right", () =>
        {
            var r = Dir(scratch, "tree");
            for (var i = 0; i < 5; i++)
            {
                var sub = Dir(r, $"sub{i}", "deeper");
                for (var f = 0; f < 10; f++) Touch(Path.Combine(sub, $"{f}.txt"));
            }
            var res = FastDeleter.Delete([r]);
            Assert(res.Success, Why(res));
            Assert(!Directory.Exists(r), "tree remains");
            Assert(res.Files == 50, $"files={res.Files}");
            Assert(res.Directories == 11, $"dirs={res.Directories}");
        });

        Test("20k files in one folder (delete while listing)", () =>
        {
            var r = Dir(scratch, "wide");
            Parallel.For(0, 20000, i => Touch(Path.Combine(r, $"f{i}.txt")));
            var res = FastDeleter.Delete([r]);
            Assert(res.Success, Why(res));
            Assert(res.Files == 20000, $"files={res.Files}");
            Assert(!Directory.Exists(r), "folder remains");
        });

        Test("JUNCTION inside tree: link gone, TARGET UNTOUCHED", () =>
        {
            var target = Dir(scratch, "junction_target");
            var keep = Path.Combine(target, "keep.txt");
            Touch(keep);
            var r = Dir(scratch, "with_junction");
            Touch(Path.Combine(r, "x.txt"));
            Junction(Path.Combine(r, "link"), target);
            var res = FastDeleter.Delete([r]);
            Assert(res.Success, Why(res));
            Assert(!Directory.Exists(r), "tree remains");
            Assert(File.Exists(keep), "DELETED THROUGH THE JUNCTION");
            Directory.Delete(target, true);
        });

        Test("junction as the listed path: link gone, target untouched", () =>
        {
            var target = Dir(scratch, "root_junction_target");
            var keep = Path.Combine(target, "keep.txt");
            Touch(keep);
            var link = Path.Combine(scratch, "root_junction");
            Junction(link, target);
            var res = FastDeleter.Delete([link]);
            Assert(res.Success, Why(res));
            Assert(!Directory.Exists(link), "link remains");
            Assert(File.Exists(keep), "DELETED THROUGH THE JUNCTION");
            Directory.Delete(target, true);
        });

        Test("symlinks (skipped without the privilege): targets untouched", () =>
        {
            var targetDir = Dir(scratch, "sym_target");
            var keep = Path.Combine(targetDir, "keep.txt");
            Touch(keep);
            var r = Dir(scratch, "with_symlinks");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(r, "dirlink"), targetDir);
                File.CreateSymbolicLink(Path.Combine(r, "filelink"), keep);
            }
            catch (IOException)
            {
                Console.WriteLine("       (no symlink privilege, junction tests still cover link safety)");
            }
            var res = FastDeleter.Delete([r]);
            Assert(res.Success, Why(res));
            Assert(!Directory.Exists(r), "tree remains");
            Assert(File.Exists(keep), "DELETED THROUGH THE SYMLINK");
            Directory.Delete(targetDir, true);
        });

        Test("hard link: other name keeps the data", () =>
        {
            var a = Dir(scratch, "hard_keep");
            var keep = Path.Combine(a, "keep.txt");
            File.WriteAllText(keep, "data");
            var r = Dir(scratch, "hard_del");
            Run("cmd.exe", $"/c mklink /H \"{Path.Combine(r, "link.txt")}\" \"{keep}\"");
            var res = FastDeleter.Delete([r]);
            Assert(res.Success, Why(res));
            Assert(File.ReadAllText(keep) == "data", "data lost");
            Directory.Delete(a, true);
        });

        Test("LOCKED file: reported, no crash, other files go, parents stay", () =>
        {
            var r = Dir(scratch, "locked");
            var sub = Dir(r, "sub");
            var locked = Path.Combine(sub, "locked.bin");
            var other = Path.Combine(sub, "other.bin");
            var sibling = Dir(r, "sibling");
            Touch(other);
            Touch(Path.Combine(sibling, "s.bin"));
            using (new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                var res = FastDeleter.Delete([r]);
                Assert(!res.Success && res.FailureCount == 1, $"failures={res.FailureCount}");
                Assert(res.Failures[0].Path.EndsWith("locked.bin"), res.Failures[0].Path);
                Assert(File.Exists(locked), "locked file vanished");
                Assert(!File.Exists(other), "unlocked neighbour remained");
                Assert(!Directory.Exists(sibling), "sibling tree remained");
            }
            var again = FastDeleter.Delete([r]);
            Assert(again.Success && !Directory.Exists(r), "second run after unlock failed");
        });

        Test("file open with share-delete disappears anyway", () =>
        {
            var r = Dir(scratch, "shared");
            var f = Path.Combine(r, "open.bin");
            using var s = new FileStream(f, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            var res = FastDeleter.Delete([r]);
            Assert(res.Success, Why(res));
            Assert(!Directory.Exists(r), "folder remains");
        });

        Test("read-only and hidden/system files and folders", () =>
        {
            var r = Dir(scratch, "attrs");
            var ro = Path.Combine(r, "ro.txt");
            var hs = Path.Combine(r, "hs.txt");
            Touch(ro);
            Touch(hs);
            File.SetAttributes(ro, FileAttributes.ReadOnly);
            File.SetAttributes(hs, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
            var roDir = Dir(r, "rodir");
            Touch(Path.Combine(roDir, "x.txt"));
            File.SetAttributes(roDir, FileAttributes.ReadOnly | FileAttributes.Directory);
            var res = FastDeleter.Delete([r]);
            Assert(res.Success, Why(res));
            Assert(!Directory.Exists(r), "tree remains");
        });

        Test("paths over 260 chars, 300 levels deep", () =>
        {
            var r = Dir(scratch, "deep");
            var cur = r;
            for (var i = 0; i < 300; i++) cur += "\\" + new string('d', 20);
            Directory.CreateDirectory(@"\\?\" + cur);
            File.WriteAllText(@"\\?\" + cur + @"\leaf.txt", "x");
            var res = FastDeleter.Delete([r]);
            Assert(res.Success, Why(res));
            Assert(!Directory.Exists(r), "tree remains");
        });

        Test("names Win32 mangles: unicode, trailing dot and space", () =>
        {
            var r = Dir(scratch, "names");
            var ex = @"\\?\" + r;
            File.WriteAllText(ex + @"\日本語 ✓ file.txt", "x");
            File.WriteAllText(ex + @"\trailingdot.", "x");
            File.WriteAllText(ex + @"\trailingspace ", "x");
            Directory.CreateDirectory(ex + @"\dotdir.");
            File.WriteAllText(ex + @"\dotdir.\inner.txt", "x");
            var res = FastDeleter.Delete([r]);
            Assert(res.Success, Why(res));
            Assert(!Directory.Exists(r), "tree remains");
        });

        Test("single file, empty folder, missing path, overlap, trailing slash", () =>
        {
            var f = Path.Combine(scratch, "single.txt");
            Touch(f);
            Assert(FastDeleter.Delete([f]).Files == 1 && !File.Exists(f), "single file");

            var e = Dir(scratch, "empty");
            var res = FastDeleter.Delete([e + "\\"]);
            Assert(res.Success && res.Directories == 1 && !Directory.Exists(e), "empty folder");

            Assert(FastDeleter.Delete([Path.Combine(scratch, "nope")]).Success, "missing path should be fine");

            var o = Dir(scratch, "overlap");
            var inner = Dir(o, "inner");
            Touch(Path.Combine(inner, "a.txt"));
            res = FastDeleter.Delete([inner, o, o, inner]);
            Assert(res.Success && !Directory.Exists(o), "overlap: " + Why(res));
        });

        Test("many folders at once share the pool", () =>
        {
            var list = new List<string>();
            for (var i = 0; i < 20; i++)
            {
                var d = Dir(scratch, $"multi{i}", "sub");
                Touch(Path.Combine(d, "f.txt"));
                list.Add(Path.Combine(scratch, $"multi{i}"));
            }
            var res = FastDeleter.Delete(list);
            Assert(res.Success && res.Files == 20, Why(res));
            Assert(list.All(p => !Directory.Exists(p)), "some remain");
        });

        // Only the guard function is called directly here, so a guard bug can't delete anything real.
        Test("guard refuses roots, Windows, profile and their parents", () =>
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var refused = new[]
            {
                @"C:\", @"E:\", @"\\?\C:\", @"\\server\share",
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                profile,
                Path.GetDirectoryName(profile)!,
                Path.Combine(profile, "Documents"),
                Path.Combine(profile, "Downloads"),
            };
            foreach (var p in refused)
                Assert(PathGuard.Check(p) != null, $"should refuse {p}");

            var allowed = new[] { @"E:\Project_PH\Saved", @"D:\Junk", Path.Combine(profile, "Downloads", "junk"), scratch };
            foreach (var p in allowed)
                Assert(PathGuard.Check(p) == null, $"should allow {p}: {PathGuard.Check(p)}");
        });

        Console.WriteLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        return _failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action body)
    {
        try
        {
            body();
            Console.WriteLine($"PASS   {name}");
        }
        catch (Exception ex)
        {
            _failed++;
            Console.WriteLine($"FAIL   {name}\n       {ex.Message}");
        }
    }

    private static void Assert(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    private static string Why(DeleteResult r) =>
        r.Success ? "ok" : $"{r.FailureCount} failures, first: {r.Failures[0].Path}: {r.Failures[0].Reason}";

    private static string Dir(params string[] parts) => Directory.CreateDirectory(Path.Combine(parts)).FullName;
    private static void Touch(string path) => File.WriteAllText(path, "x");
    private static void Junction(string link, string target) => Run("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"");

    private static void Run(string exe, string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception($"{exe} {arguments} exited {p.ExitCode}");
    }
}

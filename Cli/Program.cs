using DelFast.Core;

var targets = new List<string>();
var assumeYes = false;
var threads = 0;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-y" or "--yes":
            assumeYes = true;
            break;
        case "-t" or "--threads" when i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0:
            threads = n;
            i++;
            break;
        case "-h" or "--help" or "/?":
            PrintUsage();
            return 0;
        default:
            if (args[i].StartsWith('-'))
            {
                Console.Error.WriteLine($"Unknown option: {args[i]}");
                PrintUsage();
                return 2;
            }
            targets.Add(args[i]);
            break;
    }
}

if (targets.Count == 0)
{
    PrintUsage();
    return 2;
}

if (!assumeYes)
{
    Console.WriteLine("Permanently delete (no Recycle Bin):");
    foreach (var t in targets) Console.WriteLine($"  {Path.GetFullPath(t)}");
    Console.Write("Type y to continue: ");
    if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Cancelled.");
        return 1;
    }
}

var result = FastDeleter.Delete(targets, threads);

Console.WriteLine($"Deleted {result.Files:N0} files and {result.Directories:N0} folders, " +
                  $"{result.BytesFreed / (1024.0 * 1024 * 1024):N2} GB, in {result.Elapsed.TotalSeconds:N2} s " +
                  $"({result.Threads} threads)");

if (result.Success) return 0;

Console.Error.WriteLine($"{result.FailureCount:N0} items could not be deleted:");
foreach (var f in result.Failures.Take(20)) Console.Error.WriteLine($"  {f.Path}\n    {f.Reason}");
if (result.FailureCount > 20) Console.Error.WriteLine($"  ...and {result.FailureCount - 20:N0} more");
return 1;

static void PrintUsage() => Console.WriteLine("""
    dfast: fast parallel permanent delete

    Usage: dfast <path> [path...] [-y] [-t threads]
      -y, --yes        don't ask for confirmation
      -t, --threads N  worker threads (default: min(CPU count, 8))

    Junctions and symlinks are removed as links; their targets are left alone.
    """);

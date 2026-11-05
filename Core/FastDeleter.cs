using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DelFast.Core;

public sealed record DeleteFailure(string Path, string Reason);

public sealed class DeleteResult
{
    public long Files { get; internal set; }
    public long Directories { get; internal set; }
    public long BytesFreed { get; internal set; }
    public long FailureCount { get; internal set; }
    public int Threads { get; internal set; }
    public TimeSpan Elapsed { get; internal set; }

    /// <summary>First failures only (capped); FailureCount has the real total.</summary>
    public IReadOnlyList<DeleteFailure> Failures { get; internal set; } = [];
    public bool Success => FailureCount == 0;
}

/// <summary>
/// Parallel recursive delete on the native NT API.
///
/// Why it is fast: every open is relative to the already-open parent directory handle, so the
/// kernel never re-walks the full path, and POSIX delete semantics make a name disappear at once
/// so a parent is empty as soon as its children are done.
///
/// Why it is safe: reparse points (junctions, symlinks) are opened with FILE_OPEN_REPARSE_POINT
/// and removed as links; they are never entered, so a link can't lead the delete out of the tree.
/// Handle-relative opens also mean a directory swapped for a link mid-delete can't redirect us.
/// </summary>
public sealed unsafe class FastDeleter
{
    private const int BufferBytes = 64 * 1024;
    private const int MaxRecordedFailures = 1000;
    private const int MaxRescans = 3;

    private readonly Stack<WorkItem> _work = new();
    private readonly ConcurrentQueue<DeleteFailure> _failures = new();
    private int _idle;
    private int _rootsLeft;
    private long _files, _dirs, _bytes, _failureCount;

    private sealed class Dir(Dir? parent, string name)
    {
        public readonly Dir? Parent = parent;
        public readonly string Name = name;
        public nint Handle;
        public int Pending = 1; // the scan itself; plus one per queued file batch / child dir
        public int Rescans;
        public volatile bool Failed;

        public string FullPath()
        {
            if (Parent == null) return Name;
            var parts = new Stack<string>();
            for (var d = this; d != null; d = d.Parent) parts.Push(d.Name);
            return string.Join('\\', parts);
        }
    }

    private readonly record struct WorkItem(Dir Dir, byte[]? Batch);

    /// <summary>Default worker count. Deletes hit NTFS metadata locks, so more is not better.</summary>
    public static int DefaultThreads => Math.Clamp(Environment.ProcessorCount, 1, 8);

    /// <summary>Permanently deletes the given files and folders. Refused paths are reported as failures.</summary>
    public static DeleteResult Delete(IEnumerable<string> paths, int threads = 0)
    {
        var deleter = new FastDeleter();
        return deleter.Run(paths, threads > 0 ? threads : DefaultThreads);
    }

    private DeleteResult Run(IEnumerable<string> paths, int threads)
    {
        var sw = Stopwatch.StartNew();

        foreach (var path in Normalize(paths))
        {
            var refusal = PathGuard.Check(path);
            if (refusal != null)
            {
                Fail(path, refusal);
                continue;
            }

            FileAttributes attrs;
            try { attrs = File.GetAttributes(path); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                continue; // already gone is the outcome the caller wanted
            }
            catch (Exception ex)
            {
                Fail(path, ex.Message);
                continue;
            }

            var isRealDir = attrs.HasFlag(FileAttributes.Directory) && !attrs.HasFlag(FileAttributes.ReparsePoint);
            if (isRealDir)
            {
                _rootsLeft++;
                _work.Push(new WorkItem(new Dir(null, path), null));
            }
            else
            {
                DeleteRootEntry(path, attrs);
            }
        }

        if (_rootsLeft > 0)
        {
            var workers = new Thread[threads];
            for (var i = 0; i < threads; i++)
            {
                workers[i] = new Thread(Worker, 512 * 1024) { IsBackground = true, Name = "DelFast worker" };
                workers[i].Start();
            }
            foreach (var w in workers) w.Join();
        }

        sw.Stop();
        return new DeleteResult
        {
            Files = _files,
            Directories = _dirs,
            BytesFreed = _bytes,
            FailureCount = _failureCount,
            Failures = _failures.ToArray(),
            Threads = threads,
            Elapsed = sw.Elapsed,
        };
    }

    /// <summary>Full paths, duplicates removed, and anything inside another listed folder dropped.</summary>
    private static List<string> Normalize(IEnumerable<string> paths)
    {
        var kept = new List<string>();
        foreach (var full in paths
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Select(TrimmedFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(p => p.Length))
        {
            var nested = kept.Any(k => full.StartsWith(k + "\\", StringComparison.OrdinalIgnoreCase));
            if (!nested) kept.Add(full);
        }
        return kept;
    }

    private static string TrimmedFullPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
    }

    private static string ToNtPath(string full)
    {
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
            return @"\??\" + full[4..];
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\??\UNC\" + full[2..];
        return @"\??\" + full;
    }

    private void Worker()
    {
        while (true)
        {
            WorkItem item;
            lock (_work)
            {
                while (_work.Count == 0)
                {
                    if (Volatile.Read(ref _rootsLeft) == 0) return;
                    _idle++;
                    Monitor.Wait(_work);
                    _idle--;
                }
                item = _work.Pop(); // LIFO keeps the walk depth-first, which bounds open handles
            }

            if (item.Batch == null) Scan(item.Dir);
            else DeleteBatch(item.Dir, item.Batch);

            Release(item.Dir);
        }
    }

    private void Push(WorkItem item)
    {
        lock (_work)
        {
            _work.Push(item);
            if (_idle > 0) Monitor.Pulse(_work);
        }
    }

    /// <summary>Drops one reference; finished directories are removed and release their parent (iterative for deep trees).</summary>
    private void Release(Dir? dir)
    {
        while (dir != null)
        {
            if (Interlocked.Decrement(ref dir.Pending) != 0) return;
            if (!TryFinish(dir)) return; // requeued for a rescan

            if (dir.Parent == null)
            {
                if (Interlocked.Decrement(ref _rootsLeft) == 0)
                    lock (_work) Monitor.PulseAll(_work);
                return;
            }

            if (dir.Failed) dir.Parent.Failed = true;
            dir = dir.Parent;
        }
    }

    private void Scan(Dir dir)
    {
        if (dir.Handle == 0)
        {
            var status = OpenDirectory(dir, out dir.Handle);
            if (status != Nt.STATUS_SUCCESS)
            {
                dir.Handle = 0;
                if (!Nt.IsGone(status))
                {
                    dir.Failed = true;
                    Fail(dir.FullPath(), Nt.Describe(status));
                }
                return;
            }
        }

        var restart = dir.Rescans > 0;
        while (true)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);
            var pushedBuffer = false;
            try
            {
                Nt.IO_STATUS_BLOCK iosb;
                uint status;
                fixed (byte* p = buffer)
                {
                    status = Nt.NtQueryDirectoryFile(dir.Handle, 0, 0, 0, &iosb, p, BufferBytes,
                        Nt.FileDirectoryInformation, 0, null, restart ? (byte)1 : (byte)0);
                }
                restart = false;

                if (status is Nt.STATUS_NO_MORE_FILES or Nt.STATUS_NO_SUCH_FILE) return;
                if (status != Nt.STATUS_SUCCESS)
                {
                    dir.Failed = true;
                    Fail(dir.FullPath(), Nt.Describe(status));
                    return;
                }

                var hasFiles = false;
                fixed (byte* p = buffer)
                {
                    for (var e = p; ; e += Nt.Next(e))
                    {
                        var attrs = Nt.Attributes(e);
                        var nameLen = Nt.NameBytes(e);
                        if (!Nt.IsDots(e, nameLen))
                        {
                            if (Nt.IsRealDirectory(attrs))
                            {
                                Interlocked.Increment(ref dir.Pending);
                                Push(new WorkItem(new Dir(dir, new string(Nt.Name(e), 0, (int)(nameLen / 2))), null));
                            }
                            else
                            {
                                hasFiles = true;
                            }
                        }
                        if (Nt.Next(e) == 0) break;
                    }
                }

                if (hasFiles)
                {
                    Interlocked.Increment(ref dir.Pending);
                    pushedBuffer = true;
                    Push(new WorkItem(dir, buffer));
                }
            }
            finally
            {
                if (!pushedBuffer) ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private void DeleteBatch(Dir dir, byte[] batch)
    {
        long files = 0, bytes = 0;
        fixed (byte* p = batch)
        {
            for (var e = p; ; e += Nt.Next(e))
            {
                var attrs = Nt.Attributes(e);
                var nameLen = Nt.NameBytes(e);
                if (!Nt.IsDots(e, nameLen) && !Nt.IsRealDirectory(attrs))
                {
                    var status = DeleteRelative(dir.Handle, Nt.Name(e), (ushort)nameLen, attrs);
                    if (status == Nt.STATUS_SUCCESS)
                    {
                        files++;
                        bytes += Nt.AllocationSize(e);
                    }
                    else if (!Nt.IsGone(status))
                    {
                        dir.Failed = true;
                        Fail(dir.FullPath() + "\\" + new string(Nt.Name(e), 0, (int)(nameLen / 2)), Nt.Describe(status));
                    }
                }
                if (Nt.Next(e) == 0) break;
            }
        }
        ArrayPool<byte>.Shared.Return(batch);
        Interlocked.Add(ref _files, files);
        Interlocked.Add(ref _bytes, bytes);
    }

    /// <summary>Removes an emptied directory. Returns false if it was requeued because new entries were found.</summary>
    private bool TryFinish(Dir dir)
    {
        if (dir.Handle == 0) return true; // vanished, or the open failed and was already reported

        if (dir.Failed)
        {
            Nt.NtClose(dir.Handle);
            return true;
        }

        var status = Nt.MarkForDelete(dir.Handle);
        // Non-POSIX volumes, or another process holding a child open, can lag for a moment.
        for (var i = 0; status == Nt.STATUS_DIRECTORY_NOT_EMPTY && i < 3; i++)
        {
            Thread.Sleep(1 << (i * 2));
            status = Nt.MarkForDelete(dir.Handle);
        }

        if (status == Nt.STATUS_DIRECTORY_NOT_EMPTY && dir.Rescans < MaxRescans)
        {
            // Something appeared after we listed it. Sweep again rather than failing the whole tree.
            dir.Rescans++;
            dir.Pending = 1;
            Push(new WorkItem(dir, null));
            return false;
        }

        Nt.NtClose(dir.Handle);
        dir.Handle = 0;

        if (status == Nt.STATUS_SUCCESS)
        {
            Interlocked.Increment(ref _dirs);
        }
        else if (!Nt.IsGone(status))
        {
            dir.Failed = true;
            Fail(dir.FullPath(), Nt.Describe(status));
        }
        return true;
    }

    private static uint OpenDirectory(Dir dir, out nint handle)
    {
        var name = dir.Parent == null ? ToNtPath(dir.Name) : dir.Name;
        fixed (char* n = name)
        {
            return Nt.Open(dir.Parent?.Handle ?? 0, n, (ushort)(name.Length * 2),
                Nt.FILE_LIST_DIRECTORY | Nt.DELETE | Nt.SYNCHRONIZE,
                Nt.FILE_DIRECTORY_FILE | Nt.FILE_SYNCHRONOUS_IO_NONALERT | Nt.FILE_OPEN_REPARSE_POINT | Nt.FILE_OPEN_FOR_BACKUP_INTENT,
                out handle);
        }
    }

    private static uint DeleteRelative(nint parent, char* name, ushort nameBytes, uint attrs)
    {
        uint status = 0;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0) Thread.Sleep(5 << (attempt * 2)); // 20, 80, 320 ms: AV and indexers let go quickly

            status = Nt.Open(parent, name, nameBytes, Nt.DELETE | Nt.SYNCHRONIZE,
                Nt.FILE_SYNCHRONOUS_IO_NONALERT | Nt.FILE_OPEN_REPARSE_POINT | Nt.FILE_OPEN_FOR_BACKUP_INTENT,
                out var handle);

            if (status == Nt.STATUS_SUCCESS)
            {
                status = Nt.MarkForDelete(handle);
                Nt.NtClose(handle);
            }

            // Only volumes without the extended delete (FAT, some network shares) land here.
            if (status == Nt.STATUS_CANNOT_DELETE && (attrs & Nt.ATTR_READONLY) != 0)
                status = ClearReadOnlyAndDelete(parent, name, nameBytes);

            if (status != Nt.STATUS_SHARING_VIOLATION) return status;
        }
        return status;
    }

    private static uint ClearReadOnlyAndDelete(nint parent, char* name, ushort nameBytes)
    {
        var status = Nt.Open(parent, name, nameBytes, Nt.DELETE | Nt.SYNCHRONIZE | Nt.FILE_WRITE_ATTRIBUTES,
            Nt.FILE_SYNCHRONOUS_IO_NONALERT | Nt.FILE_OPEN_REPARSE_POINT | Nt.FILE_OPEN_FOR_BACKUP_INTENT,
            out var handle);
        if (status != Nt.STATUS_SUCCESS) return status;

        status = Nt.SetNormalAttributes(handle);
        if (status == Nt.STATUS_SUCCESS) status = Nt.MarkForDelete(handle);
        Nt.NtClose(handle);
        return status;
    }

    /// <summary>A listed path that is a file or a link: remove exactly that entry.</summary>
    private void DeleteRootEntry(string path, FileAttributes attrs)
    {
        var name = ToNtPath(path);
        long size = 0;
        if (!attrs.HasFlag(FileAttributes.Directory) && !attrs.HasFlag(FileAttributes.ReparsePoint))
        {
            try { size = new FileInfo(path).Length; } catch (IOException) { }
        }

        uint status;
        fixed (char* n = name)
            status = DeleteRelative(0, n, (ushort)(name.Length * 2), (uint)attrs);

        if (status == Nt.STATUS_SUCCESS)
        {
            _files++;
            _bytes += size;
        }
        else if (!Nt.IsGone(status))
        {
            Fail(path, Nt.Describe(status));
        }
    }

    private void Fail(string path, string reason)
    {
        if (Interlocked.Increment(ref _failureCount) <= MaxRecordedFailures)
            _failures.Enqueue(new DeleteFailure(path, reason));
    }

    /// <summary>Thin ntdll layer. Layouts match FILE_DIRECTORY_INFORMATION and friends in the Windows DDK.</summary>
    private static class Nt
    {
        public const uint STATUS_SUCCESS = 0;
        public const uint STATUS_NO_MORE_FILES = 0x80000006;
        public const uint STATUS_INVALID_INFO_CLASS = 0xC0000003;
        public const uint STATUS_INVALID_PARAMETER = 0xC000000D;
        public const uint STATUS_NO_SUCH_FILE = 0xC000000F;
        public const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;
        public const uint STATUS_OBJECT_PATH_NOT_FOUND = 0xC000003A;
        public const uint STATUS_SHARING_VIOLATION = 0xC0000043;
        public const uint STATUS_DELETE_PENDING = 0xC0000056;
        public const uint STATUS_NOT_SUPPORTED = 0xC00000BB;
        public const uint STATUS_DIRECTORY_NOT_EMPTY = 0xC0000101;
        public const uint STATUS_CANNOT_DELETE = 0xC0000121;

        public const uint DELETE = 0x00010000;
        public const uint SYNCHRONIZE = 0x00100000;
        public const uint FILE_LIST_DIRECTORY = 0x0001;
        public const uint FILE_WRITE_ATTRIBUTES = 0x0100;
        public const uint FILE_SHARE_ALL = 0x7;

        public const uint FILE_DIRECTORY_FILE = 0x00000001;
        public const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020;
        public const uint FILE_OPEN_FOR_BACKUP_INTENT = 0x00004000;
        public const uint FILE_OPEN_REPARSE_POINT = 0x00200000;

        public const uint OBJ_CASE_INSENSITIVE = 0x40;
        public const uint ATTR_READONLY = 0x01;
        public const uint ATTR_DIRECTORY = 0x10;
        public const uint ATTR_NORMAL = 0x80;
        public const uint ATTR_REPARSE = 0x400;

        public const int FileDirectoryInformation = 1;
        public const int FileBasicInformation = 4;
        public const int FileDispositionInformation = 13;
        public const int FileDispositionInformationEx = 64;

        private const uint DISPOSITION_DELETE = 0x1;
        private const uint DISPOSITION_POSIX = 0x2;
        private const uint DISPOSITION_IGNORE_READONLY = 0x10;

        [StructLayout(LayoutKind.Sequential)]
        public struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public char* Buffer;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct OBJECT_ATTRIBUTES
        {
            public uint Length;
            public nint RootDirectory;
            public UNICODE_STRING* ObjectName;
            public uint Attributes;
            public void* SecurityDescriptor;
            public void* SecurityQualityOfService;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_STATUS_BLOCK
        {
            public nint Status;
            public nuint Information;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_BASIC_INFORMATION
        {
            public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
            public uint FileAttributes;
        }

        [DllImport("ntdll.dll", ExactSpelling = true)]
        private static extern uint NtOpenFile(nint* handle, uint access, OBJECT_ATTRIBUTES* attributes,
            IO_STATUS_BLOCK* iosb, uint shareAccess, uint openOptions);

        [DllImport("ntdll.dll", ExactSpelling = true)]
        public static extern uint NtQueryDirectoryFile(nint handle, nint evt, nint apc, nint apcContext,
            IO_STATUS_BLOCK* iosb, void* buffer, uint length, int infoClass, byte returnSingleEntry,
            UNICODE_STRING* fileName, byte restartScan);

        [DllImport("ntdll.dll", ExactSpelling = true)]
        private static extern uint NtSetInformationFile(nint handle, IO_STATUS_BLOCK* iosb, void* info,
            uint length, int infoClass);

        [DllImport("ntdll.dll", ExactSpelling = true)]
        public static extern uint NtClose(nint handle);

        [DllImport("ntdll.dll", ExactSpelling = true)]
        private static extern uint RtlNtStatusToDosError(uint status);

        public static uint Open(nint root, char* name, ushort nameBytes, uint access, uint options, out nint handle)
        {
            var us = new UNICODE_STRING { Length = nameBytes, MaximumLength = nameBytes, Buffer = name };
            var oa = new OBJECT_ATTRIBUTES
            {
                Length = (uint)sizeof(OBJECT_ATTRIBUTES),
                RootDirectory = root,
                ObjectName = &us,
                Attributes = OBJ_CASE_INSENSITIVE,
            };
            IO_STATUS_BLOCK iosb;
            nint h = 0;
            var status = NtOpenFile(&h, access, &oa, &iosb, FILE_SHARE_ALL, options);
            handle = h;
            return status;
        }

        public static uint MarkForDelete(nint handle)
        {
            IO_STATUS_BLOCK iosb;
            var flags = DISPOSITION_DELETE | DISPOSITION_POSIX | DISPOSITION_IGNORE_READONLY;
            var status = NtSetInformationFile(handle, &iosb, &flags, sizeof(uint), FileDispositionInformationEx);
            if (status is STATUS_INVALID_INFO_CLASS or STATUS_INVALID_PARAMETER or STATUS_NOT_SUPPORTED)
            {
                byte legacy = 1;
                status = NtSetInformationFile(handle, &iosb, &legacy, sizeof(byte), FileDispositionInformation);
            }
            return status;
        }

        public static uint SetNormalAttributes(nint handle)
        {
            IO_STATUS_BLOCK iosb;
            var info = new FILE_BASIC_INFORMATION { FileAttributes = ATTR_NORMAL };
            return NtSetInformationFile(handle, &iosb, &info, (uint)sizeof(FILE_BASIC_INFORMATION), FileBasicInformation);
        }

        // FILE_DIRECTORY_INFORMATION field offsets.
        public static uint Next(byte* e) => *(uint*)e;
        public static long AllocationSize(byte* e) => *(long*)(e + 48);
        public static uint Attributes(byte* e) => *(uint*)(e + 56);
        public static uint NameBytes(byte* e) => *(uint*)(e + 60);
        public static char* Name(byte* e) => (char*)(e + 64);

        public static bool IsRealDirectory(uint attrs) => (attrs & ATTR_DIRECTORY) != 0 && (attrs & ATTR_REPARSE) == 0;

        public static bool IsDots(byte* e, uint nameBytes)
        {
            var n = Name(e);
            return (nameBytes == 2 && n[0] == '.') || (nameBytes == 4 && n[0] == '.' && n[1] == '.');
        }

        public static bool IsGone(uint status) =>
            status is STATUS_OBJECT_NAME_NOT_FOUND or STATUS_OBJECT_PATH_NOT_FOUND or STATUS_DELETE_PENDING;

        public static string Describe(uint status) =>
            $"{new Win32Exception((int)RtlNtStatusToDosError(status)).Message} (0x{status:X8})";
    }
}

/// <summary>Stops the delete from ever taking out a drive, Windows, or a user's profile.</summary>
public static class PathGuard
{
    /// <summary>Returns why the full path must not be deleted, or null if it is allowed.</summary>
    public static string? Check(string fullPath)
    {
        var path = StripPrefix(fullPath).TrimEnd('\\', '/');
        var root = StripPrefix(Path.GetPathRoot(fullPath) ?? "").TrimEnd('\\', '/');
        if (path.Length == 0 || string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            return "Refused: this is the root of a drive or share.";

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (IsSameOrInside(path, windows))
            return "Refused: this is inside the Windows folder.";

        foreach (var folder in ProtectedFolders())
        {
            if (string.IsNullOrEmpty(folder)) continue;
            if (string.Equals(path, folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return $"Refused: {folder} is a protected system or profile folder.";
            if (IsSameOrInside(folder, path))
                return $"Refused: this contains the protected folder {folder}.";
        }
        return null;
    }

    private static IEnumerable<string> ProtectedFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    private static string StripPrefix(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;

    private static bool IsSameOrInside(string path, string folder)
    {
        if (string.IsNullOrEmpty(folder)) return false;
        var f = folder.TrimEnd('\\');
        return string.Equals(path, f, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase);
    }
}

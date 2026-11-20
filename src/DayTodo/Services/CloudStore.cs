using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace DayTodo.Services;

/// <summary>
/// Metadata for one object in a cloud store. Sha256 is lowercase hex, matching
/// the Sha256 in manifest.json: a reader can diff the PC-owned index against the
/// bucket and download only the days it does not have yet.
/// </summary>
public sealed record CloudFile(
    string Name,
    long Length,
    string? Sha256 = null,
    DateTimeOffset? LastModifiedUtc = null);

/// <summary>
/// The cloud layer is deliberately small: a flat bucket of named byte blobs with
/// list / get / put. DayTodo's per-day files and manifest.json are opaque to it,
/// so switching cloud providers never touches DataStore, the window or the MCP tools.
///
/// Contract notes:
///   * Names are flat file names (no folders, no path separators).
///   * GetAsync returns null when the object does not exist.
///   * PutAsync is an atomic whole-file replace and returns false when the bytes
///     are unchanged (no write at all) - sync clients then see no spurious version.
///   * There is no Delete. Empty day files are kept instead of removed, because
///     deletes are much harder for sync clients to reason about.
///   * Async + CancellationToken because real adapters (Weiyun MCP, WebDAV, COS)
///     are network calls. LocalFolderCloudStore completes synchronously.
///   * No locks, ETags or optimistic concurrency: the PC is the only writer in
///     this design, and other devices are readers.
///
/// This is an abstraction seam, not a sync feature. DataStore does not call it;
/// unless DAYTODO_CLOUD_DIR is set, Current is an inert NullCloudStore and the
/// program makes no external request and writes nothing outside its data dir.
/// </summary>
public interface ICloudStore
{
    /// <summary>"none", "local-folder", or a future adapter id.</summary>
    string Provider { get; }

    /// <summary>Whether this instance does any real I/O at all.</summary>
    bool IsEnabled { get; }

    Task<IReadOnlyList<CloudFile>> ListAsync(CancellationToken ct = default);

    Task<byte[]?> GetAsync(string name, CancellationToken ct = default);

    /// <summary>Returns true only when the file was actually written.</summary>
    Task<bool> PutAsync(string name, byte[] content, CancellationToken ct = default);
}

/// <summary>
/// Default. Never touches disk or network. Put throws instead of silently
/// dropping data, so a misconfigured caller fails loudly.
/// </summary>
public sealed class NullCloudStore : ICloudStore
{
    public static readonly NullCloudStore Instance = new();

    public string Provider => "none";

    public bool IsEnabled => false;

    public Task<IReadOnlyList<CloudFile>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CloudFile>>(Array.Empty<CloudFile>());

    public Task<byte[]?> GetAsync(string name, CancellationToken ct = default) =>
        Task.FromResult<byte[]?>(null);

    public Task<bool> PutAsync(string name, byte[] content, CancellationToken ct = default) =>
        Task.FromException<bool>(new NotSupportedException(
            "CloudStore 未启用：请设置 DAYTODO_CLOUD_DIR 或注入其他 ICloudStore 实现。"));
}

/// <summary>
/// Reference implementation: treat one local directory as the bucket. Point it at
/// a folder already managed by a sync assistant (e.g. Weiyun) and the same three
/// calls work without any cloud API code. Uses the same dirty-check + atomic
/// replace as DataStore so a sync client never sees a half-written file.
/// </summary>
public sealed class LocalFolderCloudStore : ICloudStore
{
    public string Root { get; }

    public string Provider => "local-folder";

    public bool IsEnabled => true;

    public LocalFolderCloudStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("云端目录不能为空", nameof(root));
        Root = Path.GetFullPath(root.Trim());
    }

    /// <summary>Flat file names only; blocks traversal and any cloud-unsafe characters.</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128) return false;
        if (name == "." || name == ".." || name.Contains("..")) return false;
        foreach (var c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-') return false;
        return true;
    }

    private string Resolve(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException("非法文件名：" + name, nameof(name));

        var full = Path.GetFullPath(Path.Combine(Root, name));
        var prefix = Root.EndsWith(Path.DirectorySeparatorChar)
            ? Root
            : Root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("文件名越出云端目录", nameof(name));
        return full;
    }

    public Task<IReadOnlyList<CloudFile>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<CloudFile>();
        if (Directory.Exists(Root))
        {
            foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.TopDirectoryOnly)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(path);
                // .tmp is the in-flight atomic write, not an object.
                if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var info = new FileInfo(path);
                    var bytes = File.ReadAllBytes(path);
                    result.Add(new CloudFile(
                        name,
                        info.Length,
                        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
                }
                catch (IOException) { /* a sync client may be holding this file */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        return Task.FromResult<IReadOnlyList<CloudFile>>(result);
    }

    public Task<byte[]?> GetAsync(string name, CancellationToken ct = default)
    {
        var path = Resolve(name);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<byte[]?>(File.Exists(path) ? File.ReadAllBytes(path) : null);
    }

    public Task<bool> PutAsync(string name, byte[] content, CancellationToken ct = default)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));
        var path = Resolve(name);
        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Root);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            return Task.FromResult(false);

        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, content);
        File.Move(tmp, path, true);
        return Task.FromResult(true);
    }
}

/// <summary>
/// The single seam where a real adapter will be plugged in. Kept as a provider
/// rather than a DI container on purpose: DayTodo has no third-party packages.
/// </summary>
public static class CloudStoreProvider
{
    public const string EnvVar = "DAYTODO_CLOUD_DIR";

    private static readonly Lazy<ICloudStore> LazyCurrent =
        new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Inert unless DAYTODO_CLOUD_DIR is set. Nothing in the app calls this yet.</summary>
    public static ICloudStore Current => LazyCurrent.Value;

    public static ICloudStore Create() => Create(Environment.GetEnvironmentVariable(EnvVar));

    public static ICloudStore Create(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return NullCloudStore.Instance;
        try
        {
            return new LocalFolderCloudStore(directory);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("DayTodo CloudStore 初始化失败：" + ex.Message);
            return NullCloudStore.Instance;
        }
    }
}

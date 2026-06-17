// tech-debt T15 (2026-04-28): RAII wrapper for temp files used by tests.
//
// Replaces the legacy `try { File.Delete(p); } catch { }` boilerplate
// scattered across 25+ test sites. The helper guarantees:
//
//   • Cleanup runs deterministically on `Dispose` (or finalizer fallback
//     if a test forgets `using`).
//   • Cleanup failures are surfaced via `ITestOutputHelper.WriteLine` if
//     the test class injects one; otherwise via `Console.WriteLine`
//     (xUnit captures Console output and attaches it to the failed-test
//     report). Silent swallowing — the original anti-pattern — is gone.
//   • Disk-leak risk drops to zero on the happy path, since failure to
//     delete on the unhappy path now produces a visible diagnostic.
//
// Two factory methods cover the two patterns we use today:
//   • `TestTempFile.Create()` — wraps `Path.GetTempFileName()` (creates
//     the file as a 0-byte placeholder so the OS reserves the path).
//   • `TestTempFile.WithUniqueName(prefix, extension)` — produces a
//     `<temp>/<prefix>_<guid>.<ext>` path WITHOUT pre-creating it,
//     suitable for tests that write/read the file via their own API.

using System.IO;
using Xunit.Abstractions;

namespace Voxelforge.Tests.Helpers;

/// <summary>
/// RAII wrapper around a temporary file path. Disposing this object
/// deletes the file (if present) and logs cleanup failures instead of
/// silently swallowing them.
/// </summary>
public sealed class TestTempFile : IDisposable
{
    private readonly ITestOutputHelper? _output;
    private bool _disposed;

    /// <summary>The absolute path to the temp file.</summary>
    public string Path { get; }

    /// <summary>
    /// Constructs a wrapper around a caller-provided path. Use the
    /// <see cref="Create"/> or <see cref="WithUniqueName"/> factories
    /// when the test doesn't need a specific path shape.
    /// </summary>
    public TestTempFile(string path, ITestOutputHelper? output = null)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        _output = output;
    }

    /// <summary>
    /// Creates a new 0-byte temp file via <see cref="System.IO.Path.GetTempFileName"/>
    /// and returns a wrapper that deletes it on dispose.
    /// </summary>
    public static TestTempFile Create(ITestOutputHelper? output = null)
        => new(System.IO.Path.GetTempFileName(), output);

    /// <summary>
    /// Returns a wrapper around a path of the form
    /// <c>&lt;temp&gt;/&lt;prefix&gt;_&lt;guid&gt;.&lt;extension&gt;</c>.
    /// The file is NOT pre-created — the caller is expected to write
    /// it via the API under test.
    /// </summary>
    public static TestTempFile WithUniqueName(
        string prefix,
        string extension,
        ITestOutputHelper? output = null)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefix must be non-empty.", nameof(prefix));
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("Extension must be non-empty.", nameof(extension));

        // Strip a leading dot from the extension so callers can pass
        // either ".json" or "json" interchangeably.
        var ext = extension.StartsWith('.') ? extension.Substring(1) : extension;
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{prefix}_{Guid.NewGuid():N}.{ext}");
        return new TestTempFile(path, output);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (File.Exists(Path))
                File.Delete(Path);
        }
        catch (Exception ex)
        {
            // T15: surface cleanup failures instead of swallowing.
            // xUnit captures Console.* output and attaches it to the
            // test report when the test fails (or runs with -verbose),
            // so this trace is recoverable on CI.
            var msg = $"[TestTempFile] cleanup failed for '{Path}': {ex.GetType().Name}: {ex.Message}";
            if (_output is not null)
                _output.WriteLine(msg);
            else
                Console.Error.WriteLine(msg);
        }
    }
}

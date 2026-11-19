using System.Buffers;
using System.IO;
using MediPassPlus.Application.Records;
using Microsoft.Extensions.Options;

namespace MediPassPlus.Infrastructure.Records;

/// <summary>
/// Stores report files on the local filesystem under <c>Reports:Storage:RootPath</c>, using a random name and an
/// extension taken from the validated media type (FR-REC-06, NFR-SEC-09, ADR-0015 decision 6).
/// <para>
/// Two properties matter more than speed here. First, the client's file name never reaches a path: the name is
/// <c>{Guid v7:N}{ext}</c>, so a traversal attempt has nothing to traverse. Second, the bytes are written to a
/// temporary file and moved into place only after they are complete and inside the size cap, so a refused or
/// interrupted upload never leaves a readable file behind.
/// </para>
/// </summary>
public sealed class LocalReportStorage : IReportStorage
{
    private const int CopyBufferSize = 81_920;
    private const string TemporaryFileSuffix = ".tmp";

    private readonly string _rootPath;

    /// <summary>Initializes a new instance of the <see cref="LocalReportStorage"/> class.</summary>
    /// <param name="options">The validated storage options; an empty root falls back to the temporary path.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public LocalReportStorage(IOptions<ReportStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _rootPath = ReportStoragePath.Resolve(options.Value.RootPath);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The size is counted while copying, not trusted from the caller: the reported length of an upload is a claim by
    /// the client. Exceeding <see cref="ReportFileValidator.MaxSizeBytes"/> or sending zero bytes removes the partial
    /// file and throws, so the caller can answer 413 without leaving an artefact.
    /// </remarks>
    public async Task<StoredReportFile> SaveAsync(
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Throws for a media type the validator never accepts; the extension is never taken from a client file name.
        var extension = ReportFileValidator.ExtensionFor(contentType);

        var directory = Directory.CreateDirectory(_rootPath).FullName;
        var storedFileName = Guid.CreateVersion7().ToString("N") + extension;
        var finalPath = Path.Combine(directory, storedFileName);
        var temporaryPath = finalPath + TemporaryFileSuffix;

        long written = 0;

        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);

            try
            {
                await using var target = new FileStream(temporaryPath, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                    BufferSize = CopyBufferSize
                });

                int read;

                while ((read = await content.ReadAsync(
                    buffer.AsMemory(0, CopyBufferSize),
                    cancellationToken)) > 0)
                {
                    written += read;

                    if (written > ReportFileValidator.MaxSizeBytes)
                    {
                        throw new InvalidOperationException(
                            "The uploaded file is larger than the 10 MB limit; nothing was stored.");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await target.FlushAsync(cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (written == 0)
            {
                throw new InvalidOperationException("The uploaded file is empty; nothing was stored.");
            }

            // Move only after the bytes are durable enough to read: a reader never sees a half-written file, and
            // FileMode.CreateNew on the temporary path means an existing name can never be overwritten.
            File.Move(temporaryPath, finalPath);

            return new StoredReportFile(storedFileName, written);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A name that is not a plain file name is refused instead of resolved, so a corrupted report row can never make
    /// this method delete a file outside the storage root.
    /// </remarks>
    public Task<bool> DeleteAsync(string storedFileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedFileName);

        if (!IsPlainFileName(storedFileName))
        {
            throw new ArgumentException(
                "A stored file name is a plain file name inside the storage root.",
                nameof(storedFileName));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var path = Path.Combine(_rootPath, storedFileName);

        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);

        return Task.FromResult(true);
    }

    private static bool IsPlainFileName(string name)
    {
        if (name is "." or "..")
        {
            return false;
        }

        // Both separators are checked explicitly so the rule is identical on Windows and Linux, where a backslash is
        // a legal file name character and would otherwise make this test platform-dependent.
        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort: the caller is already on a failure path, and the temporary name is never served.
        }
        catch (UnauthorizedAccessException)
        {
            // Same: a cleanup failure must not replace the original exception.
        }
    }
}

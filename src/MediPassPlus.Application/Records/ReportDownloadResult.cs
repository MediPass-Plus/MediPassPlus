using System.IO;

namespace MediPassPlus.Application.Records;

/// <summary>
/// The outcome of a report download: either a readable stream of the stored file, or a refusal reason. A result is
/// never both. The stored file name and storage path are never part of it (FR-PRV-05, ADR-0015).
/// </summary>
public sealed class ReportDownloadResult : IDisposable
{
    private bool _disposed;

    private ReportDownloadResult(Stream? content, string? contentType, ReportDownloadFailure failure)
    {
        Content = content;
        ContentType = contentType;
        Failure = failure;
    }

    /// <summary>The file content stream, or null when the download was refused. The caller owns it on success.</summary>
    public Stream? Content { get; private set; }

    /// <summary>The validated media type of the file, or null when the download was refused.</summary>
    public string? ContentType { get; }

    /// <summary>The refusal reason, or <see cref="ReportDownloadFailure.None"/> when the download succeeded.</summary>
    public ReportDownloadFailure Failure { get; }

    /// <summary>True when <see cref="Content"/> is available.</summary>
    public bool IsSuccess => Content is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="content">The readable file stream.</param>
    /// <param name="contentType">The validated media type.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="contentType"/> is null or whitespace.</exception>
    public static ReportDownloadResult Success(Stream content, string contentType)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        return new ReportDownloadResult(content, contentType, ReportDownloadFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="ReportDownloadFailure.None"/>.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="ReportDownloadFailure.None"/>.
    /// </exception>
    public static ReportDownloadResult Denied(ReportDownloadFailure failure)
    {
        if (failure == ReportDownloadFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new ReportDownloadResult(null, null, failure);
    }

    /// <summary>
    /// Detaches the content stream so the caller can return it in the HTTP response. After this call the result no
    /// longer owns the stream.
    /// </summary>
    /// <returns>The content stream.</returns>
    /// <exception cref="InvalidOperationException">The result is not successful.</exception>
    public Stream DetachContent()
    {
        if (Content is null)
        {
            throw new InvalidOperationException("Only a successful result carries content.");
        }

        var stream = Content;
        Content = null;
        return stream;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Content?.Dispose();
        Content = null;
        _disposed = true;
    }
}

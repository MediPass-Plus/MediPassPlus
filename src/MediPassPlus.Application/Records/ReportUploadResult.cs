namespace MediPassPlus.Application.Records;

/// <summary>
/// The outcome of a report upload: either the stored report's safe metadata, or the refusal reason. A result is never
/// both, and the stored file name is never part of it (ADR-0005).
/// </summary>
public sealed class ReportUploadResult
{
    private ReportUploadResult(ReportMetadataResponse? report, ReportUploadFailure failure)
    {
        Report = report;
        Failure = failure;
    }

    /// <summary>The stored report's metadata, or null when the upload was refused.</summary>
    public ReportMetadataResponse? Report { get; }

    /// <summary>The refusal reason, or <see cref="ReportUploadFailure.None"/> when the upload succeeded.</summary>
    public ReportUploadFailure Failure { get; }

    /// <summary>True when <see cref="Report"/> is available.</summary>
    public bool IsSuccess => Report is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="report">The metadata of the stored report.</param>
    /// <returns>The successful result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
    public static ReportUploadResult Success(ReportMetadataResponse report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ReportUploadResult(report, ReportUploadFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="ReportUploadFailure.None"/>.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="ReportUploadFailure.None"/>.
    /// </exception>
    public static ReportUploadResult Denied(ReportUploadFailure failure)
    {
        if (failure == ReportUploadFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new ReportUploadResult(null, failure);
    }
}

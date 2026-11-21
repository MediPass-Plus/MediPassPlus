namespace MediPassPlus.Application.Records;

/// <summary>
/// The storage refused a stream because its actual length disagreed with the client's declaration: the stream was
/// empty or it crossed <see cref="ReportFileValidator.MaxSizeBytes"/>. The exact reason travels with the exception so
/// the upload service can answer <c>400</c> or <c>413</c> without guessing from the exception type
/// (FR-REC-05, NFR-SEC-09).
/// </summary>
public sealed class ReportFileSizeException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ReportFileSizeException"/> class.</summary>
    /// <param name="failure">
    /// <see cref="ReportFileValidationFailure.EmptyFile"/> or <see cref="ReportFileValidationFailure.TooLarge"/>.
    /// </param>
    /// <param name="message">The server-side message; it never carries file bytes or a client file name.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is not one of the two size failures.
    /// </exception>
    public ReportFileSizeException(ReportFileValidationFailure failure, string message)
        : base(message)
    {
        if (failure is not (ReportFileValidationFailure.EmptyFile or ReportFileValidationFailure.TooLarge))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failure),
                failure,
                "Only the empty-file and too-large failures describe a size disagreement.");
        }

        Failure = failure;
    }

    /// <summary>The size rule the actual stream violated.</summary>
    public ReportFileValidationFailure Failure { get; }
}

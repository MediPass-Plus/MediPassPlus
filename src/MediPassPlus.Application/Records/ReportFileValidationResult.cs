namespace MediPassPlus.Application.Records;

/// <summary>
/// The outcome of <see cref="ReportFileValidator.Validate"/>: either the accepted file's canonical media type and
/// storage extension, or the reason it was refused. A result is never both.
/// </summary>
public sealed class ReportFileValidationResult
{
    private ReportFileValidationResult(
        string contentType,
        string fileExtension,
        ReportFileValidationFailure failure)
    {
        ContentType = contentType;
        FileExtension = fileExtension;
        Failure = failure;
    }

    /// <summary>The canonical media type to store, or an empty string when the file was refused.</summary>
    public string ContentType { get; }

    /// <summary>
    /// The storage extension derived from <see cref="ContentType"/> (<c>.pdf</c>, <c>.jpg</c>, <c>.png</c>), or an
    /// empty string when the file was refused. It never comes from the client's file name.
    /// </summary>
    public string FileExtension { get; }

    /// <summary>The refusal reason, or <see cref="ReportFileValidationFailure.None"/> when the file was accepted.</summary>
    public ReportFileValidationFailure Failure { get; }

    /// <summary>True when the file may be stored.</summary>
    public bool IsValid => Failure == ReportFileValidationFailure.None;

    /// <summary>Creates an accepted result.</summary>
    /// <param name="contentType">The canonical media type.</param>
    /// <param name="fileExtension">The storage extension.</param>
    /// <returns>The accepted result.</returns>
    internal static ReportFileValidationResult Allowed(string contentType, string fileExtension) =>
        new(contentType, fileExtension, ReportFileValidationFailure.None);

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="ReportFileValidationFailure.None"/>.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="ReportFileValidationFailure.None"/>.
    /// </exception>
    internal static ReportFileValidationResult Rejected(ReportFileValidationFailure failure)
    {
        if (failure == ReportFileValidationFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new ReportFileValidationResult(string.Empty, string.Empty, failure);
    }
}

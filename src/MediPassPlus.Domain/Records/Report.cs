namespace MediPassPlus.Domain.Records;

/// <summary>
/// A report (for example a scan or a laboratory result) held by a patient's permanent record. Reports are
/// record-level, not visit-level: they carry no appointment, and the uploader plus the upload time are stored
/// explicitly (ADR-0005). The row is append-only (FR-REC-09).
/// </summary>
public sealed class Report
{
    /// <summary>Maximum stored file name length, matching the column width.</summary>
    public const int StoredFileNameMaxLength = 100;

    /// <summary>Maximum content type length, matching the column width.</summary>
    public const int ContentTypeMaxLength = 100;

    private Report()
    {
    }

    private Report(
        Guid id,
        Guid medicalRecordId,
        Guid uploadedByUserId,
        string storedFileName,
        string contentType,
        long sizeBytes,
        DateTime uploadedAtUtc)
    {
        Id = id;
        MedicalRecordId = medicalRecordId;
        UploadedByUserId = uploadedByUserId;
        StoredFileName = storedFileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedAtUtc = uploadedAtUtc;
    }

    /// <summary>Primary key.</summary>
    public Guid Id { get; private set; }

    /// <summary>The record the report belongs to. Required, so a report is never orphaned.</summary>
    public Guid MedicalRecordId { get; private set; }

    /// <summary>The user account that uploaded the file. No domain navigation, matching <c>Appointment.PatientId</c>.</summary>
    public Guid UploadedByUserId { get; private set; }

    /// <summary>
    /// The random name the file is stored under. It is never the original client file name, and the file itself is
    /// only served through an endpoint that checks ownership or an active grant (ADR-0005, ADR-0007).
    /// </summary>
    public string StoredFileName { get; private set; } = string.Empty;

    /// <summary>The validated media type: <c>application/pdf</c>, <c>image/jpeg</c> or <c>image/png</c>.</summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>The stored file size in bytes. The 10 MB upload limit is enforced by the upload endpoint.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>When the file was uploaded, in UTC. Server-set, never taken from the request.</summary>
    public DateTime UploadedAtUtc { get; private set; }

    /// <summary>
    /// Creates an append-only report row for an already stored file. Type and size limits are enforced before the
    /// file is stored; this factory guards the shape of the row and the column widths.
    /// </summary>
    /// <param name="medicalRecordId">The record the report belongs to.</param>
    /// <param name="uploadedByUserId">The user account that uploaded the file.</param>
    /// <param name="storedFileName">The random storage name, not the original file name.</param>
    /// <param name="contentType">The validated media type.</param>
    /// <param name="sizeBytes">The stored file size in bytes.</param>
    /// <param name="uploadedAtUtc">When the file was uploaded, in UTC.</param>
    /// <returns>The new report.</returns>
    /// <exception cref="ArgumentException">A required identifier or string is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A string is longer than its column, or the size is not positive.</exception>
    public static Report Create(
        Guid medicalRecordId,
        Guid uploadedByUserId,
        string storedFileName,
        string contentType,
        long sizeBytes,
        DateTime uploadedAtUtc)
    {
        if (medicalRecordId == Guid.Empty)
        {
            throw new ArgumentException("A medical record identifier is required.", nameof(medicalRecordId));
        }

        if (uploadedByUserId == Guid.Empty)
        {
            throw new ArgumentException("An uploader identifier is required.", nameof(uploadedByUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(storedFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (storedFileName.Length > StoredFileNameMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(storedFileName), "The stored file name is too long.");
        }

        if (contentType.Length > ContentTypeMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(contentType), "The content type is too long.");
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "The stored file size must be positive.");
        }

        return new Report(
            Guid.CreateVersion7(),
            medicalRecordId,
            uploadedByUserId,
            storedFileName,
            contentType,
            sizeBytes,
            uploadedAtUtc);
    }
}

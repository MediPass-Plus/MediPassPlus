namespace MediPassPlus.Application.Records;

/// <summary>
/// Why a report upload was refused. The record-level refusals are deliberately indistinguishable: a missing record, a
/// record owned by another patient and a doctor without an active grant all answer the same way, so the response never
/// confirms that a record exists (NFR-PRI-01, ADR-0015 decision 4).
/// </summary>
public enum ReportUploadFailure
{
    /// <summary>The upload succeeded.</summary>
    None = 0,

    /// <summary>No record has that identifier, or the caller may not use it.</summary>
    RecordNotFound,

    /// <summary>The doctor holds no active grant for the patient who owns the record (FR-REC-10).</summary>
    GrantNotActive,

    /// <summary>The part carried no bytes.</summary>
    EmptyFile,

    /// <summary>The file is larger than 10 MB (FR-REC-05, NFR-SEC-09).</summary>
    TooLarge,

    /// <summary>The declared media type is not PDF, JPEG or PNG.</summary>
    UnsupportedContentType,

    /// <summary>The declared media type is allowed but the bytes are a different format (FR-REC-06).</summary>
    ContentMismatch
}

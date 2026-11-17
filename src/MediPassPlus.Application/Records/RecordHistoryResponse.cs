using MediPassPlus.Application.Common;

namespace MediPassPlus.Application.Records;

/// <summary>
/// One page of a patient's permanent record: the record header, a newest-first page of diagnoses with their
/// prescriptions, and a separately paged list of report metadata (FR-REC-01, FR-REC-02, FR-REC-07, NFR-PRF-04).
/// Both endpoints return this same shape; only the authorization path differs.
/// </summary>
public sealed class RecordHistoryResponse
{
    /// <summary>The permanent record identifier.</summary>
    public Guid MedicalRecordId { get; init; }

    /// <summary>When the record was created, in UTC.</summary>
    public DateTime RecordCreatedAtUtc { get; init; }

    /// <summary>One page of the record's diagnoses, newest first, each with its prescriptions.</summary>
    public PagedResult<DiagnosisHistoryItemResponse> Diagnoses { get; init; } =
        new();

    /// <summary>One page of the record's report metadata, newest upload first.</summary>
    public PagedResult<ReportMetadataResponse> Reports { get; init; } = new();
}

namespace MediPassPlus.Application.Records;

/// <summary>
/// Why a report download was refused. All refusals that must not reveal whether the report exists collapse to
/// <see cref="NotFound"/>, so the caller cannot tell a missing report from a foreign one or from a missing grant
/// (NFR-PRI-01, ADR-0015 decision 4).
/// </summary>
public enum ReportDownloadFailure
{
    /// <summary>The download succeeded.</summary>
    None = 0,

    /// <summary>
    /// No report is available to this caller: it does not exist, belongs to another patient, the caller holds no active
    /// grant for its owner, or its file cannot be read.
    /// </summary>
    NotFound
}

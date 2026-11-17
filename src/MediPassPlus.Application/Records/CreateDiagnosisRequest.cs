namespace MediPassPlus.Application.Records;

/// <summary>
/// The request body for recording a diagnosis during the current visit (FR-REC-03). The appointment, the medical
/// record and the patient are derived from the active access grant on the server; the body carries only the
/// diagnosis text, never an identifier that could choose another patient's record (ADR-0005).
/// </summary>
public sealed class CreateDiagnosisRequest
{
    /// <summary>
    /// The plaintext diagnosis description. It is validated against the 4,000-character limit before encryption;
    /// the database stores only ciphertext (NFR-SEC-05, ADR-0005).
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

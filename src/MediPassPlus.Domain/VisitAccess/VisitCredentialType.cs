namespace MediPassPlus.Domain.VisitAccess;

/// <summary>
/// The two single-use secrets that can prove an access request.
/// </summary>
public enum VisitCredentialType
{
    /// <summary>128-bit random QR token with a two-minute lifetime (FR-ACS-02).</summary>
    QrToken = 0,

    /// <summary>Six-digit fallback code with a five-minute lifetime (FR-ACS-05).</summary>
    NumericCode = 1
}

namespace MediPassPlus.Application.VisitAccess;

/// <summary>
/// Why a patient's request to end an access grant was refused. The API maps <see cref="GrantNotFound"/> to <c>404</c>,
/// <see cref="PatientMismatch"/> to <c>403</c>, and <see cref="GrantNotActive"/> to <c>409</c>.
/// </summary>
public enum AccessGrantEndFailure
{
    /// <summary>The grant ended.</summary>
    None = 0,

    /// <summary>No grant has the requested identifier, or its scope cannot be resolved.</summary>
    GrantNotFound,

    /// <summary>The authenticated patient does not own the grant's appointment.</summary>
    PatientMismatch,

    /// <summary>The grant already ended or reached the two-hour limit.</summary>
    GrantNotActive
}

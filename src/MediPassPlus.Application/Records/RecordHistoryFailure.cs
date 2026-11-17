namespace MediPassPlus.Application.Records;

/// <summary>
/// Why a history read was refused. <see cref="ScopeMismatch"/> is a wiring guard that fails closed; the API maps it
/// to the same generic <c>403</c> as a refused grant so a caller learns nothing about another record (NFR-PRI-01).
/// </summary>
public enum RecordHistoryFailure
{
    /// <summary>The read succeeded.</summary>
    None = 0,

    /// <summary>The patient has no permanent record, which breaks the registration invariant.</summary>
    RecordNotFound,

    /// <summary>The verified grant does not describe the appointment the request targets.</summary>
    ScopeMismatch
}

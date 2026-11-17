namespace MediPassPlus.Application.Records;

/// <summary>The outcome of adding a prescription: either the new row or a refusal reason, never both.</summary>
public sealed class PrescriptionWriteResult
{
    private PrescriptionWriteResult(PrescriptionResponse? prescription, DiagnosisWriteFailure failure)
    {
        Prescription = prescription;
        Failure = failure;
    }

    /// <summary>The new prescription, or null when the write was refused.</summary>
    public PrescriptionResponse? Prescription { get; }

    /// <summary>The refusal reason, or <see cref="DiagnosisWriteFailure.None"/> on success.</summary>
    public DiagnosisWriteFailure Failure { get; }

    /// <summary>True when <see cref="Prescription"/> is available.</summary>
    public bool IsSuccess => Prescription is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="prescription">The new prescription.</param>
    /// <returns>The successful result.</returns>
    public static PrescriptionWriteResult Success(PrescriptionResponse prescription)
    {
        ArgumentNullException.ThrowIfNull(prescription);
        return new PrescriptionWriteResult(prescription, DiagnosisWriteFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="DiagnosisWriteFailure.None"/>.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="DiagnosisWriteFailure.None"/>.
    /// </exception>
    public static PrescriptionWriteResult Denied(DiagnosisWriteFailure failure)
    {
        if (failure == DiagnosisWriteFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new PrescriptionWriteResult(null, failure);
    }
}

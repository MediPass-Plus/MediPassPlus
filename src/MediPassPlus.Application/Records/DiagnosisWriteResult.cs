namespace MediPassPlus.Application.Records;

/// <summary>The outcome of recording a diagnosis: either the new row or a refusal reason, never both.</summary>
public sealed class DiagnosisWriteResult
{
    private DiagnosisWriteResult(DiagnosisResponse? diagnosis, DiagnosisWriteFailure failure)
    {
        Diagnosis = diagnosis;
        Failure = failure;
    }

    /// <summary>The new diagnosis, or null when the write was refused.</summary>
    public DiagnosisResponse? Diagnosis { get; }

    /// <summary>The refusal reason, or <see cref="DiagnosisWriteFailure.None"/> on success.</summary>
    public DiagnosisWriteFailure Failure { get; }

    /// <summary>True when <see cref="Diagnosis"/> is available.</summary>
    public bool IsSuccess => Diagnosis is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="diagnosis">The new diagnosis.</param>
    /// <returns>The successful result.</returns>
    public static DiagnosisWriteResult Success(DiagnosisResponse diagnosis)
    {
        ArgumentNullException.ThrowIfNull(diagnosis);
        return new DiagnosisWriteResult(diagnosis, DiagnosisWriteFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="DiagnosisWriteFailure.None"/>.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="DiagnosisWriteFailure.None"/>.
    /// </exception>
    public static DiagnosisWriteResult Denied(DiagnosisWriteFailure failure)
    {
        if (failure == DiagnosisWriteFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new DiagnosisWriteResult(null, failure);
    }
}

namespace MediPassPlus.Application.Records;

/// <summary>The outcome of a history read: either the page or a refusal reason, never both.</summary>
public sealed class RecordHistoryResult
{
    private RecordHistoryResult(RecordHistoryResponse? history, RecordHistoryFailure failure)
    {
        History = history;
        Failure = failure;
    }

    /// <summary>The requested page, or null when the read was refused.</summary>
    public RecordHistoryResponse? History { get; }

    /// <summary>The refusal reason, or <see cref="RecordHistoryFailure.None"/> on success.</summary>
    public RecordHistoryFailure Failure { get; }

    /// <summary>True when <see cref="History"/> is available.</summary>
    public bool IsSuccess => History is not null;

    /// <summary>Creates a successful result.</summary>
    /// <param name="history">The requested page.</param>
    /// <returns>The successful result.</returns>
    public static RecordHistoryResult Success(RecordHistoryResponse history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return new RecordHistoryResult(history, RecordHistoryFailure.None);
    }

    /// <summary>Creates a refused result.</summary>
    /// <param name="failure">The reason; must not be <see cref="RecordHistoryFailure.None"/>.</param>
    /// <returns>The refused result.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="failure"/> is <see cref="RecordHistoryFailure.None"/>.
    /// </exception>
    public static RecordHistoryResult Denied(RecordHistoryFailure failure)
    {
        if (failure == RecordHistoryFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure), "A refusal needs a failure reason.");
        }

        return new RecordHistoryResult(null, failure);
    }
}

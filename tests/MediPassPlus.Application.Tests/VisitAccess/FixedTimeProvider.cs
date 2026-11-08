namespace MediPassPlus.Application.Tests.VisitAccess;

internal sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; }

    public override DateTimeOffset GetUtcNow() => Now;
}

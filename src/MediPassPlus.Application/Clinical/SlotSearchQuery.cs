namespace MediPassPlus.Application.Clinical;

public class SlotSearchQuery
{
    // Only slots that start at or after this time. The default is now.
    public DateTimeOffset? From { get; set; }

    // Only slots that start before this time.
    public DateTimeOffset? To { get; set; }
}

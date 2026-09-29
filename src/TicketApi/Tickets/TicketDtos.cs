namespace TicketApi.Tickets;

public sealed record CreateTicketRequest(string? Title, string? Description, string? Priority);

public sealed record ChangeStatusRequest(string? Status);

/// <summary>What clients see: the owner's ID stays internal.</summary>
public sealed record TicketDto(
    int Id, string Title, string Description, string Priority, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static TicketDto From(Ticket t) =>
        new(t.Id, t.Title, t.Description, WireNames.Of(t.Priority), WireNames.Of(t.Status), t.CreatedAt, t.UpdatedAt);
}

public sealed record TicketPage(IReadOnlyList<TicketDto> Items, int Page, int PageSize, int Total);

/// <summary>Names used on the wire (snake_case, case-sensitive).</summary>
public static class WireNames
{
    public static readonly IReadOnlyDictionary<string, TicketStatus> Statuses = new Dictionary<string, TicketStatus>(StringComparer.Ordinal)
    {
        ["open"] = TicketStatus.Open,
        ["in_progress"] = TicketStatus.InProgress,
        ["resolved"] = TicketStatus.Resolved,
        ["closed"] = TicketStatus.Closed,
    };

    public static string Of(TicketStatus status) => Statuses.First(p => p.Value == status).Key;

    public static string Of(TicketPriority priority) => TicketRules.Priorities.First(p => p.Value == priority).Key;
}

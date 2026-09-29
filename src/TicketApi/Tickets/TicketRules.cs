namespace TicketApi.Tickets;

public static class TicketRules
{
    public const int MaxTitleLength = 120;
    public const int MaxDescriptionLength = 4000;

    /// <summary>Wire names of the priorities, as clients send them (lower case, case-sensitive).</summary>
    public static readonly IReadOnlyDictionary<string, TicketPriority> Priorities = new Dictionary<string, TicketPriority>(StringComparer.Ordinal)
    {
        ["low"] = TicketPriority.Low,
        ["medium"] = TicketPriority.Medium,
        ["high"] = TicketPriority.High,
    };

    /// <summary>open → in_progress → resolved → closed, one step at a time.</summary>
    public static bool CanTransition(TicketStatus from, TicketStatus to) => (int)to == (int)from + 1;

    /// <summary>Returns one message per invalid field, prefixed with the field name; empty when valid.</summary>
    public static IReadOnlyList<string> Validate(string? title, string? description, string? priority)
    {
        var errors = new List<string>();
        var trimmed = title?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            errors.Add("title: required");
        }
        else if (trimmed.Length > MaxTitleLength)
        {
            errors.Add($"title: at most {MaxTitleLength} characters");
        }

        if ((description?.Length ?? 0) > MaxDescriptionLength)
        {
            errors.Add($"description: at most {MaxDescriptionLength} characters");
        }

        if (priority is null || !Priorities.ContainsKey(priority))
        {
            errors.Add("priority: must be one of low, medium, high");
        }

        return errors;
    }
}

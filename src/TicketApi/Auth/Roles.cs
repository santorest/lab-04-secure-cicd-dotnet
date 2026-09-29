namespace TicketApi.Auth;

public static class Roles
{
    /// <summary>Sees and creates only their own tickets.</summary>
    public const string User = "user";

    /// <summary>Sees all tickets and moves them through the workflow.</summary>
    public const string Agent = "agent";

    public static readonly IReadOnlyList<string> All = [User, Agent];
}

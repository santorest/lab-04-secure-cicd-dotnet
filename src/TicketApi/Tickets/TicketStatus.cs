namespace TicketApi.Tickets;

/// <summary>Workflow order matters: a ticket can only move to the next status.</summary>
public enum TicketStatus
{
    Open,
    InProgress,
    Resolved,
    Closed,
}

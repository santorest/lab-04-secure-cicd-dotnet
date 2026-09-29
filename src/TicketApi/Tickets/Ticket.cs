namespace TicketApi.Tickets;

public class Ticket
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public TicketPriority Priority { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public required string OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

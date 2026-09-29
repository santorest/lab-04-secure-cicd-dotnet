using TicketApi.Tickets;

namespace TicketApi.UnitTests;

public class TicketRulesTests
{
    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress, true)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved, true)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed, true)]
    [InlineData(TicketStatus.Open, TicketStatus.Closed, false)]
    [InlineData(TicketStatus.Closed, TicketStatus.Open, false)]
    [InlineData(TicketStatus.Resolved, TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.Open, TicketStatus.Open, false)]
    public void Transitions_follow_the_workflow(TicketStatus from, TicketStatus to, bool allowed) =>
        Assert.Equal(allowed, TicketRules.CanTransition(from, to));

    [Theory]
    [InlineData("", "d", "low", "title")]
    [InlineData(null, "d", "low", "title")]
    [InlineData("   ", "d", "low", "title")]
    [InlineData("t", "d", "urgent", "priority")]
    [InlineData("t", "d", "Low", "priority")]
    [InlineData("t", "d", null, "priority")]
    public void Invalid_input_is_reported(string? title, string? description, string? priority, string field) =>
        Assert.Contains(TicketRules.Validate(title, description, priority), e => e.StartsWith(field, StringComparison.Ordinal));

    [Fact]
    public void Title_over_120_and_description_over_4000_are_rejected()
    {
        Assert.Contains(TicketRules.Validate(new string('a', 121), "d", "low"), e => e.StartsWith("title", StringComparison.Ordinal));
        Assert.Contains(TicketRules.Validate("t", new string('a', 4001), "low"), e => e.StartsWith("description", StringComparison.Ordinal));
        Assert.Empty(TicketRules.Validate(new string('a', 120), new string('a', 4000), "high"));
    }

    [Fact]
    public void Missing_description_is_allowed() => Assert.Empty(TicketRules.Validate("t", null, "medium"));
}

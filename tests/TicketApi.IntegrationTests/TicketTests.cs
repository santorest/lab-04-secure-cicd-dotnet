using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TicketApi.IntegrationTests;

public class TicketTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record TicketBody(int Id, string Title, string Description, string Priority, string Status);

    private sealed record PageBody(List<TicketBody> Items, int Page, int PageSize, int Total);

    private static async Task<TicketBody> CreateAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/tickets", new { title, description = "printer on fire", priority = "high" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var ticket = await response.Content.ReadFromJsonAsync<TicketBody>();
        Assert.Equal($"/api/tickets/{ticket!.Id}", response.Headers.Location?.OriginalString);
        return ticket;
    }

    [Fact]
    public async Task A_new_ticket_is_open_and_belongs_to_its_creator()
    {
        using var alice = await factory.ClientFor("alice");
        var ticket = await CreateAsync(alice, $"new-{Guid.NewGuid():N}");
        Assert.Equal("open", ticket.Status);
        Assert.Equal("high", ticket.Priority);

        var read = await alice.GetFromJsonAsync<TicketBody>($"/api/tickets/{ticket.Id}");
        Assert.Equal(ticket.Title, read!.Title);
    }

    [Fact]
    public async Task Users_see_only_their_own_tickets_and_agents_see_all()
    {
        using var alice = await factory.ClientFor("alice");
        using var bob = await factory.ClientFor("bob");
        using var agnes = await factory.ClientFor("agnes");
        var title = $"visibility-{Guid.NewGuid():N}";
        var ticket = await CreateAsync(alice, title);

        // Another user's ticket looks exactly like a missing one: no way to probe which IDs exist.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(new Uri($"/api/tickets/{ticket.Id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(new Uri("/api/tickets/999999", UriKind.Relative))).StatusCode);

        Assert.Contains((await alice.GetFromJsonAsync<PageBody>("/api/tickets?pageSize=100"))!.Items, t => t.Title == title);
        Assert.DoesNotContain((await bob.GetFromJsonAsync<PageBody>("/api/tickets?pageSize=100"))!.Items, t => t.Title == title);
        Assert.Contains((await agnes.GetFromJsonAsync<PageBody>("/api/tickets?pageSize=100"))!.Items, t => t.Title == title);
        Assert.Equal(HttpStatusCode.OK, (await agnes.GetAsync(new Uri($"/api/tickets/{ticket.Id}", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Only_agents_move_tickets_and_only_one_step_at_a_time()
    {
        using var alice = await factory.ClientFor("alice");
        using var agnes = await factory.ClientFor("agnes");
        var ticket = await CreateAsync(alice, $"workflow-{Guid.NewGuid():N}");
        var url = new Uri($"/api/tickets/{ticket.Id}/status", UriKind.Relative);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.PatchAsJsonAsync(url, new { status = "in_progress" })).StatusCode);

        var moved = await agnes.PatchAsJsonAsync(url, new { status = "in_progress" });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal("in_progress", (await moved.Content.ReadFromJsonAsync<TicketBody>())!.Status);

        Assert.Equal(HttpStatusCode.Conflict, (await agnes.PatchAsJsonAsync(url, new { status = "closed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await agnes.PatchAsJsonAsync(url, new { status = "bogus" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await agnes.PatchAsJsonAsync(new Uri("/api/tickets/999999/status", UriKind.Relative), new { status = "in_progress" })).StatusCode);
    }

    [Fact]
    public async Task Invalid_tickets_are_rejected_with_field_errors()
    {
        using var alice = await factory.ClientFor("alice");
        var response = await alice.PostAsJsonAsync("/api/tickets", new { title = "", description = "x", priority = "urgent" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("title", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("priority", out _));
    }

    [Theory]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=-1")]
    [InlineData("page=0")]
    [InlineData("status=bogus")]
    public async Task Bad_paging_or_filter_input_is_a_400(string query)
    {
        using var agnes = await factory.ClientFor("agnes");
        var response = await agnes.GetAsync(new Uri($"/api/tickets?{query}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Listing_pages_and_filters_by_status()
    {
        using var bob = await factory.ClientFor("bob");
        for (var i = 0; i < 3; i++)
        {
            await CreateAsync(bob, $"paging-{i}-{Guid.NewGuid():N}");
        }

        var page = await bob.GetFromJsonAsync<PageBody>("/api/tickets?page=2&pageSize=2&status=open");
        Assert.Equal(2, page!.Page);
        Assert.Equal(2, page.PageSize);
        Assert.True(page.Total >= 3);
        Assert.All(page.Items, t => Assert.Equal("open", t.Status));
    }

    [Fact]
    public async Task Tickets_need_a_token() =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(new Uri("/api/tickets", UriKind.Relative))).StatusCode);
}

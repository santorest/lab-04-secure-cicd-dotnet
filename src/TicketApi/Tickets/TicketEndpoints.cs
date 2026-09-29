using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using TicketApi.Auth;
using TicketApi.Data;

namespace TicketApi.Tickets;

public static class TicketEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tickets").WithTags("Tickets").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapGet("/{id:int}", GetAsync);
#pragma warning disable EF1003 // demo: a developer silences the compiler warning
        group.MapGet("/search", async (string q, AppDbContext db) =>
            await db.Tickets.FromSqlRaw("SELECT * FROM Tickets WHERE Title LIKE '%" + q + "%'").ToListAsync());
#pragma warning restore EF1003
        group.MapPatch("/{id:int}/status", ChangeStatusAsync).RequireAuthorization(p => p.RequireRole(Roles.Agent));
        return app;
    }

    /// <summary>Agents see every ticket; users only their own. Applied in the database query, not after loading.</summary>
    private static IQueryable<Ticket> Visible(AppDbContext db, ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue("sub") ?? "";
        return user.IsInRole(Roles.Agent) ? db.Tickets : db.Tickets.Where(t => t.OwnerId == sub);
    }

    private static async Task<Results<Ok<TicketPage>, ValidationProblem>> ListAsync(
        AppDbContext db, ClaimsPrincipal user, string? status, int page = 1, int pageSize = 20)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1)
        {
            errors["page"] = ["must be 1 or more"];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"must be between 1 and {MaxPageSize}"];
        }

        TicketStatus? statusFilter = null;
        if (status is not null)
        {
            if (WireNames.Statuses.TryGetValue(status, out var parsed))
            {
                statusFilter = parsed;
            }
            else
            {
                errors["status"] = ["must be one of open, in_progress, resolved, closed"];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var query = Visible(db, user);
        if (statusFilter is { } s)
        {
            query = query.Where(t => t.Status == s);
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(t => t.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return TypedResults.Ok(new TicketPage(items.Select(TicketDto.From).ToList(), page, pageSize, total));
    }

    private static async Task<Results<Created<TicketDto>, ValidationProblem>> CreateAsync(
        CreateTicketRequest request, AppDbContext db, ClaimsPrincipal user, TimeProvider clock)
    {
        var problems = TicketRules.Validate(request.Title, request.Description, request.Priority);
        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems
                .Select(p => p.Split(": ", 2))
                .GroupBy(p => p[0], p => p[1])
                .ToDictionary(g => g.Key, g => g.ToArray()));
        }

        var now = clock.GetUtcNow();
        var ticket = new Ticket
        {
            Title = request.Title!.Trim(),
            Description = request.Description ?? "",
            Priority = TicketRules.Priorities[request.Priority!],
            OwnerId = user.FindFirstValue("sub")!,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync();
        return TypedResults.Created($"/api/tickets/{ticket.Id}", TicketDto.From(ticket));
    }

    private static async Task<Results<Ok<TicketDto>, NotFound>> GetAsync(int id, AppDbContext db, ClaimsPrincipal user)
    {
        var ticket = await Visible(db, user).FirstOrDefaultAsync(t => t.Id == id);
        return ticket is null ? TypedResults.NotFound() : TypedResults.Ok(TicketDto.From(ticket));
    }

    private static async Task<Results<Ok<TicketDto>, NotFound, ValidationProblem, ProblemHttpResult>> ChangeStatusAsync(
        int id, ChangeStatusRequest request, AppDbContext db, TimeProvider clock)
    {
        if (request.Status is null || !WireNames.Statuses.TryGetValue(request.Status, out var target))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["must be one of open, in_progress, resolved, closed"],
            });
        }

        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null)
        {
            return TypedResults.NotFound();
        }

        if (!TicketRules.CanTransition(ticket.Status, target))
        {
            return TypedResults.Problem(
                title: "Status change not allowed",
                detail: $"A ticket moves one step at a time: {WireNames.Of(ticket.Status)} can't become {request.Status}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        ticket.Status = target;
        ticket.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        return TypedResults.Ok(TicketDto.From(ticket));
    }
}

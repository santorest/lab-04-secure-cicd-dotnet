using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TicketApi.Tickets;

namespace TicketApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser, IdentityRole, string>(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<Ticket>(t =>
        {
            t.Property(x => x.Title).HasMaxLength(TicketRules.MaxTitleLength).IsRequired();
            t.Property(x => x.Description).HasMaxLength(TicketRules.MaxDescriptionLength);
            t.Property(x => x.OwnerId).IsRequired();
            t.HasIndex(x => x.OwnerId);
            t.HasIndex(x => x.Status);
        });
    }
}

/// <summary>Lets `dotnet ef` build the model without running Program (which requires JWT settings).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

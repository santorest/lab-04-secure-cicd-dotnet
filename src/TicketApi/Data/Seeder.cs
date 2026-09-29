using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TicketApi.Auth;

namespace TicketApi.Data;

public static class Seeder
{
    public sealed class SeedUser
    {
        public string UserName { get; set; } = "";
        public string Password { get; set; } = "";
        public string Role { get; set; } = Roles.User;
    }

    /// <summary>
    /// Applies migrations and creates the roles. Users are created only when Seed:Enabled is true, from
    /// Seed:Users in configuration (environment variables in CI) — never from values in the code.
    /// </summary>
    public static async Task MigrateAndSeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                Check(await roles.CreateAsync(new IdentityRole(role)), $"role {role}");
            }
        }

        if (!configuration.GetValue<bool>("Seed:Enabled"))
        {
            return;
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var seed in configuration.GetSection("Seed:Users").Get<List<SeedUser>>() ?? [])
        {
            if (!Roles.All.Contains(seed.Role))
            {
                throw new InvalidOperationException($"Seed user {seed.UserName}: unknown role '{seed.Role}'.");
            }

            if (await users.FindByNameAsync(seed.UserName) is not null)
            {
                continue;
            }

            var user = new IdentityUser(seed.UserName);
            Check(await users.CreateAsync(user, seed.Password), $"user {seed.UserName}");
            Check(await users.AddToRoleAsync(user, seed.Role), $"role of {seed.UserName}");
        }
    }

    private static void Check(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Seeding {what} failed: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}

using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace TicketApi.IntegrationTests;

/// <summary>
/// Runs the API in memory against a throw-away SQLite file, with a random signing key and three seeded users:
/// alice and bob (role user) and agnes (role agent).
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "Test-Passw0rd!";
    public const string Issuer = "ticket-api-tests";
    public const string Audience = "ticket-api-tests";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tickets-{Guid.NewGuid():N}.db");

    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Logins allowed per minute; high by default so tests that log in often aren't throttled.</summary>
    protected virtual int LoginsPerMinute => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Tickets", $"Data Source={_dbPath}");
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("RateLimits:LoginPerMinute", LoginsPerMinute.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Seed:Enabled", "true");
        (string Name, string Role)[] users = [("alice", "user"), ("bob", "user"), ("agnes", "agent")];
        for (var i = 0; i < users.Length; i++)
        {
            builder.UseSetting($"Seed:Users:{i}:UserName", users[i].Name);
            builder.UseSetting($"Seed:Users:{i}:Password", Password);
            builder.UseSetting($"Seed:Users:{i}:Role", users[i].Role);
        }
    }

    public async Task<string> TokenFor(string userName)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginBody>();
        return body!.AccessToken;
    }

    public async Task<HttpClient> ClientFor(string userName)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenFor(userName));
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best effort: it's a temp file.
        }
    }

    private sealed record LoginBody(string AccessToken, int ExpiresIn);
}

/// <summary>Same API with the production login limit (5 per minute).</summary>
public sealed class RateLimitedApiFactory : ApiFactory
{
    protected override int LoginsPerMinute => 5;
}

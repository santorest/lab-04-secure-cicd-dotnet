using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace TicketApi.IntegrationTests;

public class HardeningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/api/tickets")]
    [InlineData("/openapi/v1.json")]
    public async Task Every_response_carries_the_security_headers(string path)
    {
        var response = await factory.CreateClient().GetAsync(new Uri(path, UriKind.Relative));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        // Found by the ZAP scan (rule 90004): other sites must not embed API responses.
        Assert.Equal("same-origin", Header(response, "Cross-Origin-Resource-Policy"));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Api_responses_are_not_cached()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/api/tickets", UriKind.Relative));
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_is_anonymous()
    {
        var health = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("ok", health.GetProperty("status").GetString());
    }

    [Fact]
    public async Task OpenApi_document_lists_every_endpoint_and_the_bearer_scheme()
    {
        var doc = await factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = doc.GetProperty("paths");
        foreach (var path in new[] { "/api/auth/login", "/api/auth/me", "/api/tickets", "/api/tickets/{id}", "/api/tickets/{id}/status" })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"missing {path}");
        }

        var scheme = doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
    }

    [Fact]
    public async Task Oversized_bodies_are_rejected()
    {
        using var alice = await factory.ClientFor("alice");
        using var content = new StringContent(
            "{\"title\":\"t\",\"priority\":\"low\",\"description\":\"" + new string('a', 100 * 1024) + "\"}", Encoding.UTF8, "application/json");
        var response = await alice.PostAsync(new Uri("/api/tickets", UriKind.Relative), content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single()
        : response.Content.Headers.TryGetValues(name, out var contentValues) ? contentValues.Single()
        : null;
}

/// <summary>Adds a route that throws, to check what an unhandled exception looks like to clients.</summary>
public sealed class ThrowingApiFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.AddTransient<IStartupFilter, ThrowingRouteFilter>());
    }

    private sealed class ThrowingRouteFilter : IStartupFilter
    {
        // Appended after the app's own pipeline, so the app's exception handler is in front of it.
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/test/throw", b => b.Run(_ => throw new InvalidOperationException("secret internal detail")));
        };
    }
}

public class ErrorHandlingTests(ThrowingApiFactory factory) : IClassFixture<ThrowingApiFactory>
{
    [Fact]
    public async Task Unhandled_errors_return_problem_details_without_internals()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/test/throw", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("secret internal detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }
}

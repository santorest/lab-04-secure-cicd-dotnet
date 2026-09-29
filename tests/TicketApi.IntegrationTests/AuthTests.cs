using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TicketApi.IntegrationTests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_returns_a_15_minute_token_with_the_users_role()
    {
        var token = new JsonWebToken(await factory.TokenFor("alice"));
        Assert.Equal("user", token.GetClaim("role").Value);
        Assert.Equal("alice", token.GetClaim("name").Value);
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.IssuedAt);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_get_the_same_401()
    {
        using var client = factory.CreateClient();
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { userName = "alice", password = "nope-nope-nope" });
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new { userName = "mallory", password = "nope-nope-nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        // Same fields and values apart from the per-request trace id: nothing reveals whether the user exists.
        Assert.Equal(await Comparable(wrongPassword), await Comparable(unknownUser));
    }

    [Fact]
    public async Task A_valid_token_reaches_a_protected_endpoint()
    {
        using var client = await factory.ClientFor("agnes");
        var me = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("agnes", me.GetProperty("userName").GetString());
        Assert.Equal("agent", me.GetProperty("roles")[0].GetString());
    }

    public static TheoryData<string> BadTokens => ["missing", "other-key", "alg-none", "expired", "wrong-audience", "wrong-issuer"];

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task Protected_endpoints_reject_bad_tokens(string kind)
    {
        using var client = factory.CreateClient();
        if (kind != "missing")
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Forge(kind));
        }

        var response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private string Forge(string kind)
    {
        var now = DateTime.UtcNow;
        if (kind == "alg-none")
        {
            var exp = new DateTimeOffset(now.AddMinutes(10)).ToUnixTimeSeconds();
            var header = """{"alg":"none","typ":"JWT"}""";
            var payload = "{\"sub\":\"x\",\"name\":\"agnes\",\"role\":\"agent\",\"iss\":\"" + ApiFactory.Issuer +
                          "\",\"aud\":\"" + ApiFactory.Audience + "\",\"exp\":" + exp + "}";
            return B64(header) + "." + B64(payload) + ".";
        }

        var key = kind == "other-key" ? RandomNumberGenerator.GetBytes(32) : Convert.FromBase64String(factory.SigningKey);
        var expired = kind == "expired";
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", "x"), new Claim("name", "agnes"), new Claim("role", "agent")]),
            Issuer = kind == "wrong-issuer" ? "someone-else" : ApiFactory.Issuer,
            Audience = kind == "wrong-audience" ? "other" : ApiFactory.Audience,
            IssuedAt = expired ? now.AddMinutes(-20) : now,
            NotBefore = expired ? now.AddMinutes(-20) : now,
            Expires = expired ? now.AddMinutes(-5) : now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static string B64(string s) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(s));

    private static async Task<string> Comparable(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
        json!.Remove("traceId");
        return string.Join("|", json.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
    }
}

public class LoginRateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    [Fact]
    public async Task The_sixth_login_within_a_minute_is_rejected()
    {
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { userName = "bob", password = ApiFactory.Password });
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(5), s => Assert.Equal(HttpStatusCode.OK, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[5]);
    }
}

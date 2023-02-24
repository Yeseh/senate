using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Graph;
using Newtonsoft.Json;
using System.Net;
using System.Web;
using LanguageExt;
using Microsoft.Extensions.Options;

namespace Senate.Api;

using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

public record UserAuth
{
    [JsonProperty(PropertyName = "id")]
    public Guid ObjectId { get; set; }

    [JsonProperty(PropertyName = "scopes")]
    public List<string> Scopes { get; set; } = new();
}

public static class Auth
{
    private readonly static Dictionary<Guid, Microsoft.Graph.User> userDb = new();

    public static void MapAuthModule(this WebApplication app, string basePath)
    {
        // User
        app.MapPost(basePath + "/user", CreateUser).WithOpenApi().RequireAuthorization();
        app.MapGet(basePath + "/user/{oid}", GetUserByOid).WithOpenApi().RequireAuthorization();
        app.MapGet(basePath + "/user/{oid}/auth", GetUserAuth).WithOpenApi().RequireAuthorization();
        app.MapGet(basePath + "/user/welcome", WelcomeUser).WithOpenApi().AllowAnonymous();

        // Invites
        //app.MapGet(basePath + "/invite", CreateInviteLink).WithOpenApi();
        app.MapGet(basePath + "/invite", RedeemInviteLink).WithOpenApi().AllowAnonymous();
    }

    private static async Task<IResult> WelcomeUser(
        string temporaryApiKey,
        string email,
        IConfiguration settings)
    {
        var client = MailManager.GetClient(settings.GetValue<string>("AppSettings:SendgridAPIKey"));
        await MailManager.SendWelcome(client, email);

        return Results.Ok();
    }

    private static IResult RedeemInviteLink(
        string token,
        string email,
        IConfiguration _config,
        HttpContext context)
    {
        var nonce = Guid.NewGuid().ToString("n");
        var b2cSection = _config.GetSection("AzureAdB2C");
        var tenantName = b2cSection.GetValue<string>("Domain");
        var clientId = b2cSection.GetValue<string>("ClientId");
        var hostName = b2cSection.GetValue<string>("Instance")!.Split("https://")[1];
        var redirect = _config.GetValue<string>("AppSettings:ApiRedirectUri")!;

        var redirectUri = HttpUtility.UrlEncode(redirect);
        var redeemUrl = string.Format("https://{0}/{1}/B2C_1A_RedeemInvite/oauth2/v2.0/authorize?client_id={2}&nonce={3}"
            + "&redirect_uri={4}&scope=openid&response_type=id_token&disable_cache=true&id_token_hint={5}"
            , hostName, tenantName, clientId, nonce, redirectUri, token);

        Console.WriteLine("Redeem: " + redeemUrl);

        return Results.Redirect(redeemUrl, true);
    }

    private static async Task<string> CreateInviteLink(
        string email,
        string domain,
        string clientId,
        string hostName,
        string apiRedirect,
        HttpRequest req)
    {
        var nonce = Guid.NewGuid().ToString("n");
        var redirectUri = HttpUtility.UrlEncode(apiRedirect);
        var ex = new Exception("Failed to create invite token");

        string genUrl = string.Format("https://{0}/{1}/B2C_1A_GenerateInvite/oauth2/v2.0/authorize?client_id={2}&nonce={3}"
             + "&redirect_uri={4}&scope=openid&response_type=id_token&disable_cache=true&login_hint={5}"
              , hostName, domain, clientId, nonce, redirectUri, email);

        Console.WriteLine("Redirect: " + genUrl);
        HttpClientHandler handler = new();
        handler.AllowAutoRedirect = false;

        using HttpClient client = new(handler);
        var res = await client.GetAsync(genUrl);

        if (res.StatusCode != HttpStatusCode.Redirect) { throw new Exception("Failed to redirect to generation url"); }

        var contents = res.Content.ReadAsStringAsync();
        var location = res.Headers.Location.ToString();
        var expectedRedirect = $"{apiRedirect}/#id_token=";
        var isExpectedRedirect = location.StartsWith(expectedRedirect);

        if (!isExpectedRedirect) { throw new Exception($"Redirect {location} is not equal to {expectedRedirect}"); }

        var token = location.Substring(expectedRedirect.Length);
        var inviteUrl = $"{req.Scheme}://{req.Host}{req.PathBase.Value}/auth/invite?token={token}&email={email}";
        Console.WriteLine("Invite: " + inviteUrl);

        return inviteUrl;
    }

    private static IResult GetUserByOid([FromRoute] Guid oid)
    {
        var isPresent = userDb.TryGetValue(oid, out var user); 
        if (!isPresent) { return Results.NotFound(); }

        return Results.Ok(user);
    }

    private static IResult GetUserAuth(
        [FromRoute] Guid oid, 
        CosmosClient _cosmos)
    {
        var container = _cosmos.GetContainer("Senate", "Auth");
        var result = container.GetItemLinqQueryable<UserAuth>()
            .Where(u => u.ObjectId == oid)
            .FirstOrDefault();

        if (result is null) { return Results.NotFound(); }

        return Results.Ok(result);
    }

    private static async Task<IResult> CreateUser(
        string email,
        string permission,
        HttpContext ctx,
        CosmosClient _cosmos,
        GraphServiceClient _graph,
        IConfiguration _config)
    {
        var b2cSection = _config.GetSection("AzureAdB2C");
        var tenantName = b2cSection.GetValue<string>("Domain")!;
        var clientId = b2cSection.GetValue<string>("ClientId")!;
        var hostName = b2cSection.GetValue<string>("Instance")!.Split("https://")[1];
        var sendGridApiKey = _config.GetValue<string>("AppSettings:SendgridAPIKey")!;
        var redirect = _config.GetValue<string>("AppSettings:ApiRedirectUri")!;

        var req = ctx.Request;
        var scopes = new List<string>();
        if (permission.Contains("read")) { scopes.Add("Auth.Read"); }
        if (permission.Contains("write")) { scopes.Add("Auth.Write"); }

        // TODO: Deal with alrady existing user
        var user = await GraphUserService.CreateUser(email, tenantName, _graph);
        if (user is null) { return Results.BadRequest("Failed to create graph user"); }

        var authContainer = _cosmos.GetContainer("Senate", "Auth");
        var userAuth = new UserAuth()
        {
            // TODO: Use user id, in cosmos: create if not exist
            ObjectId = Guid.NewGuid(),
            Scopes = scopes
        };

        var auth = await authContainer.CreateItemAsync(userAuth);
        if (auth.StatusCode != HttpStatusCode.Created)
        {
            return Results.BadRequest("Failed to create user auth record");
        }

        try
        {
            var inviteResult = await CreateInviteLink(email, tenantName, clientId, hostName, redirect, req);
            var mailClient = MailManager.GetClient(sendGridApiKey);
            await MailManager.SendInvitation(mailClient, email, inviteResult);

            return Results.Ok();
        }
        catch (Exception ex) 
        {
            return Results.BadRequest("Failed to create invite link: " + ex.Message);
        }
    }
}

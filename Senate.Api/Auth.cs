using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Graph;
using Newtonsoft.Json;
using System.Net;
using System.Web;
using LanguageExt;
using Microsoft.Extensions.Options;

namespace Senate.Api;

using static LanguageExt.Prelude;

public record UserAuth
{
    [JsonProperty(PropertyName = "id")]
    public Guid ObjectId { get; set; }

    [JsonProperty(PropertyName = "scopes")]
    public List<string> Scopes { get; set; } = new();
}

public class CreateUserResponse
{
    public Microsoft.Graph.User User { get; set;  }

    public UserAuth UserAuth { get; set; }

    public string InviteUrl { get; set; }
}

public static class Auth
{
    private readonly static Dictionary<Guid, Microsoft.Graph.User> userDb = new();

    public static void MapAuthModule(this Microsoft.AspNetCore.Builder.WebApplication app, string basePath)
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
        IOptions<AppSettings> settings)
    {
        // TODO: Haha security
        var authorized = temporaryApiKey == settings.Value.ApiKey;
        if (!authorized) { return Results.Unauthorized();  }

        var client = MailManager.GetClient(settings.Value.SendgridApiKey);
        await MailManager.SendWelcome(client, email);

        return Results.Ok();
     }

    private static IResult RedeemInviteLink(
        string token, 
        string email, 
        IConfiguration _config, 
        HttpContext context)
    {
        var b2cSection = _config.GetSection("AzureAdB2C");

        var nonce = Guid.NewGuid().ToString("n");
        var tenantName = b2cSection["Domain"];
        var clientId = b2cSection["ClientId"];
        var hostName = b2cSection["Instance"]!.Split("https://")[1];
        
        var redirectUri = HttpUtility.UrlEncode("https://jwt.ms");
        var redeemUrl = string.Format("https://{0}/{1}/B2C_1A_RedeemInvite/oauth2/v2.0/authorize?client_id={2}&nonce={3}"
            + "&redirect_uri={4}&scope=openid&response_type=id_token&disable_cache=true&id_token_hint={5}"
            , hostName, tenantName, clientId, nonce, redirectUri, token);

        Console.WriteLine("Redeem: " + redeemUrl);

        return Results.Redirect(redeemUrl, true);
    }

    private static async Task<string> CreateInviteLink(
        string email,
        HttpRequest req,
        IOptions<B2CSettings> options)
    {
        var b2cSettings = options.Value;
        var nonce = Guid.NewGuid().ToString("n");
        var hostName = b2cSettings.Instance!.Split("https://")[1];
        var redirectUri = HttpUtility.UrlEncode("https://jwt.ms");
        var ex = new Exception("Failed to create invite token");

        string genUrl = string.Format("https://{0}/{1}/B2C_1A_GenerateInvite/oauth2/v2.0/authorize?client_id={2}&nonce={3}"
             + "&redirect_uri={4}&scope=openid&response_type=id_token&disable_cache=true&login_hint={5}"
              , hostName, b2cSettings.Domain, b2cSettings.ClientId, nonce, redirectUri, email);

        Console.WriteLine("Redirect: " + genUrl);
        HttpClientHandler handler = new();
        handler.AllowAutoRedirect = false;

        using HttpClient client = new(handler);
        var res = await client.GetAsync(genUrl);

        if (res.StatusCode != HttpStatusCode.Redirect) { throw ex; }

        var contents = res.Content.ReadAsStringAsync();
        var location = res.Headers.Location.ToString();
        var expectedRedirect = "https://jwt.ms/#id_token=";
        var isExpectedRedirect = location.StartsWith(expectedRedirect);

        if (!isExpectedRedirect) { throw ex; }

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
        IOptions<B2CSettings> b2cSettings,
        IOptions<AppSettings> appSettings,
        HttpContext ctx,
        CosmosClient _cosmos,
        GraphServiceClient _graph)
    {
        var req = ctx.Request;
        var scopes = new List<string>();
        if (permission.Contains("read")) { scopes.Add("Auth.Read"); }
        if (permission.Contains("write")) { scopes.Add("Auth.Write"); }

        // TODO: Deal with alrady existing user
        var user = await GraphUserService.CreateUser(email, b2cSettings.Value.Domain, _graph);
        if (user is null) { return Results.BadRequest("Failed to create graph user"); }

        var authContainer = _cosmos.GetContainer("Senate", "Auth");
        var userAuth = new UserAuth()
        {
            ObjectId = Guid.Parse(user.Id),
            Scopes = scopes
        };

        var auth = await authContainer.CreateItemAsync(userAuth);
        if (auth.StatusCode != HttpStatusCode.Created)
        {
            return Results.BadRequest("Failed to create user auth record");
        }

        try
        {
            var inviteResult = await CreateInviteLink(email, req, b2cSettings);
            var mailClient = MailManager.GetClient(appSettings.Value.SendgridApiKey);
            await MailManager.SendInvitation(mailClient, email, inviteResult);

            return Results.Ok();
        }
        catch (Exception ex) 
        {
            Console.WriteLine(ex.Message);
            return Results.BadRequest("Failed to create invite link");
        }
    }
}

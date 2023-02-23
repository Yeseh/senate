using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Graph;
using Newtonsoft.Json;
using System.Net;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Web;
using LanguageExt;
using LanguageExt.Common;
using LanguageExt.Pipes;

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

        // Invites
        //app.MapGet(basePath + "/invite", CreateInviteLink).WithOpenApi();
        app.MapGet(basePath + "/invite", RedeemInviteLink).WithOpenApi();
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
        IConfiguration _config,
        HttpContext context)
    {
        var b2cSection = _config.GetSection("AzureAdB2C");

        var nonce = Guid.NewGuid().ToString("n");
        var tenantName = b2cSection["Domain"];
        var clientId = b2cSection["ClientId"];
        var hostName = b2cSection["Instance"]!.Split("https://")[1];
        var redirectUri = HttpUtility.UrlEncode("https://jwt.ms");
        var ex = new Exception("Failed to create invite token");

        string genUrl = string.Format("https://{0}/{1}/B2C_1A_GenerateInvite/oauth2/v2.0/authorize?client_id={2}&nonce={3}"
             + "&redirect_uri={4}&scope=openid&response_type=id_token&disable_cache=true&login_hint={5}"
              , hostName, tenantName, clientId, nonce, redirectUri, email);

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

    private static IResult GetUserByOid([FromRoute] Guid oid, HttpContext ctx)
    {
        var isPresent = userDb.TryGetValue(oid, out var user); 
        if (!isPresent) { return Results.NotFound(); }

        return Results.Ok(user);
    }

    private static IResult GetUserAuth(
        [FromRoute] Guid oid, 
        HttpContext ctx,
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
        IConfiguration _config,
        HttpRequest req,
        HttpContext ctx,
        CosmosClient _cosmos,
        GraphServiceClient _graph)
    {
        var scopes = new List<string>();
        if (permission.Contains("read")) { scopes.Add("Auth.Read"); }
        if (permission.Contains("write")) { scopes.Add("Auth.Write"); }

        // TODO: Deal with alrady existing user
        var issuer = _config.GetSection("AzureAdB2C")["Domain"]!;
        var user = await GraphUserService.CreateUser(email, issuer, _graph);
        if (user is null) { return Results.BadRequest("Failed to create graph user"); }

        var authContainer = _cosmos.GetContainer("Senate", "Auth");
        var userAuth = new UserAuth()
        {
            ObjectId = Guid.Parse(user.Id),
            Scopes = scopes
        };

        var auth = await authContainer.CreateItemAsync(userAuth);
        if (auth.StatusCode != System.Net.HttpStatusCode.Created)
        {
            return Results.BadRequest("Failed to create user auth record");
        }

        var returnUri = $"/user/{user.Id}";

        //var inviteRedeemUrl = $"https://yesehdevb2c.b2clogin.com/yesehdevb2c.onmicrosoft.com/oauth2/v2.0/authorize?p=B2C_1A_MAGICLINKSISU&client_id=bf050912-36cc-4ff6-9166-dad0068cea4e&nonce=defaultNonce&redirect_uri=https%3A%2F%2Fjwt.ms&scope=openid&response_type=id_token&prompt=login&invite_id={invite.Id}";
        var res = new CreateUserResponse()
        {
            User = user,
            UserAuth = auth.Resource,
        };

        try
        {
            var inviteResult = await CreateInviteLink(email, req, _config, ctx);
            var mailClient = MailManager.GetClient(_config.GetValue<string>("SendgridAPIKey"));
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

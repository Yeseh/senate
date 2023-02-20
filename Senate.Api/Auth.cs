using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Graph;
using Newtonsoft.Json;

namespace Senate.Api;

public record User(Guid ObjectId, string Email, bool IsEmailVerified = false, bool IsInviteAccepted = false);

public record Invite
{
    [JsonProperty(PropertyName = "id")]
    public Guid Id { get; set; }

    [JsonProperty(PropertyName = "email")]
    public string Email { get; set; } = string.Empty;

    [JsonProperty(PropertyName = "expiry")]
    public DateTime Expiry { get; set; }

    [JsonProperty(PropertyName = "redeemed")]
    public bool Redeemed { get; set; } = false;
}

public class CreateUserResponse
{
    public User User { get; set;  }

    public Invite Invite { get; set; }

    public string InviteUrl { get; set; }
}

public static class Auth
{
    private readonly static Dictionary<Guid, User> userDb = new();

    private readonly static Dictionary<Guid, Invite> inviteDb = new();

    public static void MapAuthModule(this Microsoft.AspNetCore.Builder.WebApplication app, string basePath)
    {
        // User
        app.MapPost(basePath + "/user", CreateUser).WithOpenApi().RequireAuthorization();
        app.MapGet(basePath + "/user/{oid}", GetUserByOid).WithOpenApi().RequireAuthorization();
        app.MapPut(basePath + "/user/{oid}", GetUserByOid).WithOpenApi().RequireAuthorization();

        // Invites
        app.MapGet(basePath + "/invites", GetInvites).WithOpenApi().RequireAuthorization();
        app.MapGet(basePath + "/invites/{id}", GetInvite).WithOpenApi().RequireAuthorization();
        app.MapPut(basePath + "/invites/{id}", RedeemInvite).WithOpenApi().RequireAuthorization();
    }

    private static IResult GetUserByOid([FromRoute] Guid oid, HttpContext ctx)
    {
        var isPresent = userDb.TryGetValue(oid, out var user); 
        if (!isPresent) { return Results.NotFound(); }

        return Results.Ok(user);
    }

    private static async Task<IResult> GetInvites(HttpContext ctx, CosmosClient _cosmos)
    {
        var container = _cosmos.GetContainer("Senate", "Invites");
        var query = new QueryDefinition("select * from c where c.redeemed = false");
        var result = container.GetItemQueryIterator<Invite>(query);

        List<Invite> toReturn = new();
        while (result.HasMoreResults)
        {
            var items = await result.ReadNextAsync();
            foreach (var i in items)
            {
                toReturn.Add(i);
            }
        }

        return Results.Ok(toReturn);
    }

    private static IResult GetInvite(Guid inviteId, HttpContext ctx, CosmosClient _cosmos)
    {
        var now = DateTime.UtcNow;
        var container = _cosmos.GetContainer("Senate", "Invites");
        var invites = container.GetItemLinqQueryable<Invite>()
            .Where(i => i.Id == inviteId
                && i.Expiry > now
                && !i.Redeemed);

        if (!invites.Any()) { return Results.NotFound(); }

        return Results.Ok(invites.First());
    }

    private static async Task<IResult> RedeemInvite(Guid inviteId, HttpContext ctx, CosmosClient _cosmos)
    {
        var now = DateTime.UtcNow;
        var container = _cosmos.GetContainer("Senate", "Invites");
        var invites = container.GetItemLinqQueryable<Invite>().Where(
            i => i.Id.Equals(inviteId) 
                && i.Expiry > now 
                && !i.Redeemed);

        if (!invites.Any()) { return Results.NotFound(); }

        var existing = invites.First();
        var updated = new Invite()
        {
            Id = existing.Id,
            Email = existing.Email,
            Expiry = existing.Expiry,
            Redeemed = true,
        };
        var updateRes = await container.UpsertItemAsync(updated);
        if (updateRes.StatusCode != System.Net.HttpStatusCode.OK) { return Results.BadRequest(); }

        return Results.Ok();
    }

    private static async Task<IResult> CreateUser(string email, HttpContext ctx, CosmosClient _cosmos)
    {
        User newUser = new(Guid.NewGuid(), email);

        var success = userDb.TryAdd(newUser.ObjectId, newUser);
        if (!success) { return Results.BadRequest(); }

        Invite invite = new()
        {
            Id = Guid.NewGuid(),
            Email = email,
            Expiry = DateTime.UtcNow.AddHours(48),
            Redeemed = false
        };

        var container = _cosmos.GetContainer("Senate", "Invites");
        var inviteRes = await container.CreateItemAsync(invite);
        if (inviteRes.StatusCode != System.Net.HttpStatusCode.Created) { return Results.BadRequest(); }   

        var returnUri = $"/user/{newUser.ObjectId}";

        var inviteRedeemUrl = $"https://yesehdevb2c.b2clogin.com/yesehdevb2c.onmicrosoft.com/oauth2/v2.0/authorize?p=B2C_1A_MAGICLINKSISU&client_id=bf050912-36cc-4ff6-9166-dad0068cea4e&nonce=defaultNonce&redirect_uri=https%3A%2F%2Fjwt.ms&scope=openid&response_type=id_token&prompt=login&invite_id={invite.Id}";
        var res = new CreateUserResponse()
        {
            User = newUser,
            Invite = inviteRes.Resource,
            InviteUrl = inviteRedeemUrl
        };

        return Results.Created(returnUri, res);
    }
}

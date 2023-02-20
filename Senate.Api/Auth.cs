using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace Senate.Api;

public record User(Guid ObjectId, string Email, bool IsEmailVerified = false, bool IsInviteAccepted = false);

public record Invite(Guid Id, string Email, DateTime Expiry, bool Redeemed = false);

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

    public static void MapAuthModule(this WebApplication app, string basePath)
    {
        app.MapPost(basePath + "/user", CreateUser).WithOpenApi().RequireAuthorization();
        app.MapGet(basePath + "/user/{oid}", GetUserByOid).WithOpenApi().RequireAuthorization();
        app.MapGet(basePath + "/invite/{id}", GetInvite).WithOpenApi().RequireAuthorization();
        app.MapPut(basePath + "/invite/{id}", RedeemInvite).WithOpenApi().RequireAuthorization();
    }

    private static IResult GetUserByOid([FromRoute] Guid oid, HttpContext ctx)
    {
        var isPresent = userDb.TryGetValue(oid, out var user); 
        if (!isPresent) { return Results.NotFound(); }

        return Results.Ok(user);
    }

    private static IResult GetInvite(Guid inviteId, HttpContext ctx)
    {
        var now = DateTime.UtcNow;
        var invites = inviteDb.Where(
            pair => pair.Key.Equals(inviteId) 
                && pair.Value.Expiry > now 
                && !pair.Value.Redeemed);

        if (!invites.Any()) { return Results.NotFound(); }

        return Results.Ok(invites.First().Value);
    }

    private static IResult RedeemInvite(Guid inviteId, HttpContext ctx)
    {
        var now = DateTime.UtcNow;
        var invites = inviteDb.Where(
            pair => pair.Key.Equals(inviteId) 
                && pair.Value.Expiry > now 
                && !pair.Value.Redeemed);

        if (!invites.Any()) { return Results.NotFound(); }

        var existing = invites.First().Value;
        var updated = new Invite(existing.Id, existing.Email, existing.Expiry, true);
        inviteDb[existing.Id]= updated;

        return Results.Ok();
    }

    private static IResult CreateUser(string email, HttpContext ctx)
    {
        User newUser = new(Guid.NewGuid(), email);

        var success = userDb.TryAdd(newUser.ObjectId, newUser);
        if (!success) { return Results.BadRequest(); }

        Invite invite = new(Guid.NewGuid(), email, DateTime.UtcNow.AddHours(48));
        success = inviteDb.TryAdd(invite.Id, invite);
        if (!success) { return Results.BadRequest(); }

        var returnUri = $"/user/{newUser.ObjectId}";

        var inviteRedeemUrl = $"https://yesehdevb2c.b2clogin.com/yesehdevb2c.onmicrosoft.com/oauth2/v2.0/authorize?p=B2C_1A_MAGICLINKSISU&client_id=bf050912-36cc-4ff6-9166-dad0068cea4e&nonce=defaultNonce&redirect_uri=https%3A%2F%2Fjwt.ms&scope=openid&response_type=id_token&prompt=login&invite_id={invite.Id}";
        var res = new CreateUserResponse()
        {
            User = newUser,
            Invite = invite,
            InviteUrl = inviteRedeemUrl
        };

        return Results.Created(returnUri, res);
    }
}

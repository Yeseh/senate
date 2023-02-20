namespace Senate.Api;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Graph;
using Senate.Api.Helpers;

public class UserModel : User
{
    [JsonPropertyName("password")]
    public string Password { get; set; }

    public void SetB2CProfile(string TenantName)
    {
        this.PasswordProfile = new PasswordProfile
        {
            ForceChangePasswordNextSignIn = false,
            Password = this.Password,
            ODataType = null
        };
        this.PasswordPolicies = "DisablePasswordExpiration,DisableStrongPassword";
        this.Password = null;
        this.ODataType = null;

        foreach (var item in this.Identities)
        {
            if (item.SignInType == "emailAddress" || item.SignInType == "userName")
            {
                item.Issuer = TenantName;
            }
        }
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this);
    }
}

public class GraphUserService
{
    public static async Task<List<User>> ListUsers(GraphServiceClient graphClient)
    {
        List<User> users= new();

        var graphUsers = await graphClient.Users
            .Request()
            .Select(e => new
            {
                e.DisplayName,
                e.Id,
                e.Identities
            })
            .GetAsync();

        var pageIterator = PageIterator<User>
            .CreatePageIterator(
                graphClient,
                graphUsers,
                user => { users.Add(user); return true; },
                req => req
            );

        await pageIterator.IterateAsync();

        return users;
    }
    public static async Task<User?> GetUserById(string userId, GraphServiceClient graphClient)
    {
        var result = await graphClient.Users[userId]
            .Request()
            .Select(e => new
            {
                e.DisplayName,
                e.Id,
                e.Identities
            })
            .GetAsync();

        return result;
    }

    public static async Task<User?> CreateUser(string email, GraphServiceClient graphClient)
    {
        var user = new User
        {
            Mail = email,
            PasswordPolicies = "DisablePasswordExpiration,DisableStrongPassword",
            PasswordProfile = new PasswordProfile
            {
                ForceChangePasswordNextSignIn = true,
                Password = PasswordHelper.GenerateNewPassword(10, 10, 10),
            }
        };

        // TODO: Just return existing user if it exists for now
        var existing = await GetUserByEmail(email, graphClient);
        if (existing != null) { return user;  }

        var result = await graphClient.Users
            .Request()
            .AddAsync(user);

        return result;
    }

    public static async Task<User?> GetUserByEmail(string email, GraphServiceClient graphClient)
    {
        var result = await graphClient.Users
            .Request()
            .Filter($"identities/any(c:c/email eq '{email}'")
            .Select(e => new
            {
                e.DisplayName,
                e.Id,
                e.Identities
            })
            .GetAsync();

        return result.FirstOrDefault();
    }

    public static async Task DeleteUserById(string userId, GraphServiceClient graphClient)
    {
        await graphClient.Users[userId]
            .Request()
            .DeleteAsync();
    }

    public static async Task SetPasswordByUserId(string userId, GraphServiceClient graphClient)
    {
        string password = PasswordHelper.GenerateNewPassword(10, 10, 10);

        var user = new User
        {
            PasswordPolicies = "DisablePasswordExpiration,DisableStrongPassword",
            PasswordProfile = new PasswordProfile
            {
                ForceChangePasswordNextSignIn = true,
                Password = password,
            }
        };

        await graphClient.Users[userId]
           .Request()
           .UpdateAsync(user);
    }
}

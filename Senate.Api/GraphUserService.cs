namespace Senate.Api;

using Microsoft.Graph;
using Senate.Api.Helpers;

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

    // TODO: This is pretty naive
    private static string GetUPNFromEmail(string email)
    {
        var pn = email.Replace("@", "_");
        return $"{pn}@yesehdevb2c.onmicrosoft.com";
    }

    public static async Task<User?> CreateUser(string email, string issuer, GraphServiceClient graphClient)
    {
        var user = new User
        {
            DisplayName = email,
            Mail = email,
            UserPrincipalName = GetUPNFromEmail(email),
            PasswordPolicies = "DisablePasswordExpiration,DisableStrongPassword",
            MailNickname = "Primary",
            AccountEnabled = false,
            Identities = new[]
            {
                new ObjectIdentity
                {
                    SignInType = "emailAddress",
                    Issuer = issuer,
                    IssuerAssignedId = email
                }
            },
            PasswordProfile = new PasswordProfile
            {
                ForceChangePasswordNextSignIn = true,
                Password = PasswordHelper.GenerateNewPassword(10, 10, 10),
            }
        };

        // TODO: Just return existing user if it exists for now
        var existing = await GetUserByEmail(email, graphClient);
        if (existing != null) { return existing;  }

        var result = await graphClient.Users
            .Request()
            .AddAsync(user);

        return result;
    }

    public static async Task<User?> GetUserByEmail(string email, GraphServiceClient graphClient)
    {
        var result = await graphClient.Users
            .Request()
            .Filter($"mail eq '{email}'")
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

using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components;

namespace Senate.Portal;

public class CustomAuthorizationMessageHandler : AuthorizationMessageHandler
{
    private static string readScope = @"https://yesehdevb2c.onmicrosoft.com/senate/api/read";
    private static string writeScope = @"https://yesehdevb2c.onmicrosoft.com/senate/api/write";

    public CustomAuthorizationMessageHandler(IAccessTokenProvider provider,
        NavigationManager navigationManager)
        : base(provider, navigationManager)
    {
        ConfigureHandler(
            authorizedUrls: new[] { "http://localhost:3000", "https://localhost:3001", "http://localhost:8000", "https://localhost:8001" },
            scopes: new[] { readScope, writeScope });
    }
}


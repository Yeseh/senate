using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Net.Http;
using Senate.Portal;

var scopes = new string[]
{
    "https://yesehdevb2c.onmicrosoft.com/senate/api/read",
    "https://yesehdevb2c.onmicrosoft.com/senate/api/write",
};

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAdB2C", options.ProviderOptions.Authentication);
    _ = options.ProviderOptions.DefaultAccessTokenScopes.Concat(scopes);
});

builder.Services.AddScoped<CustomAuthorizationMessageHandler>();

builder.Services.AddHttpClient(
    "SenateAPI",  client => client.BaseAddress = new Uri("https://localhost:8001"))
    .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();

builder.Services.AddScoped(
    sp => sp.GetRequiredService<IHttpClientFactory>()
            .CreateClient("SenateAPI"));

await builder.Build().RunAsync();

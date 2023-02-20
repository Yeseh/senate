using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Net.Http;
using Senate.Portal;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

//builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddMsalAuthentication(options =>
{
    var clientId = builder.Configuration.GetSection("AzureAdB2C").GetValue<string>("ClientId");
    builder.Configuration.Bind("AzureAdB2C", options.ProviderOptions.Authentication);
    options.ProviderOptions.DefaultAccessTokenScopes.Add("https://yesehdevb2c.onmicrosoft.com/senate/api/Auth.Read");
    options.ProviderOptions.DefaultAccessTokenScopes.Add("https://yesehdevb2c.onmicrosoft.com/senate/api/Auth.Write");
    options.ProviderOptions.LoginMode = "redirect";
});

builder.Services.AddTransient<CustomAuthorizationMessageHandler>();

if (builder.HostEnvironment.IsDevelopment())
{
    builder.Services.AddHttpClient(
        "SenateAPI",  client => client.BaseAddress = new Uri("https://localhost:8001"))
        .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();
}
else
{
    builder.Services.AddHttpClient(
        "SenateAPI",  client => client.BaseAddress = new Uri("https://app-senate-api.azurewebsites.net"))
        .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();
}

builder.Services.AddScoped(
    sp => sp.GetRequiredService<IHttpClientFactory>()
            .CreateClient("SenateAPI"));

await builder.Build().RunAsync();

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder; 
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.Resource;
using Microsoft.Azure.Cosmos;
using Senate.Api;
using Azure.Identity;
using Microsoft.Graph;

var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args);
var localHostCors = "localHostAccess";
var productionCors = "productionAccess";
var b2cSection = builder.Configuration.GetSection("AzureAdB2C");

// Add services to the container.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(b2cSection);

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("ReadScope",
        p => p.Requirements.Add(new ScopesRequirement(builder.Configuration["ReadScope"])));
});

builder.Services.AddSingleton(s =>
{
    var scopes = new[] { "https://graph.microsoft.com/.default" };
    var cred = new ClientSecretCredential(
        b2cSection["TenantId"],
        b2cSection["ClientId"],
        b2cSection["ClientSecret"]);

    return new GraphServiceClient(cred, scopes);
});

builder.Services.AddSingleton(s =>
{
    var connString = builder.Configuration.GetValue<string>("ConnectionStrings:CosmosDB");
    return new CosmosClient(connString);
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o =>
{
    if (builder.Environment.IsDevelopment())
    {
        o.AddPolicy(localHostCors, b => {
            b.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
         });
    }
    else
    {
        o.AddPolicy(productionCors, b =>
        {
            var allowedOrigins = builder.Configuration
                .GetValue<string[]>("AllowedHosts");

            b.AllowAnyMethod().AllowAnyHeader();
            b.WithOrigins(allowedOrigins ?? Array.Empty<string>());
        });
    }
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseCors(localHostCors);
}
else
{
    app.UseCors(productionCors);
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

var scopeRequiredByApi = new string[] { "Auth.Read", "Auth.Write" }; //app.Configuration.GetSection("AzureAdB2C").GetValue<string[]>("Scopes") ?? Array.Empty<string>(); 
var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", (HttpContext httpContext) =>
{
    httpContext.VerifyUserHasAnyAcceptedScope(scopeRequiredByApi);

    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi()
.RequireAuthorization();

app.MapAuthModule("/auth");

var cosmosdb = app.Services.GetRequiredService<CosmosClient>();
await cosmosdb.CreateDatabaseIfNotExistsAsync("Senate");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

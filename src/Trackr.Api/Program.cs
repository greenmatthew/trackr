using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// TLS is terminated upstream (nginx in front of us, and the user's own reverse
// proxy in front of that), so we speak plain HTTP and never redirect. Instead we
// trust the forwarded headers, which milestone 2 needs in order to mark the
// Identity session cookie Secure. KnownProxies/KnownNetworks are cleared because
// container IPs on the compose network are not stable; the backend is not
// reachable from outside that network.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var connectionString = builder.Configuration.GetConnectionString("Trackr")
    ?? throw new InvalidOperationException(
        "No connection string named 'Trackr'. Set ConnectionStrings__Trackr in the environment.");

builder.Services.AddDbContext<TrackrDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TrackrDbContext>("database");

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

#if DEBUG
// Dev convenience only: serve the Blazor app from this process so `dotnet watch`
// gives a single-origin dev server with hot reload, matching how nginx serves it
// in production. Compiled out of Release builds - see Trackr.Api.csproj.
if (app.Environment.IsDevelopment())
{
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();
}
#endif

app.MapHealthEndpoints();
app.MapHealthChecks("/api/health/ready");

#if DEBUG
if (app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}
#endif

app.Run();

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Trackr.Web;
using Trackr.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddAuthorizationCore();

// Supplies the cascading AuthenticationState that AuthorizeView and AuthorizeRouteView
// consume. Note App.razor therefore does NOT also wrap the router in
// <CascadingAuthenticationState> - it is one or the other, not both.
builder.Services.AddCascadingAuthenticationState();

// Registered twice on purpose: once as the concrete type, so the HTTP handler and
// AuthClient can call Invalidate(), and once as the abstraction Blazor resolves.
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<CookieAuthenticationStateProvider>());

builder.Services.AddScoped<AuthClient>();

// No Microsoft.Extensions.Http here: one named client is not worth another package in a
// trimmed WASM bundle. Same-origin requests carry the session cookie by default
// (fetch uses credentials: "same-origin"), so nothing extra is needed to authenticate.
builder.Services.AddScoped(sp => new HttpClient(
    new UnauthorizedResponseHandler(sp) { InnerHandler = new HttpClientHandler() })
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

await builder.Build().RunAsync();

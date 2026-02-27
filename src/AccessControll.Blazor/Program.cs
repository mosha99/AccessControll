using Blazored.LocalStorage;
using AccessControll.Blazor;
using AccessControll.Blazor.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// When served by the API (production), BaseAddress = same origin → no CORS needed.
// In dev (standalone Blazor server on a different port), override via appsettings.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
                 ?? builder.HostEnvironment.BaseAddress;

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthStateProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IDoorService, DoorService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<ILogService, LogService>();
builder.Services.AddScoped<IPanelService, PanelService>();
builder.Services.AddScoped<DoorHubService>();
builder.Services.AddScoped<IStationService, StationService>();

await builder.Build().RunAsync();

using AccessControll.API.Hubs;
using AccessControll.Domain.Interfaces;
using AccessControll.Hardware;
using AccessControll.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Device.Gpio;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/mediarsystem-.log", rollingInterval: RollingInterval.Day));

// Infrastructure: EF, Identity, repositories, JWT
builder.Services.AddInfrastructure(builder.Configuration);

// MediatR — scan Application assembly
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(
        typeof(AccessControll.Application.Auth.LoginCommandHandler).Assembly
    );
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AccessControll API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Token",
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(opt =>
    opt.AddPolicy("BlazorPolicy", p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddSignalR(options => options.EnableDetailedErrors = true);

// ── Hardware services (multi-station) ──────────────────────────────────────
// Oled: renders display frames (font rendering + optional local I2C)
builder.Services.AddSingleton<Oled>();

// StationConnectionManager: tracks MAC ↔ SignalR ConnectionId for all stations
builder.Services.AddSingleton<StationConnectionManager>();

// StationSessionManager: owns one StationSession per connected station
builder.Services.AddSingleton<StationSessionManager>();

// Physical door lock control (GPIO/I2C relay — runs on server hardware)
builder.Services.AddSingleton<GpioController>(_ =>
{
    try { return new GpioController(); }
    catch { return null!; }
});
builder.Services.AddSingleton<IPhysicalPortService, PhysicalPortService>();
// ──────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseWebSockets();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// NOTE: HttpsRedirection is intentionally omitted — ESP8266 connects via HTTP WebSocket
app.UseCors("BlazorPolicy");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<DoorHub>("/hubs/door");
// WebSockets-only transport — ESP8266 connects directly without negotiate step
app.MapHub<HardwareHub>("/hardwareHub", options =>
{
    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
});

// Auto-migrate and seed on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AccessControll.Infrastructure.Data.ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AccessControll.Domain.Entities.ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
    await AccessControll.Infrastructure.Services.DatabaseSeeder.SeedAsync(app.Environment.IsDevelopment(), db, userManager, roleManager);
}

// ── Wire display pipeline (same pattern as the old OnBuffer approach) ──────
var oled              = app.Services.GetRequiredService<Oled>();
var sessionManager    = app.Services.GetRequiredService<StationSessionManager>();
var connectionManager = app.Services.GetRequiredService<StationConnectionManager>();
var hubContext        = app.Services.GetRequiredService<IHubContext<HardwareHub>>();

// Init font (tries local I2C; silently skips if unavailable)
oled.Init();

// Tell StationSessionManager how to send display frames to a specific station.
// Each session wraps this with its own MAC so messages are targeted, not broadcast.
sessionManager.SetDisplaySender(async (mac, buffer) =>
{
    var connId = connectionManager.GetConnectionId(mac);
    Console.WriteLine($"[DISP] SendDisplay mac={mac} connId={connId ?? "NULL"} bufLen={buffer.Length}");
    if (connId is null) return;
    var base64 = Convert.ToBase64String(buffer);
    Console.WriteLine($"[DISP] Calling SendAsync RenderDisplay to {connId} b64Len={base64.Length}");
    await hubContext.Clients.Client(connId).SendAsync("RenderDisplay", base64);
    Console.WriteLine($"[DISP] SendAsync completed for {connId}");
});
// ──────────────────────────────────────────────────────────────────────────

Console.WriteLine("--- Server Ready ---");
app.Run();

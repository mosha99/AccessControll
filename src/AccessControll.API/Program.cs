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

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors  = true;
    options.KeepAliveInterval     = TimeSpan.FromSeconds(10);  // ping هر 10 ثانیه
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(20);  // اگه 20 ثانیه جواب نداد → disconnect
});

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

// Ed25519 key pair — generated once on first run, persisted to server.ed25519
builder.Services.AddSingleton<ServerKeyService>();

builder.Services.AddSingleton<ProvisioningService>();

// Station I2C relay — singleton with callback wired after app.Build()
builder.Services.AddSingleton<StationRelayService>();
builder.Services.AddSingleton<IStationRelayService>(sp => sp.GetRequiredService<StationRelayService>());
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
var keyService        = app.Services.GetRequiredService<ServerKeyService>();

// Init font (tries local I2C; silently skips if unavailable)
oled.Init();

// Tell StationSessionManager how to send display frames to a specific station.
// Each session wraps this with its own MAC so messages are targeted, not broadcast.
sessionManager.SetDisplaySender(async (mac, buffer) =>
{
    var connId = connectionManager.GetConnectionId(mac);
    if (connId is null) return;
    var base64 = Convert.ToBase64String(buffer);
    await hubContext.Clients.Client(connId).SendAsync("RenderDisplay", base64);
});

// Wire I2C relay pipeline — same callback pattern as display sender.
// All control messages are P-256 signed with a per-connection seqno to prevent forgery and replay.
var relayService = app.Services.GetRequiredService<StationRelayService>();

relayService.SetOpenSender(async (mac, i2cAddress, pin, durationMs, isActiveLow) =>
{
    var connId = connectionManager.GetConnectionId(mac);
    if (connId is null) return;

    var seqno   = connectionManager.NextSeqno(connId);
    var alBit   = isActiveLow ? 1 : 0;
    var sigData = System.Text.Encoding.UTF8.GetBytes($"OpenDoor|{i2cAddress}|{pin}|{durationMs}|{alBit}|{seqno}");
    var sigHex  = Convert.ToHexString(keyService.Sign(sigData)).ToLowerInvariant();

    await hubContext.Clients.Client(connId).SendAsync("OpenDoor", i2cAddress, pin, durationMs, isActiveLow, seqno, sigHex);
});

relayService.SetCloseSender(async (mac, i2cAddress, pin, isActiveLow) =>
{
    var connId = connectionManager.GetConnectionId(mac);
    if (connId is null) return;

    var seqno   = connectionManager.NextSeqno(connId);
    var alBit   = isActiveLow ? 1 : 0;
    var sigData = System.Text.Encoding.UTF8.GetBytes($"CloseDoor|{i2cAddress}|{pin}|{alBit}|{seqno}");
    var sigHex  = Convert.ToHexString(keyService.Sign(sigData)).ToLowerInvariant();

    await hubContext.Clients.Client(connId).SendAsync("CloseDoor", i2cAddress, pin, isActiveLow, seqno, sigHex);
});
// ──────────────────────────────────────────────────────────────────────────

Console.WriteLine("--- Server Ready ---");
app.Run();

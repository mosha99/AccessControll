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

//// Serilog
//builder.Host.UseSerilog((ctx, lc) => lc
//    .ReadFrom.Configuration(ctx.Configuration)
//    .WriteTo.Console()
//    .WriteTo.File("logs/mediarsystem-.log", rollingInterval: RollingInterval.Day));

//builder.Services.AddInfrastructure(builder.Configuration);

//// MediatR 12 — scan all application assemblies
//builder.Services.AddMediatR(cfg =>
//{
//    cfg.RegisterServicesFromAssemblies(
//        typeof(AccessControll.Application.Auth.LoginCommandHandler).Assembly
//    );
//});

//builder.Services.AddControllers();
//builder.Services.AddSignalR();
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddSwaggerGen(c =>
//{
//    c.SwaggerDoc("v1", new() { Title = "AccessControll Access Control API", Version = "v1" });
//    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
//    {
//        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
//        Description = "JWT Token",
//        Name = "Authorization",
//        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
//        Scheme = "Bearer"
//    });
//    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
//    {
//        {
//            new Microsoft.OpenApi.Models.OpenApiSecurityScheme { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }},
//            Array.Empty<string>()
//        }
//    });
//});

//builder.Services.AddCors(opt =>
//    opt.AddPolicy("BlazorPolicy", p => p
//        .AllowAnyOrigin()
//        .AllowAnyHeader()
//        .AllowAnyMethod()));


//if (false)
//{
//    builder.Services.AddSingleton<GpioController>();
//    builder.Services.AddSingleton<Keypad>();
//    builder.Services.AddSingleton<Oled>();
//    builder.Services.AddSingleton<IPhysicalPortService, PhysicalPortService>();
//    builder.Services.AddSingleton<PhysicalAuthService>();
//    builder.Services.AddSingleton<PhysicalDoorService>();

//    builder.Services.AddHostedService<KeyPadListener>();
//}  

builder.Services.AddSignalR();

// این رو اضافه کن
builder.Services.Configure<HubOptions>(options =>
{
    options.EnableDetailedErrors = true;
});

var app = builder.Build();

app.UseWebSockets(); // ← این مهمه

app.MapHub<HardwareHub>("/hardwareHub", options =>
{
    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
});
//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

//app.UseHttpsRedirection();
//app.UseCors("BlazorPolicy");
//app.UseAuthentication();
//app.UseAuthorization();
//app.MapControllers();
//app.MapHub<DoorHub>("/hubs/door");
//app.MapHub<HardwareHub>("/hardwareHub");

//// Auto-migrate and seed on startup
//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<AccessControll.Infrastructure.Data.ApplicationDbContext>();

//    await db.Database.MigrateAsync();


//    var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AccessControll.Domain.Entities.ApplicationUser>>();
//    var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<Microsoft.AspNetCore.Identity.IdentityRole>>();
//    await AccessControll.Infrastructure.Services.DatabaseSeeder.SeedAsync(app.Environment.IsDevelopment(), db, userManager, roleManager);

//}
//Console.WriteLine("--------------Start----------------");
app.Run();


// HardwareHub.cs
public class HardwareHub : Hub
{
    public HardwareHub()
    {
        
    }
    public override Task OnConnectedAsync()
    {
        return base.OnConnectedAsync();
    }
    // دریافت کلید از ESP
    public async Task SendKey(string key)
    {
        Console.WriteLine($"Key pressed: {key}");
        // هر کاری خواستی با کلید بکن
        await Clients.All.SendAsync("KeyReceived", key);
    }

    // فرستادن buffer به ESP
    public async Task SendDisplay(byte[] buffer)
    {
        string base64 = Convert.ToBase64String(buffer);
        await Clients.All.SendAsync("RenderDisplay", base64);
    }

    // روشن/خاموش
    public async Task SetPower(bool state)
    {
        await Clients.All.SendAsync("PowerState", state);
    }
}
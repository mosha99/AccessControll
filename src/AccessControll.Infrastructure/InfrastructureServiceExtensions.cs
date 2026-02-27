using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AccessControll.Application.Auth;
using AccessControll.Domain.Entities;
using AccessControll.Domain.Interfaces;
using AccessControll.Infrastructure.Authorization;
using AccessControll.Infrastructure.Data;
using AccessControll.Infrastructure.Repositories;
using AccessControll.Infrastructure.Services;

namespace AccessControll.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // EF Core
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection"));
            options.UseOpenIddict();
        });

        // Identity
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();

        // JWT Authentication
        var jwtSettings = configuration.GetSection("JwtSettings");
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!)),
                ValidateIssuer = true,
                ValidIssuer = jwtSettings["Issuer"],
                ValidateAudience = true,
                ValidAudience = jwtSettings["Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
            // SignalR WebSocket: token از query string خونده میشه
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var token = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) &&
                        context.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = token;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        // OpenIddict
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<ApplicationDbContext>())
            .AddServer(options =>
            {
                options.SetTokenEndpointUris("/connect/token")
                       .SetIntrospectionEndpointUris("/connect/introspect");
                options.AllowPasswordFlow()
                       .AllowRefreshTokenFlow();
                options.UseAspNetCore()
                       .EnableTokenEndpointPassthrough();

                options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        // Repositories
        services.AddScoped<IDoorRepository, DoorRepository>();
        services.AddScoped<IDoorAccessLogRepository, DoorAccessLogRepository>();
        services.AddScoped<IUserDoorPermissionRepository, UserDoorPermissionRepository>();
        services.AddScoped<IRolePanelPermissionRepository, RolePanelPermissionRepository>();

        // Authorization handler + panel-based named policies
        services.AddScoped<IAuthorizationHandler, PanelAccessHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy("panel:doors",    p => p.Requirements.Add(new PanelAccessRequirement(AppPanels.Doors)));
            options.AddPolicy("panel:logs",     p => p.Requirements.Add(new PanelAccessRequirement(AppPanels.Logs)));
            options.AddPolicy("panel:stations", p => p.Requirements.Add(new PanelAccessRequirement(AppPanels.Stations)));
            options.AddPolicy("panel:users",    p => p.Requirements.Add(new PanelAccessRequirement(AppPanels.Users)));
            options.AddPolicy("panel:roles",    p => p.Requirements.Add(new PanelAccessRequirement(AppPanels.Roles)));
        });

        // Services
        services.AddScoped<IJwtService, JwtService>();

        return services;
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace AccessControll.Blazor.Services;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record LoginResult(bool Succeeded, string? Token, bool RequiresTwoFactor, string? Error);
public record DoorDto(Guid Id, string Name, string Description, string Location, bool IsLocked, bool IsEnabled, string? HardwareId, DateTime CreatedAt);
public record DoorLogDto(Guid Id, string DoorName, string UserFullName, string UserEmail, DateTime AccessedAt, string Action, string Result, string? IpAddress, string? Notes);
public record UserDto(string Id, string Email, string FullName, bool IsActive, bool TwoFactorEnabled, DateTime CreatedAt, DateTime? LastLoginAt, List<string> Roles);
public record RoleDto(string Id, string Name, int UsersCount);
public record PagedResult<T>(IEnumerable<T> Items, int Total, int Page, int PageSize);

// ── Auth Service ──────────────────────────────────────────────────────────────

public interface IAuthService
{
    Task<LoginResult> LoginAsync(string email, string password, string? twoFactorCode = null);
    Task LogoutAsync();
    Task<bool> Setup2FAAsync();
    Task<bool> Verify2FAAsync(string code);
}

public class AuthService : IAuthService
{
    private readonly HttpClient _http;
    private readonly JwtAuthStateProvider _authState;

    public AuthService(HttpClient http, AuthenticationStateProvider authState)
    {
        _http = http;
        _authState = (JwtAuthStateProvider)authState;
    }

    public async Task<LoginResult> LoginAsync(string email, string password, string? twoFactorCode = null)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new { email, password, twoFactorCode });
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("requiresTwoFactor", out var tf) && tf.GetBoolean())
                return new LoginResult(false, null, true, null);
            var token = json.GetProperty("token").GetString()!;
            await _authState.NotifyLogin(token);
            return new LoginResult(true, token, false, null);
        }
        var err = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new LoginResult(false, null, false, err.TryGetProperty("message", out var m) ? m.GetString() : "خطا");
    }

    public async Task LogoutAsync()
    {
        await _http.PostAsync("api/auth/logout", null);
        await _authState.NotifyLogout();
    }

    public async Task<bool> Setup2FAAsync()
    {
        var response = await _http.PostAsync("api/auth/2fa/setup", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> Verify2FAAsync(string code)
    {
        var response = await _http.PostAsJsonAsync("api/auth/2fa/verify", new { code });
        return response.IsSuccessStatusCode;
    }
}

// ── Door Service ──────────────────────────────────────────────────────────────

public interface IDoorService
{
    Task<List<DoorDto>> GetAllAsync();
    Task<DoorDto?> GetByIdAsync(Guid id);
    Task<DoorDto?> CreateAsync(string name, string description, string location, string? hardwareId);
    Task<DoorDto?> UpdateAsync(Guid id, string name, string description, string location, bool isEnabled, string? hardwareId);
    Task DeleteAsync(Guid id);
    Task<(bool success, string message)> ControlDoorAsync(Guid doorId, bool lock_);
    Task<PagedResult<DoorLogDto>> GetLogsAsync(Guid? doorId = null, string? userId = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 20);
}

public class DoorService : IDoorService
{
    private readonly HttpClient _http;
    public DoorService(HttpClient http) => _http = http;

    public async Task<List<DoorDto>> GetAllAsync() =>
        await _http.GetFromJsonAsync<List<DoorDto>>("api/doors") ?? new();

    public async Task<DoorDto?> GetByIdAsync(Guid id) =>
        await _http.GetFromJsonAsync<DoorDto>($"api/doors/{id}");

    public async Task<DoorDto?> CreateAsync(string name, string description, string location, string? hardwareId)
    {
        var r = await _http.PostAsJsonAsync("api/doors", new { name, description, location, hardwareId });
        return r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<DoorDto>() : null;
    }

    public async Task<DoorDto?> UpdateAsync(Guid id, string name, string description, string location, bool isEnabled, string? hardwareId)
    {
        var r = await _http.PutAsJsonAsync($"api/doors/{id}", new { id, name, description, location, isEnabled, hardwareId });
        return r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<DoorDto>() : null;
    }

    public async Task DeleteAsync(Guid id) => await _http.DeleteAsync($"api/doors/{id}");

    public async Task<(bool success, string message)> ControlDoorAsync(Guid doorId, bool lock_)
    {
        var r = await _http.PostAsJsonAsync($"api/doors/{doorId}/control", new { lock_ });
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        var msg = json.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        return (r.IsSuccessStatusCode, msg);
    }

    public async Task<PagedResult<DoorLogDto>> GetLogsAsync(Guid? doorId = null, string? userId = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 20)
    {
        var url = $"api/doors/logs?page={page}&pageSize={pageSize}";
        if (doorId.HasValue) url += $"&doorId={doorId}";
        if (!string.IsNullOrEmpty(userId)) url += $"&userId={userId}";
        if (from.HasValue) url += $"&from={from:O}";
        if (to.HasValue) url += $"&to={to:O}";

        var json = await _http.GetFromJsonAsync<JsonElement>(url);
        var items = json.GetProperty("items").Deserialize<List<DoorLogDto>>() ?? new();
        var total = json.GetProperty("total").GetInt32();
        return new PagedResult<DoorLogDto>(items, total, page, pageSize);
    }
}

// ── User Service ──────────────────────────────────────────────────────────────

public interface IUserService
{
    Task<PagedResult<UserDto>> GetAllAsync(int page = 1, int pageSize = 20);
    Task<UserDto?> GetByIdAsync(string id);
    Task<(bool success, List<string> errors)> CreateAsync(string email, string fullName, string password, List<string> roles);
    Task<bool> UpdateAsync(string id, string fullName, bool isActive, List<string> roles);
    Task<bool> DeleteAsync(string id);
    Task<bool> ToggleActiveAsync(string id);
}

public class UserService : IUserService
{
    private readonly HttpClient _http;
    public UserService(HttpClient http) => _http = http;

    public async Task<PagedResult<UserDto>> GetAllAsync(int page = 1, int pageSize = 20)
    {
        // تعریف تنظیمات برای درک حروف کوچک در JSON
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, // یا استفاده از PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = await _http.GetFromJsonAsync<JsonElement>($"api/users?page={page}&pageSize={pageSize}");
        var users = json.GetProperty("users").Deserialize<List<UserDto>>(options) ?? new();
        var total = json.GetProperty("total").GetInt32();
        return new PagedResult<UserDto>(users, total, page, pageSize);
    }

    public async Task<UserDto?> GetByIdAsync(string id) =>
        await _http.GetFromJsonAsync<UserDto>($"api/users/{id}");

    public async Task<(bool success, List<string> errors)> CreateAsync(string email, string fullName, string password, List<string> roles)
    {
        var r = await _http.PostAsJsonAsync("api/users", new { email, fullName, password, roles });
        if (r.IsSuccessStatusCode) return (true, new());
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        var errors = json.TryGetProperty("errors", out var e) ? e.Deserialize<List<string>>() ?? new() : new();
        return (false, errors);
    }

    public async Task<bool> UpdateAsync(string id, string fullName, bool isActive, List<string> roles)
    {
        var r = await _http.PutAsJsonAsync($"api/users/{id}", new { id, fullName, isActive, roles });
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var r = await _http.DeleteAsync($"api/users/{id}");
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> ToggleActiveAsync(string id)
    {
        var r = await _http.PostAsync($"api/users/{id}/toggle-active", null);
        return r.IsSuccessStatusCode;
    }
}

// ── Role Service ──────────────────────────────────────────────────────────────

public interface IRoleService
{
    Task<List<RoleDto>> GetAllAsync();
    Task<bool> CreateAsync(string name);
    Task<bool> DeleteAsync(string id);
    Task<bool> AssignRoleAsync(string userId, string roleName);
    Task<bool> RemoveRoleAsync(string userId, string roleName);
}

public class RoleService : IRoleService
{
    private readonly HttpClient _http;
    public RoleService(HttpClient http) => _http = http;

    public async Task<List<RoleDto>> GetAllAsync() =>
        await _http.GetFromJsonAsync<List<RoleDto>>("api/roles") ?? new();

    public async Task<bool> CreateAsync(string name)
    {
        var r = await _http.PostAsJsonAsync("api/roles", new { name });
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var r = await _http.DeleteAsync($"api/roles/{id}");
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> AssignRoleAsync(string userId, string roleName)
    {
        var r = await _http.PostAsJsonAsync("api/roles/assign", new { userId, roleName });
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveRoleAsync(string userId, string roleName)
    {
        var r = await _http.PostAsJsonAsync("api/roles/remove", new { userId, roleName });
        return r.IsSuccessStatusCode;
    }
}

// ── Log Service ───────────────────────────────────────────────────────────────

public interface ILogService
{
    Task<PagedResult<DoorLogDto>> GetLogsAsync(Guid? doorId = null, string? userId = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 20);
}

public class LogService : ILogService
{
    private readonly IDoorService _doorService;
    public LogService(IDoorService doorService) => _doorService = doorService;

    public Task<PagedResult<DoorLogDto>> GetLogsAsync(Guid? doorId = null, string? userId = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 20) =>
        _doorService.GetLogsAsync(doorId, userId, from, to, page, pageSize);
}

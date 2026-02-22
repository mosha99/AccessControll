using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using AccessControll.Contracts.Auth;
using AccessControll.Contracts.Doors;
using AccessControll.Contracts.Users;
using AccessControll.Contracts.Roles;
using AccessControll.Contracts.Common;

namespace AccessControll.Blazor.Services;

// ── Shared JSON options ───────────────────────────────────────────────────────

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

// ── Auth Service ──────────────────────────────────────────────────────────────

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(string email, string password, string? twoFactorCode = null);
    Task<LoginResponse> Login2FAAsync(string totpCode);
    Task LogoutAsync();
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

    public async Task<LoginResponse> LoginAsync(string email, string password, string? twoFactorCode = null)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login", new LoginRequest(email, password, twoFactorCode));
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("requiresTwoFactor", out var tf) && tf.GetBoolean())
                return new LoginResponse(false, RequiresTwoFactor: true);
            var token = json.GetProperty("token").GetString()!;
            await _authState.NotifyLogin(token);
            return new LoginResponse(true, token);
        }
        var err = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new LoginResponse(false, Error: err.TryGetProperty("message", out var m) ? m.GetString() : "خطا در ورود");
    }

    public async Task<LoginResponse> Login2FAAsync(string totpCode)
    {
        var response = await _http.PostAsJsonAsync("api/auth/login/2fa", new Login2FARequest(totpCode));
        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var token = json.GetProperty("token").GetString()!;
            await _authState.NotifyLogin(token);
            return new LoginResponse(true, token);
        }
        var err = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new LoginResponse(false, Error: err.TryGetProperty("message", out var m) ? m.GetString() : "کد نامعتبر");
    }

    public async Task LogoutAsync()
    {
        try { await _http.PostAsync("api/auth/logout", null); } catch { }
        await _authState.NotifyLogout();
    }
}

// ── Profile Service ───────────────────────────────────────────────────────────

public interface IProfileService
{
    Task<ProfileDto?> GetAsync();
    Task<ProfileDto?> UpdateAsync(string fullName);
    Task<(bool Success, string? Error)> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<TwoFactorSetupResponse?> Setup2FAAsync();
    Task<bool> Verify2FAAsync(string code);
    Task<bool> Disable2FAAsync();
}

public class ProfileService : IProfileService
{
    private readonly HttpClient _http;
    public ProfileService(HttpClient http) => _http = http;

    public async Task<ProfileDto?> GetAsync() =>
        await _http.GetFromJsonAsync<ProfileDto>("api/profile", JsonOptions.CaseInsensitive);

    public async Task<ProfileDto?> UpdateAsync(string fullName)
    {
        var r = await _http.PutAsJsonAsync("api/profile", new UpdateProfileRequest(fullName));
        return r.IsSuccessStatusCode
            ? await r.Content.ReadFromJsonAsync<ProfileDto>(JsonOptions.CaseInsensitive)
            : null;
    }

    public async Task<(bool Success, string? Error)> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var r = await _http.PostAsJsonAsync("api/profile/change-password",
            new ChangePasswordRequest(currentPassword, newPassword));
        if (r.IsSuccessStatusCode) return (true, null);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (false, json.TryGetProperty("message", out var m) ? m.GetString() : "خطا در تغییر رمز");
    }

    public async Task<TwoFactorSetupResponse?> Setup2FAAsync()
    {
        var r = await _http.PostAsync("api/profile/2fa/setup", null);
        return r.IsSuccessStatusCode
            ? await r.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(JsonOptions.CaseInsensitive)
            : null;
    }

    public async Task<bool> Verify2FAAsync(string code)
    {
        var r = await _http.PostAsJsonAsync("api/profile/2fa/verify", new { code });
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> Disable2FAAsync()
    {
        var r = await _http.PostAsync("api/profile/2fa/disable", null);
        return r.IsSuccessStatusCode;
    }
}

// ── Door Service ──────────────────────────────────────────────────────────────

public interface IDoorService
{
    Task<List<DoorDto>> GetAllAsync();
    Task<DoorDto?> GetByIdAsync(Guid id);
    Task<DoorDto?> CreateAsync(string name, int code, string description, string location, string? hardwareId,
        string? stationMacAddress, string i2cAddress, string i2cPin, int durationMs, bool isMomentary);
    Task<DoorDto?> UpdateAsync(Guid id, string name, int code, string description, string location, bool isEnabled,
        string? hardwareId, string? stationMacAddress, string i2cAddress, string i2cPin, int durationMs, bool isMomentary);
    Task DeleteAsync(Guid id);
    Task<(bool Success, string Message)> ControlDoorAsync(Guid doorId, bool lockDoor);
    Task<PagedResult<DoorAccessLogDto>> GetLogsAsync(Guid? doorId = null, string? userId = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 25);
    Task<List<UserPermissionDto>> GetPermissionsAsync(Guid doorId);
    Task<bool> GrantPermissionAsync(Guid doorId, string userId, bool canOpen, bool canLock, TimeSpan? fromTime, TimeSpan? toTime);
    Task<bool> RevokePermissionAsync(Guid doorId, string userId);
}

public class DoorService : IDoorService
{
    private readonly HttpClient _http;
    public DoorService(HttpClient http) => _http = http;

    public async Task<List<DoorDto>> GetAllAsync() =>
        await _http.GetFromJsonAsync<List<DoorDto>>("api/doors", JsonOptions.CaseInsensitive) ?? new();

    public async Task<DoorDto?> GetByIdAsync(Guid id) =>
        await _http.GetFromJsonAsync<DoorDto>($"api/doors/{id}", JsonOptions.CaseInsensitive);

    public async Task<DoorDto?> CreateAsync(string name, int code, string description, string location, string? hardwareId,
        string? stationMacAddress, string i2cAddress, string i2cPin, int durationMs, bool isMomentary)
    {
        var r = await _http.PostAsJsonAsync("api/doors", new { name, code, description, location, hardwareId, stationMacAddress, i2cAddress, i2cPin, durationMs, isMomentary });
        return r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<DoorDto>(JsonOptions.CaseInsensitive) : null;
    }

    public async Task<DoorDto?> UpdateAsync(Guid id, string name, int code, string description, string location, bool isEnabled,
        string? hardwareId, string? stationMacAddress, string i2cAddress, string i2cPin, int durationMs, bool isMomentary)
    {
        var r = await _http.PutAsJsonAsync($"api/doors/{id}", new { id, name, code, description, location, isEnabled, hardwareId, stationMacAddress, i2cAddress, i2cPin, durationMs, isMomentary });
        return r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<DoorDto>(JsonOptions.CaseInsensitive) : null;
    }

    public async Task DeleteAsync(Guid id) => await _http.DeleteAsync($"api/doors/{id}");

    public async Task<(bool Success, string Message)> ControlDoorAsync(Guid doorId, bool lockDoor)
    {
        var r = await _http.PostAsJsonAsync($"api/doors/{doorId}/control", new { Lock = lockDoor });
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        var msg = json.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
        return (r.IsSuccessStatusCode, msg);
    }

    public async Task<PagedResult<DoorAccessLogDto>> GetLogsAsync(Guid? doorId = null, string? userId = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 25)
    {
        var url = $"api/doors/logs?page={page}&pageSize={pageSize}";
        if (doorId.HasValue) url += $"&doorId={doorId}";
        if (!string.IsNullOrEmpty(userId)) url += $"&userId={Uri.EscapeDataString(userId)}";
        if (from.HasValue) url += $"&from={from.Value:O}";
        if (to.HasValue) url += $"&to={to.Value:O}";

        var json = await _http.GetFromJsonAsync<JsonElement>(url);
        var items = json.GetProperty("items").Deserialize<List<DoorAccessLogDto>>(JsonOptions.CaseInsensitive) ?? new();
        var total = json.GetProperty("total").GetInt32();
        return new PagedResult<DoorAccessLogDto>(items, total, page, pageSize);
    }

    public async Task<List<UserPermissionDto>> GetPermissionsAsync(Guid doorId) =>
        await _http.GetFromJsonAsync<List<UserPermissionDto>>($"api/doors/{doorId}/permissions", JsonOptions.CaseInsensitive) ?? new();

    public async Task<bool> GrantPermissionAsync(Guid doorId, string userId, bool canOpen, bool canLock, TimeSpan? fromTime, TimeSpan? toTime)
    {
        var r = await _http.PostAsJsonAsync($"api/doors/{doorId}/permissions",
            new GrantPermissionRequest(userId, canOpen, canLock, fromTime, toTime));
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> RevokePermissionAsync(Guid doorId, string userId)
    {
        var r = await _http.DeleteAsync($"api/doors/{doorId}/permissions/{userId}");
        return r.IsSuccessStatusCode;
    }
}

// ── User Service ──────────────────────────────────────────────────────────────

public interface IUserService
{
    Task<PagedResult<UserDto>> GetAllAsync(int page = 1, int pageSize = 20);
    Task<UserDto?> GetByIdAsync(string id);
    Task<List<UserPermissionDto>> GetPermissionsAsync(string userId);
    Task<(bool Success, List<string> Errors)> CreateAsync(string email, string fullName, string password, List<string> roles);
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
        var json = await _http.GetFromJsonAsync<JsonElement>($"api/users?page={page}&pageSize={pageSize}");
        var users = json.GetProperty("users").Deserialize<List<UserDto>>(JsonOptions.CaseInsensitive) ?? new();
        var total = json.GetProperty("total").GetInt32();
        return new PagedResult<UserDto>(users, total, page, pageSize);
    }

    public async Task<UserDto?> GetByIdAsync(string id) =>
        await _http.GetFromJsonAsync<UserDto>($"api/users/{id}", JsonOptions.CaseInsensitive);

    public async Task<List<UserPermissionDto>> GetPermissionsAsync(string userId) =>
        await _http.GetFromJsonAsync<List<UserPermissionDto>>($"api/users/{userId}/permissions", JsonOptions.CaseInsensitive) ?? new();

    public async Task<(bool Success, List<string> Errors)> CreateAsync(string email, string fullName, string password, List<string> roles)
    {
        var r = await _http.PostAsJsonAsync("api/users", new CreateUserRequest(email, fullName, password, roles));
        if (r.IsSuccessStatusCode) return (true, new());
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        var errors = json.TryGetProperty("errors", out var e)
            ? e.Deserialize<List<string>>() ?? new()
            : new List<string> { "خطای ناشناخته" };
        return (false, errors);
    }

    public async Task<bool> UpdateAsync(string id, string fullName, bool isActive, List<string> roles)
    {
        var r = await _http.PutAsJsonAsync($"api/users/{id}", new UpdateUserRequest(fullName, isActive, roles));
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
        await _http.GetFromJsonAsync<List<RoleDto>>("api/roles", JsonOptions.CaseInsensitive) ?? new();

    public async Task<bool> CreateAsync(string name)
    {
        var r = await _http.PostAsJsonAsync("api/roles", new CreateRoleRequest(name));
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var r = await _http.DeleteAsync($"api/roles/{id}");
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> AssignRoleAsync(string userId, string roleName)
    {
        var r = await _http.PostAsJsonAsync("api/roles/assign", new AssignRoleRequest(userId, roleName));
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> RemoveRoleAsync(string userId, string roleName)
    {
        var r = await _http.PostAsJsonAsync("api/roles/remove", new AssignRoleRequest(userId, roleName));
        return r.IsSuccessStatusCode;
    }
}

// ── Station Service ───────────────────────────────────────────────────────────

public record StationDto(
    Guid Id, string MacAddress, string Name, string? Description,
    bool IsEnabled, DateTime RegisteredAt, DateTime? LastSeen, bool IsConnected,
    string? LastKnownIp);

public record DiscoveredStationDto(string Ip, string Mac, bool IsRegistered);

public interface IStationService
{
    Task<List<StationDto>> GetAllAsync();
    Task<List<DiscoveredStationDto>> ScanNetworkAsync();
    Task<(bool Success, string? Error)> RegisterAsync(string macAddress, string name, string? description, string? ip);
    Task<bool> ConnectAsync(string mac);
    Task<bool> UpdateAsync(string mac, string name, string? description, bool isEnabled);
    Task<bool> DeleteAsync(string mac);
    Task<string[]> GetSerialPortsAsync();
    Task<(bool Success, string? Mac, bool AlreadyRegistered, string? Error, string? FlashOutput)> ProvisionAsync(string portName, string ssid, string password, string name, string? description, bool flashFirst = false, string? sessionId = null);
}

public class StationService : IStationService
{
    private readonly HttpClient _http;
    public StationService(HttpClient http) => _http = http;

    public async Task<List<StationDto>> GetAllAsync() =>
        await _http.GetFromJsonAsync<List<StationDto>>("api/stations", JsonOptions.CaseInsensitive) ?? new();

    public async Task<List<DiscoveredStationDto>> ScanNetworkAsync()
    {
        var r = await _http.PostAsync("api/stations/scan", null);
        return r.IsSuccessStatusCode
            ? await r.Content.ReadFromJsonAsync<List<DiscoveredStationDto>>(JsonOptions.CaseInsensitive) ?? new()
            : new();
    }

    public async Task<(bool Success, string? Error)> RegisterAsync(string macAddress, string name, string? description, string? ip)
    {
        var r = await _http.PostAsJsonAsync("api/stations", new { macAddress, name, description, ip });
        if (r.IsSuccessStatusCode) return (true, null);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (false, json.TryGetProperty("message", out var m) ? m.GetString() : "خطا در ثبت دستگاه");
    }

    public async Task<bool> ConnectAsync(string mac)
    {
        var r = await _http.PostAsync($"api/stations/{mac}/connect", null);
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateAsync(string mac, string name, string? description, bool isEnabled)
    {
        var r = await _http.PutAsJsonAsync($"api/stations/{mac}", new { name, description, isEnabled });
        return r.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteAsync(string mac)
    {
        var r = await _http.DeleteAsync($"api/stations/{mac}");
        return r.IsSuccessStatusCode;
    }

    public async Task<string[]> GetSerialPortsAsync()
    {
        return await _http.GetFromJsonAsync<string[]>("api/stations/serial-ports", JsonOptions.CaseInsensitive)
               ?? Array.Empty<string>();
    }

    public async Task<(bool Success, string? Mac, bool AlreadyRegistered, string? Error, string? FlashOutput)> ProvisionAsync(
        string portName, string ssid, string password, string name, string? description, bool flashFirst = false, string? sessionId = null)
    {
        var r = await _http.PostAsJsonAsync("api/stations/provision",
            new { portName, ssid, password, name, description, flashFirst, sessionId });

        var json = await r.Content.ReadFromJsonAsync<JsonElement>();

        string? flashOutput = json.TryGetProperty("flashOutput", out var fo) ? fo.GetString() : null;

        if (r.IsSuccessStatusCode)
        {
            var mac = json.TryGetProperty("mac", out var m) ? m.GetString() : null;
            var already = json.TryGetProperty("alreadyRegistered", out var a) && a.GetBoolean();
            return (true, mac, already, null, flashOutput);
        }

        return (false, null, false, json.TryGetProperty("message", out var msg) ? msg.GetString() : "خطا در پروویژن", flashOutput);
    }
}

// ── Log Service ───────────────────────────────────────────────────────────────

public interface ILogService
{
    Task<PagedResult<DoorAccessLogDto>> GetLogsAsync(Guid? doorId = null, string? userId = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 25);
}

public class LogService : ILogService
{
    private readonly IDoorService _doorService;
    public LogService(IDoorService doorService) => _doorService = doorService;

    public Task<PagedResult<DoorAccessLogDto>> GetLogsAsync(Guid? doorId = null, string? userId = null,
        DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 25) =>
        _doorService.GetLogsAsync(doorId, userId, from, to, page, pageSize);
}

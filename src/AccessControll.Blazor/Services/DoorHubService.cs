using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;

namespace AccessControll.Blazor.Services;

public class DoorStatusChanged
{
    public Guid DoorId { get; set; }
    public bool IsLocked { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime At { get; set; }
}

public class DoorHubService : IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly string _hubUrl;
    private readonly JwtAuthStateProvider _authState;

    public event Action<DoorStatusChanged>? OnDoorStatusChanged;
    public event Action<object>? OnDoorUpdated;

    public DoorHubService(IConfiguration config, AuthenticationStateProvider authState)
    {
        var apiBase = config["ApiBaseUrl"] ?? "https://localhost:7000";
        _hubUrl = apiBase.TrimEnd('/') + "/hubs/door";
        _authState = (JwtAuthStateProvider)authState;
    }

    public async Task StartAsync()
    {
        if (_connection?.State == HubConnectionState.Connected) return;

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, opts =>
            {
                opts.AccessTokenProvider = async () => await _authState.GetTokenAsync();
            })
            .WithAutomaticReconnect()
            .Build();

        _connection.On<DoorStatusChanged>("DoorStatusChanged", status =>
            OnDoorStatusChanged?.Invoke(status));

        _connection.On<object>("DoorUpdated", door =>
            OnDoorUpdated?.Invoke(door));

        try { await _connection.StartAsync(); }
        catch { /* Hub unavailable — silently skip */ }
    }

    public async Task StopAsync()
    {
        if (_connection != null)
            try { await _connection.StopAsync(); } catch { }
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
            await _connection.DisposeAsync();
    }
}

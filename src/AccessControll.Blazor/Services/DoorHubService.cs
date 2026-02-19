using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Components.Authorization;

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
    private bool _startAttempted;

    public event Action<DoorStatusChanged>? OnDoorStatusChanged;
    public event Action<object>? OnDoorUpdated;

    public DoorHubService(HttpClient http, AuthenticationStateProvider authState)
    {
        _hubUrl = http.BaseAddress!.ToString().TrimEnd('/') + "/hubs/door";
        _authState = (JwtAuthStateProvider)authState;

    }

    public async Task StartAsync()
    {
        Console.WriteLine("-------------0-------------");
        // Avoid repeated connection attempts if already connected or already failed
        if (_startAttempted && _connection?.State != HubConnectionState.Disconnected) return;
        if (_connection?.State == HubConnectionState.Connected) return;

        _startAttempted = true;
        Console.WriteLine("-------------1-------------");

        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, opts =>
            {
                opts.AccessTokenProvider = async () => await _authState.GetTokenAsync();
                // Use LongPolling as fallback to avoid WebSocket SecurityError in mixed-content scenarios
                opts.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets
                                | Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                opts.SkipNegotiation = false;
            })
            // Limit reconnect attempts to prevent WASM runtime crash from repeated failures
            .WithAutomaticReconnect(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30) })
            .Build();
        Console.WriteLine("-------------2-------------");

        _connection.On<DoorStatusChanged>("DoorStatusChanged", status =>
            OnDoorStatusChanged?.Invoke(status));

        _connection.On<object>("DoorUpdated", door =>
            OnDoorUpdated?.Invoke(door));
        Console.WriteLine("-------------3-------------");

        try
        {
            await _connection.StartAsync();
        }
        catch (Exception)
        {
            Console.WriteLine("--------------3 error------------");

            // Hub unavailable (e.g. wrong protocol, server offline) — silently degrade
            // The app continues to function without real-time updates
        }
        Console.WriteLine("--------------4------------");
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
        {
            try { await _connection.StopAsync(); } catch { }
            await _connection.DisposeAsync();
        }
    }
}

using AccessControll.Domain.Enums;
using AccessControll.Hardware;
using AccessControll.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AccessControll.API.Hubs;

public class HardwareHub : Hub
{
    private readonly StationConnectionManager _connectionManager;
    private readonly StationSessionManager _sessionManager;
    private readonly IHubContext<DoorHub> _doorHub;
    private readonly ServerKeyService _keyService;
    private readonly ApplicationDbContext _db;

    // connectionId → nonce sent with the challenge; cleared on RegisterStation or Disconnect
    private static readonly ConcurrentDictionary<string, byte[]> _pendingChallenges = new();

    public HardwareHub(
        StationConnectionManager connectionManager,
        StationSessionManager sessionManager,
        IHubContext<DoorHub> doorHub,
        ServerKeyService keyService,
        ApplicationDbContext db)
    {
        _connectionManager = connectionManager;
        _sessionManager    = sessionManager;
        _doorHub           = doorHub;
        _keyService        = keyService;
        _db                = db;
    }

    /// <summary>
    /// On every new WebSocket connection, generate a 16-byte random nonce, sign it with
    /// the server's P-256 private key, and send both values as <c>AuthChallenge</c>.
    /// The ESP8266 verifies the signature against the provisioned public key; only if valid
    /// does it proceed to call <c>RegisterStation</c>.
    ///
    /// The pending nonce is stored so that <c>RegisterStation</c> can confirm the challenge
    /// was issued (defense-in-depth: prevents registration without prior challenge).
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var nonce     = RandomNumberGenerator.GetBytes(16);
        var signature = _keyService.Sign(nonce);

        _pendingChallenges[Context.ConnectionId] = nonce;

        var nonceHex = Convert.ToHexString(nonce).ToLowerInvariant();    // 32 hex chars
        var sigHex   = Convert.ToHexString(signature).ToLowerInvariant(); // 128 hex chars

        Console.WriteLine($"[HW] {Context.ConnectionId} connected — AuthChallenge sent (nonce={nonceHex[..8]}…)");
        await Clients.Caller.SendAsync("AuthChallenge", nonceHex, sigHex);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _pendingChallenges.TryRemove(Context.ConnectionId, out _);

        var mac = _connectionManager.GetMac(Context.ConnectionId);
        if (mac != null)
        {
            Console.WriteLine($"[HW] Station disconnected: {mac}");
            _connectionManager.Unregister(Context.ConnectionId);
            _sessionManager.RemoveSession(mac);

            await _doorHub.Clients.Group("all-doors")
                .SendAsync("StationStatusChanged", mac, false);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Called by the ESP8266 after it has successfully verified the <c>AuthChallenge</c>
    /// signature locally with micro-ecc.
    ///
    /// Two gates before registration is allowed:
    ///   1. A pending challenge must exist (proves the auth flow was followed, not bypassed).
    ///   2. The MAC address must be registered and enabled in the database — even if the
    ///      server itself booted/provisioned the device, it is not allowed to connect until
    ///      it has been explicitly registered (prevents rogue or decommissioned stations).
    /// </summary>
    /// <param name="stationType">Station type reported by the ESP firmware (CFG_STATION_TYPE baked at flash time).
    /// The server trusts this value and updates the DB so the type always reflects actual hardware.</param>
    public async Task RegisterStation(string macAddress, int stationType = 0)
    {
        // ── Gate 1: challenge must have been issued for this connection ────────
        if (!_pendingChallenges.TryRemove(Context.ConnectionId, out _))
        {
            Console.WriteLine($"[HW] RegisterStation from {Context.ConnectionId} without a pending challenge — aborting");
            Context.Abort();
            return;
        }

        var mac = macAddress.ToUpperInvariant();

        // ── Gate 2: MAC must be in the DB and enabled ─────────────────────────
        var station = await _db.Stations
            .FirstOrDefaultAsync(s => s.MacAddress == mac);

        if (station is null)
        {
            Console.WriteLine($"[HW] MAC {mac} is not registered in the database — rejecting");
            Context.Abort();
            return;
        }

        if (!station.IsEnabled)
        {
            Console.WriteLine($"[HW] Station {mac} is disabled — rejecting");
            Context.Abort();
            return;
        }

        // ── Update type from firmware ─────────────────────────────────────────
        var reportedType = (StationType)stationType;
        if (station.Type != reportedType)
        {
            station.Type = reportedType;
            await _db.SaveChangesAsync();
            Console.WriteLine($"[HW] Station {mac} type updated to {reportedType}");
        }

        // ── Accepted ──────────────────────────────────────────────────────────
        Console.WriteLine($"[HW] Station registered: {mac} — '{station.Name}' type={station.Type} (conn={Context.ConnectionId})");

        _connectionManager.Register(mac, Context.ConnectionId);

        // RemoteControl stations have no display/keyboard — no session needed
        if (station.Type != StationType.RemoteControl)
            _sessionManager.CreateSession(mac, station.Type);

        await _doorHub.Clients.Group("all-doors")
            .SendAsync("StationStatusChanged", mac, true);
    }

    /// <summary>
    /// Receives a key press from an ESP8266 keypad and routes it to that station's session.
    /// Display update is sent back only to that station via its ConnectionId.
    /// </summary>
    public async Task SendKey(string key)
    {
        var mac = _connectionManager.GetMac(Context.ConnectionId);
        if (mac == null)
        {
            Console.WriteLine($"[HW] Key from unregistered connection: {Context.ConnectionId}");
            return;
        }

        Console.WriteLine($"[HW] Key '{key}' from {mac}");

        if (key.Length > 0)
            await _sessionManager.HandleKey(mac, key[0]);
    }
}

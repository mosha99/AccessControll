using System.Collections.Concurrent;

namespace AccessControll.Hardware;

/// <summary>
/// Tracks live MAC address ↔ SignalR ConnectionId mappings for all connected ESP8266 stations.
/// Thread-safe singleton.
/// </summary>
public class StationConnectionManager
{
    private readonly ConcurrentDictionary<string, string> _macToConn = new();
    private readonly ConcurrentDictionary<string, string> _connToMac = new();

    // Per-connection monotonic counter used to sequence signed messages (anti-replay).
    private readonly ConcurrentDictionary<string, uint> _seqno = new();

    /// <summary>Register or update the connection for a MAC address.</summary>
    public void Register(string mac, string connectionId)
    {
        // If this MAC was already connected (reconnect), clean up old mapping
        if (_macToConn.TryGetValue(mac, out var oldConn))
        {
            _connToMac.TryRemove(oldConn, out _);
            _seqno.TryRemove(oldConn, out _);
        }

        _macToConn[mac] = connectionId;
        _connToMac[connectionId] = mac;
        _seqno[connectionId] = 0;
    }

    /// <summary>Remove mapping when a connection disconnects.</summary>
    public void Unregister(string connectionId)
    {
        if (_connToMac.TryRemove(connectionId, out var mac))
            _macToConn.TryRemove(mac, out _);
        _seqno.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// Returns the next sequence number for a connection (1-based, monotonically increasing).
    /// Used to prevent replay attacks on signed server→ESP messages.
    /// </summary>
    public uint NextSeqno(string connectionId) =>
        _seqno.AddOrUpdate(connectionId, 1u, (_, v) => v + 1u);

    /// <summary>Get the current ConnectionId for a given MAC, or null if not connected.</summary>
    public string? GetConnectionId(string mac) =>
        _macToConn.TryGetValue(mac, out var conn) ? conn : null;

    /// <summary>Get the MAC address for a given ConnectionId, or null if unknown.</summary>
    public string? GetMac(string connectionId) =>
        _connToMac.TryGetValue(connectionId, out var mac) ? mac : null;

    /// <summary>List of all currently connected MAC addresses.</summary>
    public IReadOnlyList<string> GetConnectedMacs() =>
        _macToConn.Keys.ToList().AsReadOnly();

    public bool IsConnected(string mac) => _macToConn.ContainsKey(mac);
}

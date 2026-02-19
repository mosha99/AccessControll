using AccessControll.Domain.Entities;
using AccessControll.Hardware;
using AccessControll.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AccessControll.API.Controllers;

[ApiController]
[Route("api/stations")]
[Authorize(Roles = "Admin")]
public class StationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly StationConnectionManager _connectionManager;

    public StationsController(ApplicationDbContext db, StationConnectionManager connectionManager)
    {
        _db = db;
        _connectionManager = connectionManager;
    }

    /// <summary>Returns all registered stations with their live connection status.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var stations = await _db.Stations.ToListAsync();
        return Ok(stations.Select(s => new
        {
            s.Id,
            s.MacAddress,
            s.Name,
            s.Description,
            s.IsEnabled,
            s.RegisteredAt,
            s.LastSeen,
            IsConnected = _connectionManager.IsConnected(s.MacAddress)
        }));
    }

    /// <summary>Returns all currently connected station MACs (registered or not).</summary>
    [HttpGet("connected")]
    public async Task<IActionResult> GetConnected()
    {
        var macs = _connectionManager.GetConnectedMacs();

        var registered = await _db.Stations
            .Where(s => macs.Contains(s.MacAddress))
            .ToListAsync();

        var registeredByMac = registered.ToDictionary(s => s.MacAddress);

        var result = macs.Select(mac => new
        {
            MacAddress = mac,
            IsRegistered = registeredByMac.ContainsKey(mac),
            Name = registeredByMac.TryGetValue(mac, out var s) ? s.Name : null,
            StationId = registeredByMac.TryGetValue(mac, out var s2) ? s2.Id : (Guid?)null
        });

        return Ok(result);
    }

    /// <summary>Register a discovered station by MAC address.</summary>
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterStationRequest req)
    {
        var mac = req.MacAddress.ToUpperInvariant();

        if (await _db.Stations.AnyAsync(s => s.MacAddress == mac))
            return Conflict(new { message = "این دستگاه قبلاً ثبت شده است" });

        var station = new Station
        {
            Id = Guid.NewGuid(),
            MacAddress = mac,
            Name = req.Name,
            Description = req.Description,
            IsEnabled = true,
            RegisteredAt = DateTime.UtcNow
        };

        _db.Stations.Add(station);
        await _db.SaveChangesAsync();
        return Ok(station);
    }

    /// <summary>Update station name / description / enabled flag.</summary>
    [HttpPut("{mac}")]
    public async Task<IActionResult> Update(string mac, [FromBody] UpdateStationRequest req)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.MacAddress == mac.ToUpperInvariant());
        if (station == null) return NotFound();

        station.Name = req.Name;
        station.Description = req.Description;
        station.IsEnabled = req.IsEnabled;
        await _db.SaveChangesAsync();
        return Ok(station);
    }

    /// <summary>Remove a registered station.</summary>
    [HttpDelete("{mac}")]
    public async Task<IActionResult> Delete(string mac)
    {
        var station = await _db.Stations.FirstOrDefaultAsync(s => s.MacAddress == mac.ToUpperInvariant());
        if (station == null) return NotFound();

        _db.Stations.Remove(station);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record RegisterStationRequest(string MacAddress, string Name, string? Description);
public record UpdateStationRequest(string Name, string? Description, bool IsEnabled);

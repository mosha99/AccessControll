using AccessControll.API.Hubs;
using AccessControll.Domain.Entities;
using AccessControll.Domain.Enums;
using AccessControll.Hardware;
using AccessControll.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AccessControll.API.Controllers;

[ApiController]
[Route("api/stations")]
[Authorize(Roles = "Admin")]
public class StationsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly StationConnectionManager _connectionManager;
    private readonly ProvisioningService _provisioning;
    private readonly IHubContext<DoorHub> _doorHub;
    private readonly ServerKeyService _keyService;

    public StationsController(
        ApplicationDbContext db,
        StationConnectionManager connectionManager,
        ProvisioningService provisioning,
        IHubContext<DoorHub> doorHub,
        ServerKeyService keyService)
    {
        _db                = db;
        _connectionManager = connectionManager;
        _provisioning      = provisioning;
        _doorHub           = doorHub;
        _keyService        = keyService;
    }

    /// <summary>
    /// Returns the server's P-256 public key and a live sign+verify round-trip result.
    /// Use the fingerprint to confirm a provisioned station received the correct key.
    /// </summary>
    [HttpGet("server-key")]
    public IActionResult GetServerKey()
    {
        // Live round-trip: sign a random nonce and verify it — confirms the key service works
        var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        var sig   = _keyService.Sign(nonce);
        var ok    = _keyService.Verify(nonce, sig);

        return Ok(new
        {
            pubkeyHex   = _keyService.PublicKeyHex,          // 128 chars — sent to ESP during provisioning
            fingerprint = _keyService.Fingerprint,            // SHA-256 of public key
            selfTest    = ok ? "PASS" : "FAIL",               // live sign+verify round-trip
            keyFile     = "server.p256key",
        });
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
            s.LastKnownIp,
            Type        = (int)s.Type,
            IsConnected = _connectionManager.IsConnected(s.MacAddress)
        }));
    }

    /// <summary>Returns the list of serial ports available on the server OS.</summary>
    [HttpGet("serial-ports")]
    public IActionResult GetSerialPorts()
    {
        return Ok(ProvisioningService.GetAvailablePorts());
    }

    /// <summary>
    /// Provisions an ESP8266:
    ///   - FlashFirst=true : flash via arduino-cli → extract MAC from esptool output →
    ///                       send WiFi creds one-way (no roundtrip wait) → register in DB.
    ///   - FlashFirst=false: legacy roundtrip — send PROVISION command, wait for OK:{mac}.
    /// </summary>
    [HttpPost("provision")]
    public async Task<IActionResult> Provision([FromBody] ProvisionRequest req)
    {
        string? flashOutput = null;
        string mac;

        if (req.FlashFirst)
        {
            // ── Step 1: Bake WiFi creds + server pubkey into config.h ─────────────
            // The sketch reads config.h on first boot and writes to EEPROM.
            // No UART roundtrip needed — credentials travel inside the firmware.
            try { _provisioning.WriteConfigHeader(req.Ssid, req.Password, req.StationType, req.ServerHost, req.ServerPort); }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"خطا در نوشتن config.h: {ex.Message}", flashOutput });
            }

            // ── Step 2: Compile + flash (arduino-cli picks up the new config.h) ──
            Action<string>? onLine = null;
            if (!string.IsNullOrEmpty(req.SessionId))
            {
                var group = $"prov-{req.SessionId}";
                onLine = line => _ = _doorHub.Clients.Group(group).SendAsync("FlashLog", line);
            }

            var flash = await _provisioning.FlashFirmwareAsync(req.PortName, onLine);
            flashOutput = flash.Output;
            if (!flash.Success)
                return BadRequest(new { message = "خطا در فلش firmware", flashOutput });

            // ── Step 3: Extract MAC from esptool output ───────────────────────────
            var extractedMac = ProvisioningService.ExtractMacFromOutput(flashOutput);
            if (extractedMac is null)
                return BadRequest(new { message = "MAC آدرس در خروجی esptool یافت نشد", flashOutput });

            mac = extractedMac;
            // No UART send needed — credentials are baked into the firmware.
        }
        else
        {
            // Legacy UART roundtrip — only works with adapters that have DTR/RTS.
            var result = await _provisioning.ProvisionAsync(req.PortName, req.Ssid, req.Password);
            if (!result.Success)
                return BadRequest(new { message = result.Error, flashOutput });

            mac = result.Mac!.ToUpperInvariant();
        }

        var alreadyRegistered = await _db.Stations.AnyAsync(s => s.MacAddress == mac);

        if (!alreadyRegistered)
        {
            _db.Stations.Add(new Station
            {
                Id           = Guid.NewGuid(),
                MacAddress   = mac,
                Name         = req.Name,
                Description  = req.Description,
                IsEnabled    = true,
                RegisteredAt = DateTime.UtcNow,
                Type         = (StationType)req.StationType,
            });
            await _db.SaveChangesAsync();
        }

        return Ok(new { Mac = mac, AlreadyRegistered = alreadyRegistered, FlashOutput = flashOutput });
    }

    /// <summary>Register a station by MAC address (and optional IP).</summary>
    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterStationRequest req)
    {
        var mac = req.MacAddress.ToUpperInvariant();

        if (await _db.Stations.AnyAsync(s => s.MacAddress == mac))
            return Conflict(new { message = "این دستگاه قبلاً ثبت شده است" });

        var station = new Station
        {
            Id           = Guid.NewGuid(),
            MacAddress   = mac,
            Name         = req.Name,
            Description  = req.Description,
            IsEnabled    = true,
            RegisteredAt = DateTime.UtcNow,
            LastKnownIp  = req.Ip,
            Type         = (AccessControll.Domain.Enums.StationType)(req.Type ?? 0)
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

        station.Name        = req.Name;
        station.Description = req.Description;
        station.IsEnabled   = req.IsEnabled;
        station.Type        = (AccessControll.Domain.Enums.StationType)req.Type;
        await _db.SaveChangesAsync();
        return Ok(station);
    }

    /// <summary>
    /// Informs the caller of the current connection status.
    /// The ESP reconnects automatically every ~5 s after registration,
    /// so no explicit trigger is needed — this endpoint just returns the live state.
    /// </summary>
    [HttpPost("{mac}/connect")]
    public IActionResult Connect(string mac)
    {
        mac = mac.ToUpperInvariant();
        var connected = _connectionManager.IsConnected(mac);
        return Ok(new
        {
            connected,
            message = connected ? "دستگاه در حال حاضر متصل است" : "دستگاه به زودی به صورت خودکار متصل می‌شود"
        });
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

public record RegisterStationRequest(string MacAddress, string Name, string? Description, string? Ip, int? Type = null);
public record UpdateStationRequest(string Name, string? Description, bool IsEnabled, int Type = 0);
public record ProvisionRequest(string PortName, string Ssid, string Password, string Name, string? Description, bool FlashFirst = false, string? SessionId = null, int StationType = 0, string? ServerHost = null, int? ServerPort = null);

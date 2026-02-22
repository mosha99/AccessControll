using System.Security.Cryptography;

namespace AccessControll.Hardware;

/// <summary>
/// Manages the server's P-256 (secp256r1) ECDSA key pair.
///
/// On first run, generates a new key pair and persists the private key as a PEM
/// file (<c>server.p256key</c> next to the executable). On subsequent runs the
/// key is loaded from that file so the public key stays stable across restarts.
///
/// Security model
/// ──────────────
/// The public key (64 bytes = X‖Y, 128 hex chars) is sent to each ESP8266
/// station during UART provisioning and stored in its EEPROM.  When the server
/// later POSTs a /connect command it includes a random nonce and its ECDSA
/// (SHA-256) signature of that nonce.  The ESP verifies the signature with
/// micro-ecc against the stored public key before opening the WebSocket —
/// preventing a rogue server from hijacking stations.
///
/// Signature format: IEEE P1363 fixed-width concatenation (r‖s, 64 bytes).
/// Public key format: raw X‖Y coordinates (64 bytes, big-endian, no prefix).
/// </summary>
public sealed class ServerKeyService : IDisposable
{
    private readonly ECDsa _ecdsa;

    /// <summary>Raw 64-byte P-256 public key (X‖Y, each 32 bytes big-endian).</summary>
    public byte[] PublicKeyBytes { get; }

    /// <summary>Lowercase hex representation of the public key (128 chars).</summary>
    public string PublicKeyHex { get; }

    public ServerKeyService(string keyFilePath = "server.p256key")
    {
        if (File.Exists(keyFilePath))
        {
            _ecdsa = ECDsa.Create();
            _ecdsa.ImportFromPem(File.ReadAllText(keyFilePath));
            Console.WriteLine($"[Key] P-256 key loaded from {keyFilePath}");
        }
        else
        {
            _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllText(keyFilePath, _ecdsa.ExportECPrivateKeyPem());
            Console.WriteLine($"[Key] New P-256 key pair generated → {keyFilePath}");
        }

        var p = _ecdsa.ExportParameters(false);
        PublicKeyBytes = [.. p.Q.X!, .. p.Q.Y!];                               // 64 bytes
        PublicKeyHex   = Convert.ToHexString(PublicKeyBytes).ToLowerInvariant(); // 128 chars

        // SHA-256 fingerprint of the public key (first 8 bytes shown, like SSH)
        var fp = SHA256.HashData(PublicKeyBytes);
        Fingerprint = Convert.ToHexString(fp).ToLowerInvariant();

        // Self-test: sign a known message and verify — catches bad key files immediately
        var probe = "AccessControll-selftest"u8.ToArray();
        var probeSig = Sign(probe);
        if (!Verify(probe, probeSig))
            throw new InvalidOperationException("[Key] FATAL: P-256 self-test FAILED — key file may be corrupt");

        Console.WriteLine($"[Key] Fingerprint: {Fingerprint[..16]}…  self-test: PASS");
    }

    /// <summary>SHA-256 fingerprint of the public key (64 hex chars).</summary>
    public string Fingerprint { get; }

    /// <summary>
    /// Signs <paramref name="data"/> with P-256 / SHA-256.
    /// Returns a 64-byte IEEE P1363 raw signature (r‖s).
    /// </summary>
    public byte[] Sign(byte[] data) =>
        _ecdsa.SignData(
            data,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// <summary>Verifies a 64-byte P1363 signature. Used for testing.</summary>
    public bool Verify(byte[] data, byte[] signature) =>
        _ecdsa.VerifyData(
            data,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public void Dispose() => _ecdsa.Dispose();
}

using System.Security.Cryptography;
using System.Text;

namespace LBLocker.Infrastructure.Security;

/// <summary>
/// Protects PIN values at rest with AES-256-GCM. The key is supplied outside
/// the database through LBLOCKER_PIN_ENCRYPTION_KEY (base64, exactly 32 bytes).
/// </summary>
public sealed class PinCredentialProtector
{
    private const string CurrentKeyVersion = "aes256gcm-v1";
    private readonly byte[] _key;

    public PinCredentialProtector()
    {
        var encodedKey = Environment.GetEnvironmentVariable("LBLOCKER_PIN_ENCRYPTION_KEY");
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
#if DEBUG
            // DEMO ONLY: "Jacob" is deliberately hard-coded so reviewers can see
            // how key management must NOT be done. Never ship this in production.
            // Release builds require LBLOCKER_PIN_ENCRYPTION_KEY instead.
            Console.Error.WriteLine(
                "WARNING: Using the hard-coded DEMO PIN key. This is insecure and DEBUG-only.");
            _key = SHA256.HashData(Encoding.UTF8.GetBytes("Jacob"));
            return;
#else
            throw new InvalidOperationException(
                "PIN encryption is not configured. Set LBLOCKER_PIN_ENCRYPTION_KEY to a base64-encoded 32-byte key.");
#endif
        }

        try
        {
            _key = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "LBLOCKER_PIN_ENCRYPTION_KEY must be valid base64.", exception);
        }

        if (_key.Length != 32)
            throw new InvalidOperationException(
                "LBLOCKER_PIN_ENCRYPTION_KEY must decode to exactly 32 bytes.");
    }

    public string KeyVersion => CurrentKeyVersion;

    public string Protect(string pin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pin);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(pin);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);
        CryptographicOperations.ZeroMemory(plaintext);
        return Convert.ToBase64String(payload);
    }

    public string Unprotect(string protectedPin)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedPin);
        var payload = Convert.FromBase64String(protectedPin);
        if (payload.Length < 28)
            throw new CryptographicException("Protected PIN payload is truncated.");

        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var ciphertext = payload.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

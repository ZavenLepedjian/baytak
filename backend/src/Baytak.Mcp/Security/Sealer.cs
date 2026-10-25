using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Baytak.Mcp.Security;

/// <summary>
/// Encrypts and authenticates small JSON payloads (AES-256-GCM) so the app can stay stateless: sign-in steps, authorization
/// codes, tokens and registered client ids carry their own data. The purpose is bound as associated data, so a value sealed
/// for one use (an access token) can never be opened as another (a refresh token).
/// </summary>
public sealed class Sealer
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly byte[] _key;
    private readonly byte[]? _previous;

    public Sealer(IOptions<McpOptions> options)
    {
        _key = ParseKey(options.Value.SecretKey, "Mcp:SecretKey")
               ?? throw new InvalidOperationException("Set Mcp:SecretKey to a base64-encoded 32-byte key (openssl rand -base64 32).");
        _previous = string.IsNullOrWhiteSpace(options.Value.PreviousSecretKey) ? null : ParseKey(options.Value.PreviousSecretKey, "Mcp:PreviousSecretKey");
    }

    public string Seal<T>(string purpose, T payload)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        var output = new byte[NonceSize + plain.Length + TagSize];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize, plain.Length), output.AsSpan(NonceSize + plain.Length), Encoding.UTF8.GetBytes(purpose));
        return Base64Url.EncodeToString(output);
    }

    /// <summary>The payload, or null when the value is malformed, tampered with, or sealed for another purpose.</summary>
    public T? Open<T>(string purpose, string? value) where T : class
    {
        if (string.IsNullOrEmpty(value) || value.Length > 8192) return null;
        byte[] data;
        try
        {
            data = Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            return null;
        }
        if (data.Length < NonceSize + TagSize) return null;

        var keys = _previous is null ? new[] { _key } : new[] { _key, _previous };
        foreach (var key in keys)
        {
            var plain = new byte[data.Length - NonceSize - TagSize];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize, plain.Length), data.AsSpan(NonceSize + plain.Length), plain, Encoding.UTF8.GetBytes(purpose));
            }
            catch (AuthenticationTagMismatchException)
            {
                continue;
            }
            try
            {
                return JsonSerializer.Deserialize<T>(plain, Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>A random identifier safe for URLs and Table Storage keys.</summary>
    public static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static byte[]? ParseKey(string? raw, string name)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        byte[] key;
        try
        {
            key = Convert.FromBase64String(raw.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{name} must be base64.");
        }
        return key.Length == 32 ? key : throw new InvalidOperationException($"{name} must decode to exactly 32 bytes.");
    }
}

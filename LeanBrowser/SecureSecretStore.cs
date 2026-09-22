using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;

namespace LeanBrowser;

/// <summary>Names are fixed to prevent path injection and purpose confusion.</summary>
public enum SecureSecretId
{
    AdministratorAccessToken,
    AiProviderApiKey,
    WebRiskApiKey
}

public interface ISecretStore
{
    void Save(SecureSecretId id, ReadOnlySpan<char> secret);
    bool TryOpen(SecureSecretId id, out SecretLease? secret);
    bool Delete(SecureSecretId id);
}

/// <summary>
/// Windows-only local secret store. Ciphertext is bound to the current Windows
/// user through DPAPI; each secret also has separate purpose entropy so a
/// ciphertext cannot be substituted for another secret type.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private const string PurposePrefix = "CottonBrowser.DPAPI.v1:";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly string _directory;

    public DpapiSecretStore(string? directory = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DPAPI secret storage is available only on Windows.");

        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeanBrowser",
            "Secrets");
    }

    public void Save(SecureSecretId id, ReadOnlySpan<char> secret)
    {
        if (secret.IsEmpty || secret.IsWhiteSpace())
            throw new ArgumentException("A secret cannot be empty.", nameof(secret));

        var secretCharacters = secret.ToArray();
        byte[]? plaintext = null;
        var entropy = CreatePurposeEntropy(id);
        byte[]? ciphertext = null;
        try
        {
            plaintext = Utf8.GetBytes(secretCharacters);
            ciphertext = ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser);
            WriteCiphertextAtomically(GetPath(id), ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes<char>(secretCharacters));
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(entropy);
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public bool TryOpen(SecureSecretId id, out SecretLease? secret)
    {
        secret = null;
        var path = GetPath(id);
        if (!File.Exists(path)) return false;

        byte[]? ciphertext = null;
        byte[]? plaintext = null;
        var entropy = CreatePurposeEntropy(id);
        try
        {
            ciphertext = File.ReadAllBytes(path);
            plaintext = ProtectedData.Unprotect(ciphertext, entropy, DataProtectionScope.CurrentUser);
            var characters = Utf8.GetChars(plaintext);
            if (characters.Length == 0 || ((ReadOnlySpan<char>)characters).IsWhiteSpace())
            {
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes<char>(characters));
                return false;
            }

            secret = new SecretLease(characters);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            if (ciphertext is not null) CryptographicOperations.ZeroMemory(ciphertext);
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(entropy);
        }
    }

    public bool Delete(SecureSecretId id)
    {
        var path = GetPath(id);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private void WriteCiphertextAtomically(string destination, byte[] ciphertext)
    {
        Directory.CreateDirectory(_directory);
        var temporary = Path.Combine(_directory, "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(ciphertext, 0, ciphertext.Length);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string GetPath(SecureSecretId id) => Path.Combine(_directory, id switch
    {
        SecureSecretId.AdministratorAccessToken => "administrator-access-token.dpapi",
        SecureSecretId.AiProviderApiKey => "ai-provider-api-key.dpapi",
        SecureSecretId.WebRiskApiKey => "web-risk-api-key.dpapi",
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    });

    private static byte[] CreatePurposeEntropy(SecureSecretId id) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(PurposePrefix + id));
}

/// <summary>
/// Keeps decrypted characters in a disposable buffer. Call CopyToString only
/// at the last responsible moment, then dispose the lease promptly.
/// </summary>
public sealed class SecretLease : IDisposable
{
    private char[]? _characters;

    internal SecretLease(char[] characters) => _characters = characters;

    public string CopyToString()
    {
        var characters = _characters ?? throw new ObjectDisposedException(nameof(SecretLease));
        return new string(characters);
    }

    public void Dispose()
    {
        var characters = Interlocked.Exchange(ref _characters, null);
        if (characters is not null)
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes<char>(characters));
    }
}

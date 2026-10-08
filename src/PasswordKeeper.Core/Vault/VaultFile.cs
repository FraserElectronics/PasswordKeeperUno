using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace PasswordKeeper.Core.Vault;

/// <summary>
/// Encrypts and decrypts the portable vault file.
///
/// File layout (all integers big-endian):
///   magic   "PKV1"        4 bytes
///   format  0x01          1 byte
///   memKiB                4 bytes   Argon2id memory cost
///   iters                 4 bytes   Argon2id time cost
///   lanes                 4 bytes   Argon2id parallelism
///   salt                 16 bytes   fresh on every save
///   nonce                12 bytes   fresh on every save
///   ciphertext            n bytes   AES-256-GCM of the JSON payload
///   tag                  16 bytes
/// The entire header is bound to the ciphertext as GCM associated data, so
/// changing any header byte makes decryption fail.
/// </summary>
public static class VaultFile
{
    private static readonly byte[] Magic = "PKV1"u8.ToArray();
    private const byte FormatVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int HeaderSize = 4 + 1 + 4 + 4 + 4 + SaltSize + NonceSize;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>Encrypts <paramref name="data"/> into a vault file image.</summary>
    public static byte[] Seal(VaultData data, string masterPassword, KdfParameters? kdf = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var p = kdf ?? KdfParameters.Default;
        if (!p.IsWithinSafeBounds) throw new ArgumentException("KDF parameters out of range.", nameof(kdf));

        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        header[4] = FormatVersion;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(5), p.MemoryKiB);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(9), p.Iterations);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(13), p.Parallelism);
        RandomNumberGenerator.Fill(header.AsSpan(17, SaltSize));
        RandomNumberGenerator.Fill(header.AsSpan(17 + SaltSize, NonceSize));

        var plaintext = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        var key = DeriveKey(masterPassword, header.AsSpan(17, SaltSize), p);
        try
        {
            var output = new byte[HeaderSize + plaintext.Length + TagSize];
            header.CopyTo(output, 0);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(
                header.AsSpan(17 + SaltSize, NonceSize),
                plaintext,
                output.AsSpan(HeaderSize, plaintext.Length),
                output.AsSpan(HeaderSize + plaintext.Length, TagSize),
                header);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Decrypts a vault file image.</summary>
    /// <exception cref="InvalidVaultFormatException">Not a vault, or unsafe parameters.</exception>
    /// <exception cref="VaultAuthenticationException">Wrong password or tampered file.</exception>
    public static VaultData Open(ReadOnlySpan<byte> file, string masterPassword)
    {
        if (file.Length < HeaderSize + TagSize || !file[..4].SequenceEqual(Magic))
            throw new InvalidVaultFormatException("Not a PasswordKeeper vault file.");
        if (file[4] != FormatVersion)
            throw new InvalidVaultFormatException($"Unsupported vault format version {file[4]}.");

        var p = new KdfParameters(
            BinaryPrimitives.ReadUInt32BigEndian(file[5..]),
            BinaryPrimitives.ReadUInt32BigEndian(file[9..]),
            BinaryPrimitives.ReadUInt32BigEndian(file[13..]));
        if (!p.IsWithinSafeBounds)
            throw new InvalidVaultFormatException("Vault key-derivation parameters are out of range.");

        var header = file[..HeaderSize];
        var ciphertext = file.Slice(HeaderSize, file.Length - HeaderSize - TagSize);
        var tag = file[^TagSize..];

        var key = DeriveKey(masterPassword, header.Slice(17, SaltSize), p);
        var plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(header.Slice(17 + SaltSize, NonceSize), ciphertext, tag, plaintext, header);
            return JsonSerializer.Deserialize<VaultData>(plaintext, Json)
                   ?? throw new InvalidVaultFormatException("Vault payload is empty.");
        }
        catch (CryptographicException)
        {
            throw new VaultAuthenticationException();
        }
        catch (JsonException)
        {
            throw new InvalidVaultFormatException("Vault payload is malformed.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Writes the vault atomically: temp file in the same folder, then rename over the target.</summary>
    public static void Save(string path, VaultData data, string masterPassword, KdfParameters? kdf = null)
    {
        var bytes = Seal(data, masterPassword, kdf);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    public static VaultData Load(string path, string masterPassword) =>
        Open(File.ReadAllBytes(path), masterPassword);

    private static byte[] DeriveKey(string masterPassword, ReadOnlySpan<byte> salt, KdfParameters p)
    {
        ArgumentException.ThrowIfNullOrEmpty(masterPassword);
        // NFKC so the same password typed on macOS, Windows or a phone keyboard yields the same key.
        var pwd = Encoding.UTF8.GetBytes(masterPassword.Normalize(NormalizationForm.FormKC));
        try
        {
            using var argon = new Argon2id(pwd)
            {
                Salt = salt.ToArray(),
                MemorySize = (int)p.MemoryKiB,
                Iterations = (int)p.Iterations,
                DegreeOfParallelism = (int)p.Parallelism,
            };
            return argon.GetBytes(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwd);
        }
    }
}

using PasswordKeeper.Core.Cloud;

namespace PasswordKeeper.Core.Vault;

/// <summary>The decrypted contents of a vault file.</summary>
public sealed class VaultData
{
    public int SchemaVersion { get; set; } = 1;
    public List<VaultEntry> Entries { get; set; } = new();

    /// <summary>Backblaze settings; stored here so they are encrypted and travel with the vault.</summary>
    public CloudBackupSettings? CloudBackup { get; set; }
}

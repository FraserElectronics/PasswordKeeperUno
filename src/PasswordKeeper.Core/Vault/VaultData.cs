namespace PasswordKeeper.Core.Vault;

/// <summary>The decrypted contents of a vault file.</summary>
public sealed class VaultData
{
    public int SchemaVersion { get; set; } = 1;
    public List<VaultEntry> Entries { get; set; } = new();
}

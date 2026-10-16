namespace PasswordKeeper.Core.Vault;

public static class VaultAudit
{
    /// <summary>Other entries that use exactly this password.</summary>
    public static List<VaultEntry> EntriesSharingPassword(string password, Guid excludeId, IEnumerable<VaultEntry> all) =>
        password.Length == 0
            ? new List<VaultEntry>()
            : all.Where(e => e.Id != excludeId && e.Password == password).ToList();
}

namespace PasswordKeeper.Core.Vault;

/// <summary>A previous password for an entry and when it was replaced.</summary>
public sealed class PasswordHistoryItem
{
    public string Password { get; set; } = "";
    public DateTimeOffset ChangedUtc { get; set; } = DateTimeOffset.UtcNow;
}

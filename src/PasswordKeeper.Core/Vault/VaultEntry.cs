namespace PasswordKeeper.Core.Vault;

/// <summary>A single saved login: title, URL, username, password and free-text notes.</summary>
public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Previous passwords, newest first. Absent in older vault files (loads as empty).</summary>
    public List<PasswordHistoryItem> PasswordHistory { get; set; } = new();

    public const int MaxHistory = 10;

    /// <summary>Changes the password, recording the old one in <see cref="PasswordHistory"/>.</summary>
    public void SetPassword(string newPassword)
    {
        if (newPassword == Password) return;
        if (Password.Length > 0)
        {
            PasswordHistory.Insert(0, new PasswordHistoryItem { Password = Password });
            if (PasswordHistory.Count > MaxHistory)
                PasswordHistory.RemoveRange(MaxHistory, PasswordHistory.Count - MaxHistory);
        }
        Password = newPassword;
    }
}

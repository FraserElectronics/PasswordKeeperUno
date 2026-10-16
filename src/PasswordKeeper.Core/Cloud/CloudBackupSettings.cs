using System.Text.Json.Serialization;

namespace PasswordKeeper.Core.Cloud;

/// <summary>
/// Backblaze B2 connection details. They are stored inside the encrypted vault, so a copy of the
/// vault file in the cloud carries the settings to every device once it is unlocked.
/// </summary>
public sealed class CloudBackupSettings
{
    /// <summary>The application key ID (not the master key).</summary>
    public string KeyId { get; set; } = "";
    public string ApplicationKey { get; set; } = "";
    public string Bucket { get; set; } = "";
    public string FileName { get; set; } = "vault.pkv";
    /// <summary>Upload again after every successful save.</summary>
    public bool AutoBackup { get; set; }

    [JsonIgnore]
    public bool IsComplete =>
        KeyId.Trim().Length > 0 && ApplicationKey.Trim().Length > 0 &&
        Bucket.Trim().Length > 0 && FileName.Trim().Length > 0;
}

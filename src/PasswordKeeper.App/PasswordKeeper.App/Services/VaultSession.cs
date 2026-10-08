using System.Security.Cryptography;
using System.Text;
using PasswordKeeper.Core.Vault;

namespace PasswordKeeper.App.Services;

/// <summary>
/// Holds the unlocked vault in memory. The master password is kept only so the file can be
/// re-encrypted on save; it is dropped on <see cref="Lock"/>.
/// </summary>
public sealed class VaultSession
{
    private string? _masterPassword;

    public VaultSession(string path) => Path = path;

    public string Path { get; }
    public VaultData? Data { get; private set; }
    public bool IsUnlocked => Data is not null;
    // A zero-length file (some pickers pre-create one) counts as "no vault yet".
    public bool FileExists => File.Exists(Path) && new FileInfo(Path).Length > 0;

    public static string DefaultPath()
    {
        var saved = AppSettings.Load().VaultPath;
        if (!string.IsNullOrEmpty(saved)) return saved;
        var dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PasswordKeeper");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "vault.pkv");
    }

    public Task CreateAsync(string masterPassword) => Task.Run(() =>
    {
        var data = new VaultData();
        VaultFile.Save(Path, data, masterPassword);
        Data = data;
        _masterPassword = masterPassword;
    });

    public Task UnlockAsync(string masterPassword) => Task.Run(() =>
    {
        Data = VaultFile.Load(Path, masterPassword);
        _masterPassword = masterPassword;
    });

    public Task SaveAsync()
    {
        if (Data is null || _masterPassword is null) throw new InvalidOperationException("Vault is locked.");
        var data = Data;
        var pw = _masterPassword;
        return Task.Run(() =>
        {
            BackupIfDue();
            VaultFile.Save(Path, data, pw);
        });
    }

    /// <summary>Re-encrypts the vault under a new master password after verifying the current one.</summary>
    public Task ChangeMasterPasswordAsync(string current, string next)
    {
        if (Data is null || _masterPassword is null) throw new InvalidOperationException("Vault is locked.");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(current), Encoding.UTF8.GetBytes(_masterPassword)))
            throw new VaultAuthenticationException();
        var data = Data;
        return Task.Run(() =>
        {
            BackupIfDue();
            VaultFile.Save(Path, data, next);
            _masterPassword = next;
        });
    }

    /// <summary>
    /// Copies the existing (still encrypted) vault into a "backups" folder beside it once per day,
    /// keeping the newest <see cref="BackupsToKeep"/>. Backups are encrypted with the password in force
    /// when they were made.
    /// </summary>
    private const int BackupsToKeep = 10;

    private void BackupIfDue()
    {
        if (!FileExists) return;
        var dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path)!, "backups");
        Directory.CreateDirectory(dir);
        var name = System.IO.Path.GetFileNameWithoutExtension(Path);
        var target = System.IO.Path.Combine(dir, $"{name}-{DateTime.UtcNow:yyyyMMdd}.pkv");
        if (!File.Exists(target)) File.Copy(Path, target);

        foreach (var old in Directory.GetFiles(dir, $"{name}-*.pkv").OrderByDescending(f => f).Skip(BackupsToKeep))
            File.Delete(old);
    }

    public void Lock()
    {
        Data = null;
        _masterPassword = null;
    }
}

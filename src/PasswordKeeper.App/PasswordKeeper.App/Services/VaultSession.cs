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
    public bool FileExists => File.Exists(Path);

    public static string DefaultPath()
    {
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
        return Task.Run(() => VaultFile.Save(Path, data, pw));
    }

    public void Lock()
    {
        Data = null;
        _masterPassword = null;
    }
}

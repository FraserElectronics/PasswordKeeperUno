using System.Text;
using PasswordKeeper.Core.Cloud;

namespace PasswordKeeper.App.Services;

public enum BackupOutcome
{
    Uploaded,
    /// <summary>The cloud copy differs from what this device last synced, so nothing was uploaded.</summary>
    CloudChanged,
}

/// <summary>Uploads the (already encrypted) vault file to Backblaze and brings it back again.</summary>
public static class CloudBackupService
{
    public static async Task<BackupOutcome> BackupAsync(
        VaultSession session, CloudBackupSettings settings, bool overwriteCloud, CancellationToken ct = default)
    {
        using var client = new B2Client(settings);
        var bytes = session.ReadFileBytes();
        var sha1 = B2Client.Sha1Hex(bytes);
        var state = AppSettings.Load();

        var remote = await client.GetRemoteInfoAsync(ct);
        if (remote is not null && !overwriteCloud)
        {
            if (remote.Sha1 == sha1)
            {
                // Already identical: just remember it.
                state.LastCloudSha1 = sha1;
                state.Save();
                return BackupOutcome.Uploaded;
            }
            if (!string.Equals(remote.Sha1, state.LastCloudSha1, StringComparison.OrdinalIgnoreCase))
                return BackupOutcome.CloudChanged;
        }

        var info = await client.UploadAsync(bytes, ct);
        state.LastCloudSha1 = info.Sha1;
        state.Save();
        return BackupOutcome.Uploaded;
    }

    /// <summary>
    /// Replaces the vault file at <paramref name="path"/> with the cloud copy. The previous file is kept
    /// beside it as ".before-restore". The caller must lock the session afterwards.
    /// </summary>
    public static async Task RestoreAsync(string path, CloudBackupSettings settings, CancellationToken ct = default)
    {
        using var client = new B2Client(settings);
        var data = await client.DownloadAsync(ct);

        if (data.Length < 4 || Encoding.ASCII.GetString(data, 0, 4) != "PKV1")
            throw new B2Exception("That file in the cloud is not a PasswordKeeper vault.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path) && new FileInfo(path).Length > 0)
            File.Copy(path, path + ".before-restore", overwrite: true);

        var temp = path + ".download";
        await File.WriteAllBytesAsync(temp, data, ct);
        File.Move(temp, path, overwrite: true);

        var state = AppSettings.Load();
        state.LastCloudSha1 = B2Client.Sha1Hex(data);
        state.Save();
    }
}

using PasswordKeeper.Core.Vault;
using Xunit;

namespace PasswordKeeper.Core.Tests;

public class VaultFileTests
{
    // Cheap parameters keep the test suite fast; production uses KdfParameters.Default.
    private static readonly KdfParameters Fast = new(1024, 1, 1);

    private static VaultData Sample() => new()
    {
        Entries =
        {
            new VaultEntry
            {
                Title = "Example", Url = "https://example.com", Username = "andy",
                Password = "p@ss wörd ✓", Notes = "line1\nline2",
            },
        },
    };

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = Sample();
        var opened = VaultFile.Open(VaultFile.Seal(original, "master-pw", Fast), "master-pw");

        var e = Assert.Single(opened.Entries);
        Assert.Equal(original.Entries[0].Id, e.Id);
        Assert.Equal("Example", e.Title);
        Assert.Equal("https://example.com", e.Url);
        Assert.Equal("andy", e.Username);
        Assert.Equal("p@ss wörd ✓", e.Password);
        Assert.Equal("line1\nline2", e.Notes);
    }

    [Fact]
    public void WrongPassword_Fails()
    {
        var file = VaultFile.Seal(Sample(), "right", Fast);
        Assert.Throws<VaultAuthenticationException>(() => VaultFile.Open(file, "wrong"));
    }

    [Fact]
    public void TamperedCiphertext_Fails()
    {
        var file = VaultFile.Seal(Sample(), "pw", Fast);
        file[^20] ^= 0x01;
        Assert.Throws<VaultAuthenticationException>(() => VaultFile.Open(file, "pw"));
    }

    [Fact]
    public void TamperedHeader_Fails()
    {
        var file = VaultFile.Seal(Sample(), "pw", Fast);
        file[20] ^= 0x01; // inside the salt
        Assert.Throws<VaultAuthenticationException>(() => VaultFile.Open(file, "pw"));
    }

    [Fact]
    public void NotAVault_IsRejected()
    {
        Assert.Throws<InvalidVaultFormatException>(() => VaultFile.Open(new byte[100], "pw"));
    }

    [Fact]
    public void ExcessiveKdfCost_IsRejectedBeforeAnyWork()
    {
        var file = VaultFile.Seal(Sample(), "pw", Fast);
        file[5] = 0xFF; // memory cost becomes enormous
        Assert.Throws<InvalidVaultFormatException>(() => VaultFile.Open(file, "pw"));
    }

    [Fact]
    public void EachSave_UsesFreshSaltAndNonce()
    {
        var a = VaultFile.Seal(Sample(), "pw", Fast);
        var b = VaultFile.Seal(Sample(), "pw", Fast);
        Assert.NotEqual(a[17..45], b[17..45]);
    }

    [Fact]
    public void PlaintextNeverAppearsInFile()
    {
        var file = VaultFile.Seal(Sample(), "pw", Fast);
        Assert.DoesNotContain("example.com", System.Text.Encoding.UTF8.GetString(file));
    }

    [Fact]
    public void SaveAndLoad_WorkOnDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pkv");
        try
        {
            VaultFile.Save(path, Sample(), "pw", Fast);
            Assert.Single(VaultFile.Load(path, "pw").Entries);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { File.Delete(path); }
    }
}

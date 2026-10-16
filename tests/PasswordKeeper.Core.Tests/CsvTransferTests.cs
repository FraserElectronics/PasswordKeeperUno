using PasswordKeeper.Core.Vault;
using Xunit;

namespace PasswordKeeper.Core.Tests;

public class CsvTransferTests
{
    [Fact]
    public void RoundTrip_PreservesCommasQuotesAndNewlines()
    {
        var original = new[]
        {
            new VaultEntry { Title = "A, \"B\"", Url = "https://x.test", Username = "u", Password = "p,\"q", Notes = "l1\nl2" },
        };
        var back = CsvTransfer.Import(CsvTransfer.Export(original));

        var e = Assert.Single(back);
        Assert.Equal("A, \"B\"", e.Title);
        Assert.Equal("p,\"q", e.Password);
        Assert.Equal("l1\nl2", e.Notes);
    }

    [Fact]
    public void Import_UnderstandsOtherManagersColumnNames()
    {
        var csv = "name,login_uri,login_username,login_password,notes\r\nSite,https://s.test,me,secret,hi\r\n";
        var e = Assert.Single(CsvTransfer.Import(csv));
        Assert.Equal("Site", e.Title);
        Assert.Equal("https://s.test", e.Url);
        Assert.Equal("me", e.Username);
        Assert.Equal("secret", e.Password);
    }

    [Fact]
    public void Import_WithoutRecognisedHeader_Throws() =>
        Assert.Throws<InvalidVaultFormatException>(() => CsvTransfer.Import("foo,bar\r\n1,2\r\n"));

    [Fact]
    public void Export_NeutralisesSpreadsheetFormulas()
    {
        var csv = CsvTransfer.Export(new[] { new VaultEntry { Title = "=1+1" } });
        Assert.Contains("'=1+1", csv);
    }
}

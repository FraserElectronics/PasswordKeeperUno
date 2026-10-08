using PasswordKeeper.Core.Vault;
using Xunit;

namespace PasswordKeeper.Core.Tests;

public class PasswordHealthTests
{
    [Theory]
    [InlineData("", PasswordStrengthLevel.Empty)]
    [InlineData("password1", PasswordStrengthLevel.Weak)]
    [InlineData("12345678", PasswordStrengthLevel.Weak)]
    [InlineData("aaaaaaaaaaaa", PasswordStrengthLevel.Weak)]
    [InlineData("vK9#mQ2$xL7!pR4@nT8&", PasswordStrengthLevel.Strong)]
    public void Strength_ClassifiesExamples(string password, PasswordStrengthLevel expected) =>
        Assert.Equal(expected, PasswordStrength.Evaluate(password));

    [Fact]
    public void SetPassword_RecordsPreviousPasswordNewestFirst()
    {
        var e = new VaultEntry { Password = "one" };
        e.SetPassword("two");
        e.SetPassword("three");

        Assert.Equal("three", e.Password);
        Assert.Equal(new[] { "two", "one" }, e.PasswordHistory.Select(h => h.Password));
    }

    [Fact]
    public void SetPassword_SameValue_DoesNotAddHistory()
    {
        var e = new VaultEntry { Password = "same" };
        e.SetPassword("same");
        Assert.Empty(e.PasswordHistory);
    }

    [Fact]
    public void History_IsCappedAtTen()
    {
        var e = new VaultEntry { Password = "p0" };
        for (var i = 1; i <= 15; i++) e.SetPassword("p" + i);
        Assert.Equal(VaultEntry.MaxHistory, e.PasswordHistory.Count);
        Assert.Equal("p14", e.PasswordHistory[0].Password);
    }

    [Fact]
    public void History_SurvivesEncryptionRoundTrip()
    {
        var e = new VaultEntry { Password = "a" };
        e.SetPassword("b");
        var data = new VaultData { Entries = { e } };
        var back = VaultFile.Open(VaultFile.Seal(data, "pw", new KdfParameters(1024, 1, 1)), "pw");
        Assert.Equal("a", Assert.Single(Assert.Single(back.Entries).PasswordHistory).Password);
    }

    [Fact]
    public void Reuse_FindsOtherEntriesWithSamePassword()
    {
        var a = new VaultEntry { Title = "A", Password = "shared" };
        var b = new VaultEntry { Title = "B", Password = "shared" };
        var c = new VaultEntry { Title = "C", Password = "unique" };

        var others = VaultAudit.EntriesSharingPassword(a.Password, a.Id, new[] { a, b, c });

        Assert.Equal(new[] { "B" }, others.Select(o => o.Title));
        Assert.Empty(VaultAudit.EntriesSharingPassword("", a.Id, new[] { a, b }));
    }
}

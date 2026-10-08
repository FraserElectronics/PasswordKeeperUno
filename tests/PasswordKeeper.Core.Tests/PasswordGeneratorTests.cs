using PasswordKeeper.Core.Generation;
using Xunit;

namespace PasswordKeeper.Core.Tests;

public class PasswordGeneratorTests
{
    [Fact]
    public void DefaultPassword_HasRequestedLengthAndAllClasses()
    {
        for (var i = 0; i < 200; i++)
        {
            var p = PasswordGenerator.Generate();
            Assert.Equal(20, p.Length);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsDigit);
            Assert.Contains(p, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public void DigitsOnly_ProducesOnlyDigits()
    {
        var p = PasswordGenerator.Generate(new PasswordOptions(12, false, false, true, false));
        Assert.All(p, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void NoClassSelected_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            PasswordGenerator.Generate(new PasswordOptions(10, false, false, false, false)));

    [Fact]
    public void Passwords_AreDifferentEachTime() =>
        Assert.NotEqual(PasswordGenerator.Generate(), PasswordGenerator.Generate());
}

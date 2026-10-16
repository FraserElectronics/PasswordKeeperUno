namespace PasswordKeeper.Core.Vault;

public enum PasswordStrengthLevel { Empty, Weak, Fair, Good, Strong }

/// <summary>A deliberately simple strength estimate: size of character pool x effective length.</summary>
public static class PasswordStrength
{
    private static readonly string[] Common =
    {
        "password", "passw0rd", "qwerty", "letmein", "welcome", "admin", "iloveyou",
        "123456", "abc123", "monkey", "dragon", "football", "login",
    };

    public static PasswordStrengthLevel Evaluate(string password)
    {
        if (string.IsNullOrEmpty(password)) return PasswordStrengthLevel.Empty;

        var lower = password.ToLowerInvariant();
        if (password.Length < 16 && Common.Any(c => lower.Contains(c, StringComparison.Ordinal)))
            return PasswordStrengthLevel.Weak;
        if (password.Length < 16 && password.All(char.IsDigit))
            return PasswordStrengthLevel.Weak;

        var pool = 0;
        if (password.Any(char.IsLower)) pool += 26;
        if (password.Any(char.IsUpper)) pool += 26;
        if (password.Any(char.IsDigit)) pool += 10;
        if (password.Any(c => !char.IsLetterOrDigit(c))) pool += 33;

        // Repeated characters add little: cap the effective length at twice the number of distinct ones.
        var effectiveLength = Math.Min(password.Length, password.Distinct().Count() * 2);
        var bits = effectiveLength * Math.Log2(Math.Max(pool, 2));

        return bits switch
        {
            < 40 => PasswordStrengthLevel.Weak,
            < 60 => PasswordStrengthLevel.Fair,
            < 80 => PasswordStrengthLevel.Good,
            _ => PasswordStrengthLevel.Strong,
        };
    }
}

using System.Security.Cryptography;

namespace PasswordKeeper.Core.Generation;

public sealed record PasswordOptions(
    int Length = 20,
    bool Lowercase = true,
    bool Uppercase = true,
    bool Digits = true,
    bool Symbols = true);

/// <summary>Generates random passwords with the OS CSPRNG, free of modulo bias.</summary>
public static class PasswordGenerator
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";   // no 'l'
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";     // no 'I', 'O'
    private const string Digit = "23456789";                      // no '0', '1'
    private const string Symbol = "!@#$%^&*()-_=+[]{};:,.?";

    public static string Generate(PasswordOptions? options = null)
    {
        var o = options ?? new PasswordOptions();
        var sets = new List<string>();
        if (o.Lowercase) sets.Add(Lower);
        if (o.Uppercase) sets.Add(Upper);
        if (o.Digits) sets.Add(Digit);
        if (o.Symbols) sets.Add(Symbol);
        if (sets.Count == 0) throw new ArgumentException("Select at least one character class.", nameof(options));
        if (o.Length < sets.Count || o.Length > 256)
            throw new ArgumentOutOfRangeException(nameof(options), "Length must be between the number of classes and 256.");

        var all = string.Concat(sets);
        var chars = new char[o.Length];
        // One guaranteed character from each selected class, rest from the full pool.
        for (var i = 0; i < chars.Length; i++)
        {
            var pool = i < sets.Count ? sets[i] : all;
            chars[i] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        }
        // Fisher-Yates shuffle so the guaranteed characters are not always first.
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}

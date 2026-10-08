namespace PasswordKeeper.Core.Vault;

/// <summary>The file is not a valid vault, or uses unsupported/unsafe parameters.</summary>
public sealed class InvalidVaultFormatException : Exception
{
    public InvalidVaultFormatException(string message) : base(message) { }
}

/// <summary>
/// Decryption failed: wrong master password or the file was modified.
/// The two cases are deliberately indistinguishable.
/// </summary>
public sealed class VaultAuthenticationException : Exception
{
    public VaultAuthenticationException()
        : base("Wrong master password, or the vault file is corrupted or has been tampered with.") { }
}

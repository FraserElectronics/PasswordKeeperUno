namespace PasswordKeeper.Core.Vault;

/// <summary>Argon2id cost parameters stored in the vault header.</summary>
public readonly record struct KdfParameters(uint MemoryKiB, uint Iterations, uint Parallelism)
{
    // OWASP/RFC 9106 guidance: >= 64 MiB, t >= 3. Mobile devices cope fine with this.
    public static KdfParameters Default => new(64 * 1024, 3, 4);

    // Upper bounds stop a crafted file from forcing a huge allocation on open.
    internal const uint MaxMemoryKiB = 1024 * 1024; // 1 GiB
    internal const uint MaxIterations = 20;
    internal const uint MaxParallelism = 16;

    internal bool IsWithinSafeBounds =>
        MemoryKiB >= 8 * Parallelism && MemoryKiB <= MaxMemoryKiB &&
        Iterations >= 1 && Iterations <= MaxIterations &&
        Parallelism >= 1 && Parallelism <= MaxParallelism;
}

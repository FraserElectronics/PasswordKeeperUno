using System.Text.Json.Serialization;

namespace PasswordKeeper.Core.Vault;

/// <summary>Source-generated JSON metadata, so serialisation survives trimming/AOT on iOS and Android.</summary>
[JsonSerializable(typeof(VaultData))]
internal partial class VaultJsonContext : JsonSerializerContext
{
}

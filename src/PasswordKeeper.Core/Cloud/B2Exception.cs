namespace PasswordKeeper.Core.Cloud;

/// <summary>A Backblaze request failed; the message is safe to show to the user.</summary>
public sealed class B2Exception : Exception
{
    public B2Exception(string message) : base(message) { }
    public B2Exception(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The copy held in the cloud.</summary>
public sealed record CloudFileInfo(string FileId, string Sha1, long Size, DateTimeOffset UploadedUtc);

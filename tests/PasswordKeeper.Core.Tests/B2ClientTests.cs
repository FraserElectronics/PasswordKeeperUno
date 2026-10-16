using System.Net;
using System.Text;
using PasswordKeeper.Core.Cloud;
using PasswordKeeper.Core.Vault;
using Xunit;

namespace PasswordKeeper.Core.Tests;

public class B2ClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<byte[]> Bodies { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken));
            return Respond(request);
        }
    }

    private static CloudBackupSettings Settings() => new()
    {
        KeyId = "key-id", ApplicationKey = "app-key", Bucket = "my-bucket", FileName = "vaults/vault.pkv",
    };

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private const string AuthJson =
        "{\"accountId\":\"acc\",\"authorizationToken\":\"tok\",\"apiUrl\":\"https://api.test\",\"downloadUrl\":\"https://dl.test\"," +
        "\"allowed\":{\"bucketId\":\"bkt1\",\"bucketName\":\"my-bucket\"}}";

    [Fact]
    public async Task Upload_SendsChecksumAndEncodedName()
    {
        var handler = new FakeHandler();
        handler.Respond = req => req.RequestUri!.AbsolutePath switch
        {
            "/b2api/v2/b2_authorize_account" => Json(AuthJson),
            "/b2api/v2/b2_get_upload_url" => Json("{\"uploadUrl\":\"https://up.test/u\",\"authorizationToken\":\"uptok\"}"),
            "/u" => Json("{\"fileId\":\"f1\",\"fileName\":\"vaults/vault.pkv\",\"contentLength\":3,\"uploadTimestamp\":1700000000000}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        using var client = new B2Client(Settings(), handler);

        var data = new byte[] { 1, 2, 3 };
        var info = await client.UploadAsync(data);

        var upload = handler.Requests.Last();
        Assert.Equal("uptok", upload.Headers.GetValues("Authorization").Single());
        Assert.Equal("vaults/vault.pkv", upload.Headers.GetValues("X-Bz-File-Name").Single());
        Assert.Equal(B2Client.Sha1Hex(data), upload.Headers.GetValues("X-Bz-Content-Sha1").Single());
        Assert.Equal(data, handler.Bodies.Last());
        Assert.Equal("f1", info.FileId);
        Assert.Equal(B2Client.Sha1Hex(data), info.Sha1);
    }

    [Fact]
    public async Task GetRemoteInfo_ReturnsNullWhenNothingThere()
    {
        var handler = new FakeHandler();
        handler.Respond = req => req.RequestUri!.AbsolutePath switch
        {
            "/b2api/v2/b2_authorize_account" => Json(AuthJson),
            "/b2api/v2/b2_list_file_names" => Json("{\"files\":[]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        using var client = new B2Client(Settings(), handler);
        Assert.Null(await client.GetRemoteInfoAsync());
    }

    [Fact]
    public async Task GetRemoteInfo_ReadsChecksumOfMatchingFile()
    {
        var handler = new FakeHandler();
        handler.Respond = req => req.RequestUri!.AbsolutePath switch
        {
            "/b2api/v2/b2_authorize_account" => Json(AuthJson),
            "/b2api/v2/b2_list_file_names" => Json(
                "{\"files\":[{\"fileId\":\"f9\",\"fileName\":\"vaults/vault.pkv\",\"contentSha1\":\"ABC123\",\"contentLength\":10,\"uploadTimestamp\":1700000000000}]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        using var client = new B2Client(Settings(), handler);
        var info = await client.GetRemoteInfoAsync();
        Assert.NotNull(info);
        Assert.Equal("abc123", info!.Sha1);
        Assert.Equal("f9", info.FileId);
    }

    [Fact]
    public async Task Download_RejectsCorruptedData()
    {
        var handler = new FakeHandler();
        handler.Respond = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/b2api/v2/b2_authorize_account") return Json(AuthJson);
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 9, 9, 9 }) };
            resp.Headers.TryAddWithoutValidation("X-Bz-Content-Sha1", new string('0', 40));
            return resp;
        };
        using var client = new B2Client(Settings(), handler);
        await Assert.ThrowsAsync<B2Exception>(() => client.DownloadAsync());
    }

    [Fact]
    public async Task Download_ReturnsBytesAndUsesBucketPath()
    {
        var data = new byte[] { 5, 6, 7 };
        var handler = new FakeHandler();
        handler.Respond = req =>
        {
            if (req.RequestUri!.AbsolutePath == "/b2api/v2/b2_authorize_account") return Json(AuthJson);
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
            resp.Headers.TryAddWithoutValidation("X-Bz-Content-Sha1", B2Client.Sha1Hex(data));
            return resp;
        };
        using var client = new B2Client(Settings(), handler);
        Assert.Equal(data, await client.DownloadAsync());
        Assert.Equal("/file/my-bucket/vaults/vault.pkv", handler.Requests.Last().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task BadCredentials_GiveFriendlyMessage()
    {
        var handler = new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) };
        using var client = new B2Client(Settings(), handler);
        var ex = await Assert.ThrowsAsync<B2Exception>(() => client.GetRemoteInfoAsync());
        Assert.Contains("not accepted", ex.Message);
    }

    [Fact]
    public async Task KeyForOtherBucket_IsReported()
    {
        var handler = new FakeHandler { Respond = _ => Json(AuthJson.Replace("my-bucket", "other")) };
        using var client = new B2Client(Settings(), handler);
        var ex = await Assert.ThrowsAsync<B2Exception>(() => client.GetRemoteInfoAsync());
        Assert.Contains("only works with bucket", ex.Message);
    }

    [Fact]
    public void IncompleteSettings_AreRejected() =>
        Assert.Throws<B2Exception>(() => new B2Client(new CloudBackupSettings()));

    [Fact]
    public void CloudSettings_SurviveSealAndOpen()
    {
        var data = new VaultData { CloudBackup = Settings() };
        data.CloudBackup.AutoBackup = true;
        var back = VaultFile.Open(VaultFile.Seal(data, "master-pw", new KdfParameters(1024, 1, 1)), "master-pw");
        Assert.NotNull(back.CloudBackup);
        Assert.Equal("my-bucket", back.CloudBackup!.Bucket);
        Assert.True(back.CloudBackup.AutoBackup);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The profile picture: upload, fetch, cache and remove.
/// </summary>
/// <remarks>
/// The bytes are never interpreted - the server validates the declared content type and the
/// length and stores what it was given. Resizing happens on the client, because the only
/// .NET image libraries worth using either pull in native dependencies or are not
/// AGPL-compatible (CLAUDE.md section 10 rules out ImageSharp's Split License).
/// </remarks>
public sealed class AvatarTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    /// <summary>An 8x8 PNG. Small, but a real one rather than random bytes.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAIAQMAAAD+wSzIAAAABlBMVEX///+/v7+jQ3Y5AAAADklEQVQI"
        + "12P4AIX8EAgALgAD/aNpbtEAAAAASUVORK5CYII=");

    [Fact]
    public async Task An_account_starts_with_no_picture()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.GetAsync("/api/account/avatar");

        // 404 rather than an empty 200: having no picture is the default, and the client
        // draws initials instead.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Null(me!.AvatarUpdatedUtc);
    }

    [Fact]
    public async Task An_uploaded_picture_comes_back_byte_for_byte()
    {
        using var client = await RegisterOwnerAsync();

        using var upload = await UploadAsync(client, TinyPng, "image/png");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        using var fetched = await client.GetAsync("/api/account/avatar");

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TinyPng, await fetched.Content.ReadAsByteArrayAsync());
    }

    /// <summary>
    /// The regression this file exists for.
    /// </summary>
    /// <remarks>
    /// A .NET tick is 100ns; a Postgres timestamptz keeps microseconds. Writing UtcNow
    /// straight through meant the upload reported one more digit than /me returned
    /// afterwards, so a client comparing its cached marker against /me saw a difference every
    /// single time and re-downloaded the picture forever - the exact opposite of what the
    /// marker is for. Found by running it, not by reading it.
    /// </remarks>
    [Fact]
    public async Task The_marker_returned_by_upload_is_the_one_me_reports()
    {
        using var client = await RegisterOwnerAsync();

        using var upload = await UploadAsync(client, TinyPng, "image/png");
        var uploaded = await upload.Content.ReadFromJsonAsync<AvatarResponse>();

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");

        Assert.Equal(uploaded!.UpdatedUtc, me!.AvatarUpdatedUtc);
    }

    [Fact]
    public async Task A_matching_etag_gets_a_304_instead_of_the_image()
    {
        using var client = await RegisterOwnerAsync();
        using var upload = await UploadAsync(client, TinyPng, "image/png");

        using var first = await client.GetAsync("/api/account/avatar");
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/account/avatar");
        request.Headers.IfNoneMatch.Add(etag);

        using var second = await client.SendAsync(request);

        // The phone re-checks on every launch, so this is the common case, not the rare one.
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Re_uploading_changes_the_etag()
    {
        using var client = await RegisterOwnerAsync();

        await UploadAsync(client, TinyPng, "image/png");
        using var first = await client.GetAsync("/api/account/avatar");

        // Far enough apart to be distinct at microsecond precision.
        await Task.Delay(10);

        await UploadAsync(client, TinyPng, "image/png");
        using var second = await client.GetAsync("/api/account/avatar");

        // Same bytes, but the user asked for a change, so a cached copy must be refetched.
        Assert.NotEqual(first.Headers.ETag, second.Headers.ETag);
    }

    [Theory]
    // The one that matters: SVG is a document that can carry script, and these bytes are
    // handed straight back to whatever renders them.
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    public async Task Only_the_allowed_image_types_are_stored(string contentType)
    {
        using var client = await RegisterOwnerAsync();

        using var response = await UploadAsync(client, TinyPng, contentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_upload_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        var tooBig = new byte[AvatarRules.MaxBytes + 1];

        using var response = await UploadAsync(client, tooBig, "image/png");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_removes_the_picture_and_clears_the_marker()
    {
        using var client = await RegisterOwnerAsync();
        await UploadAsync(client, TinyPng, "image/png");

        using var deleted = await client.DeleteAsync("/api/account/avatar");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var fetched = await client.GetAsync("/api/account/avatar");
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Null(me!.AvatarUpdatedUtc);
    }

    [Fact]
    public async Task Deleting_a_picture_that_is_not_there_still_succeeds()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.DeleteAsync("/api/account/avatar");

        // The caller wanted no picture, and there is no picture. Idempotent on purpose:
        // retrying a delete that already worked should not look like a failure.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Someone_elses_picture_is_not_reachable()
    {
        using var owner = await RegisterOwnerAsync();
        await UploadAsync(owner, TinyPng, "image/png");

        var invite = await CreateInviteAsync(owner);

        using var member = Factory.NewClient();
        using var registered = await member.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                Email = "member@example.test",
                Password = OwnerPassword,
                InviteToken = invite,
            });
        registered.EnsureSuccessStatusCode();

        using var response = await member.GetAsync("/api/account/avatar");

        // The route carries no user id - it is always "mine" - so this is really a check that
        // it stays that way if someone adds one.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Signed_out_callers_get_nothing()
    {
        await RegisterOwnerAsync();

        using var anonymous = Factory.NewClient();

        using var response = await anonymous.GetAsync("/api/account/avatar");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        byte[] content,
        string contentType)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return await client.PutAsync("/api/account/avatar", body);
    }
}

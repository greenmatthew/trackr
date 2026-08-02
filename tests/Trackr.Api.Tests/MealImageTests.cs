using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trackr.Api.Data;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Meal photos: stored untouched, claimed by an entry, and never leaking into a listing.
/// </summary>
/// <remarks>
/// As with the avatar, the bytes are never interpreted - the server checks the declared content
/// type and the length and stores exactly what it was given. Nothing here decodes an image, which
/// is deliberate: decoders are a remote-code-execution surface and this milestone adds none.
/// </remarks>
public sealed class MealImageTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    /// <summary>An 8x8 PNG. Small, but a real one rather than random bytes.</summary>
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAIAQMAAAD+wSzIAAAABlBMVEX///+/v7+jQ3Y5AAAADklEQVQI"
        + "12P4AIX8EAgALgAD/aNpbtEAAAAASUVORK5CYII=");

    [Fact]
    public async Task An_uploaded_image_comes_back_byte_for_byte()
    {
        using var client = await RegisterOwnerAsync();

        var uploaded = await UploadAsync(client, TinyPng, "image/png");

        using var fetched = await client.GetAsync($"/api/images/{uploaded.Id}");

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);

        // Full resolution, no re-encoding: a future rescan against a better model has to see the
        // photo the camera took.
        Assert.Equal(TinyPng, await fetched.Content.ReadAsByteArrayAsync());
        Assert.Equal(TinyPng.Length, uploaded.ByteCount);
    }

    [Fact]
    public async Task An_unattached_image_can_be_claimed_by_a_log_entry()
    {
        using var client = await RegisterOwnerAsync();
        var food = await FoodCatalogTests.CreateAsync(client, Payloads.Food());
        var image = await UploadAsync(client, TinyPng, "image/png");

        using var response = await client.PostAsJsonAsync(
            "/api/log",
            Payloads.LogOf(food, imageIds: image.Id));

        response.EnsureSuccessStatusCode();

        var entry = await response.Content.ReadFromJsonAsync<LogEntryResponse>();

        Assert.Equal(image.Id, Assert.Single(entry!.Images).Id);
    }

    /// <remarks>
    /// The upload-then-confirm order is what makes a photo claimable at all, and it is also what
    /// makes claiming it twice a mistake worth catching: two entries sharing one photo would give
    /// deleting either of them the power to remove the other's.
    /// </remarks>
    [Fact]
    public async Task Claiming_an_image_twice_is_refused()
    {
        using var client = await RegisterOwnerAsync();
        var food = await FoodCatalogTests.CreateAsync(client, Payloads.Food());
        var image = await UploadAsync(client, TinyPng, "image/png");

        using var first = await client.PostAsJsonAsync("/api/log", Payloads.LogOf(food, imageIds: image.Id));
        first.EnsureSuccessStatusCode();

        using var second = await client.PostAsJsonAsync("/api/log", Payloads.LogOf(food, imageIds: image.Id));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Someone_elses_image_cannot_be_claimed_or_read()
    {
        using var owner = await RegisterOwnerAsync();
        var image = await UploadAsync(owner, TinyPng, "image/png");

        using var member = await RegisterMemberAsync(owner, "member@example.test");
        var food = await FoodCatalogTests.CreateAsync(member, Payloads.Food());

        using var claimed = await member.PostAsJsonAsync(
            "/api/log",
            Payloads.LogOf(food, imageIds: image.Id));

        using var read = await member.GetAsync($"/api/images/{image.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, claimed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    /// <summary>Both halves of the nullable foreign key's cascade, in one test.</summary>
    [Fact]
    public async Task Deleting_an_entry_deletes_its_images_and_leaves_unattached_ones_alone()
    {
        using var client = await RegisterOwnerAsync();
        var food = await FoodCatalogTests.CreateAsync(client, Payloads.Food());

        var attached = await UploadAsync(client, TinyPng, "image/png");
        var loose = await UploadAsync(client, TinyPng, "image/png");

        using var created = await client.PostAsJsonAsync(
            "/api/log",
            Payloads.LogOf(food, imageIds: attached.Id));
        var entry = await created.Content.ReadFromJsonAsync<LogEntryResponse>();

        using var deleted = await client.DeleteAsync($"/api/log/{entry!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var gone = await client.GetAsync($"/api/images/{attached.Id}");
        using var survives = await client.GetAsync($"/api/images/{loose.Id}");

        // Cascade fires only for non-null foreign keys, so a photo still waiting to be confirmed
        // is untouched by somebody deleting an unrelated entry.
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal(HttpStatusCode.OK, survives.StatusCode);
    }

    /// <summary>
    /// Guards the projection rule: a regression here would put megabytes into every log response.
    /// </summary>
    [Fact]
    public async Task Listing_a_log_entry_does_not_return_image_bytes()
    {
        using var client = await RegisterOwnerAsync();
        var food = await FoodCatalogTests.CreateAsync(client, Payloads.Food());
        var image = await UploadAsync(client, TinyPng, "image/png");

        using var created = await client.PostAsJsonAsync(
            "/api/log",
            Payloads.LogOf(food, imageIds: image.Id));
        created.EnsureSuccessStatusCode();

        using var listed = await client.GetAsync("/api/log");
        var body = await listed.Content.ReadAsStringAsync();

        var entries = await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log");
        var metadata = Assert.Single(Assert.Single(entries!).Images);

        Assert.Equal(image.Id, metadata.Id);
        Assert.Equal(TinyPng.Length, metadata.ByteCount);

        // The base64 of the stored bytes must not appear anywhere in the response.
        Assert.DoesNotContain(Convert.ToBase64String(TinyPng), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_oversized_upload_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        var tooBig = new byte[MealImageRules.MaxBytes + 1];

        using var response = await PostAsync(client, tooBig, "image/webp");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Theory]
    // SVG is the one that matters: a document that can carry script, handed straight back to
    // whatever renders it.
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/octet-stream")]
    public async Task A_disallowed_content_type_is_refused(string contentType)
    {
        using var client = await RegisterOwnerAsync();

        using var response = await PostAsync(client, TinyPng, contentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_deleted_image_is_gone_and_deleting_it_again_still_succeeds()
    {
        using var client = await RegisterOwnerAsync();
        var image = await UploadAsync(client, TinyPng, "image/png");

        using var first = await client.DeleteAsync($"/api/images/{image.Id}");
        using var second = await client.DeleteAsync($"/api/images/{image.Id}");
        using var fetched = await client.GetAsync($"/api/images/{image.Id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }

    [Fact]
    public async Task Replacing_an_entry_detaches_a_dropped_photo_rather_than_destroying_it()
    {
        using var client = await RegisterOwnerAsync();
        var food = await FoodCatalogTests.CreateAsync(client, Payloads.Food());
        var image = await UploadAsync(client, TinyPng, "image/png");

        using var created = await client.PostAsJsonAsync(
            "/api/log",
            Payloads.LogOf(food, imageIds: image.Id));
        var entry = await created.Content.ReadFromJsonAsync<LogEntryResponse>();

        using var replaced = await client.PutAsJsonAsync($"/api/log/{entry!.Id}", Payloads.LogOf(food));
        replaced.EnsureSuccessStatusCode();

        var updated = await replaced.Content.ReadFromJsonAsync<LogEntryResponse>();
        using var stillThere = await client.GetAsync($"/api/images/{image.Id}");

        Assert.Empty(updated!.Images);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();
        var row = await db.MealImages.SingleAsync(candidate => candidate.Id == image.Id);

        Assert.Null(row.LogEntryId);
    }

    private static async Task<MealImageResponse> UploadAsync(
        HttpClient client,
        byte[] content,
        string contentType)
    {
        using var response = await PostAsync(client, content, contentType);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<MealImageResponse>()
            ?? throw new InvalidOperationException("The image endpoint returned no body.");
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        byte[] content,
        string contentType)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        return await client.PostAsync("/api/images", body);
    }
}

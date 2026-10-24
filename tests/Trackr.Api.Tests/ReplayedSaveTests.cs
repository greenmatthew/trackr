using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The server's half of the phone's offline queue: sending the same save twice records one meal.
/// </summary>
/// <remarks>
/// The queue retries with nobody watching, and the failure it has to survive is the one where the
/// write succeeded and the reply was lost - indistinguishable, from the phone, from a request that
/// never arrived. <c>ClientRequestId</c> is what makes the second attempt idempotent instead of a
/// second dinner.
/// </remarks>
public sealed class ReplayedSaveTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task Sending_the_same_save_twice_records_one_meal()
    {
        using var client = await RegisterOwnerAsync();

        var request = Payloads.AdHocLog();
        request.ClientRequestId = Guid.CreateVersion7();

        using var first = await client.PostAsJsonAsync("/api/log", request);
        using var second = await client.PostAsJsonAsync("/api/log", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // 200, not 201: the second request created nothing, and saying otherwise would be a lie a
        // client could reasonably act on.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var created = await first.Content.ReadFromJsonAsync<LogEntryResponse>();
        var replayed = await second.Content.ReadFromJsonAsync<LogEntryResponse>();

        Assert.Equal(created!.Id, replayed!.Id);

        var entries = await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log");

        Assert.Single(entries!);
    }

    /// <summary>
    /// The replay check runs before validation, and this is why.
    /// </summary>
    /// <remarks>
    /// A photo already attached to an entry is refused, correctly - it is how one meal's picture
    /// is kept out of another's. A replay carries the ids the successful attempt attached, so
    /// validating first would answer every retry with a 400 about the photos it had itself stored.
    /// </remarks>
    [Fact]
    public async Task A_replay_carrying_photos_the_first_attempt_attached_still_returns_its_entry()
    {
        using var client = await RegisterOwnerAsync();

        var image = await MealImageTests.UploadAsync(client);

        var request = Payloads.AdHocLog();
        request.ClientRequestId = Guid.CreateVersion7();
        request.ImageIds = [image.Id];

        using var first = await client.PostAsJsonAsync("/api/log", request);
        using var second = await client.PostAsJsonAsync("/api/log", request);

        first.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var replayed = await second.Content.ReadFromJsonAsync<LogEntryResponse>();

        Assert.Equal(image.Id, Assert.Single(replayed!.Images).Id);
    }

    /// <summary>
    /// Two phones on one server pick their own ids and never see each other's.
    /// </summary>
    /// <remarks>
    /// Unique per account rather than globally, so the id can be a plain <c>Guid</c> the client
    /// invents offline. A collision between accounts would otherwise hand one household member
    /// another's meal, which is a great deal worse than a duplicate.
    /// </remarks>
    [Fact]
    public async Task Two_accounts_may_use_the_same_client_request_id()
    {
        using var owner = await RegisterOwnerAsync();
        using var member = await RegisterMemberAsync(owner, "member@example.test");

        var shared = Guid.CreateVersion7();

        var ownersMeal = Payloads.AdHocLog(name: "Porridge");
        ownersMeal.ClientRequestId = shared;

        var membersMeal = Payloads.AdHocLog(name: "Toast");
        membersMeal.ClientRequestId = shared;

        using var ownersSave = await owner.PostAsJsonAsync("/api/log", ownersMeal);
        using var membersSave = await member.PostAsJsonAsync("/api/log", membersMeal);

        Assert.Equal(HttpStatusCode.Created, ownersSave.StatusCode);
        Assert.Equal(HttpStatusCode.Created, membersSave.StatusCode);

        var theirs = await membersSave.Content.ReadFromJsonAsync<LogEntryResponse>();

        Assert.Equal("Toast", Assert.Single(theirs!.Items).Name);
    }

    /// <summary>
    /// No id, no de-duplication - and that is the right default.
    /// </summary>
    /// <remarks>
    /// Two identical meals in a day is a thing people genuinely do, so the server must not decide
    /// on its own that the second one is a mistake. Only a client that says "this is the same save"
    /// gets to be de-duplicated.
    /// </remarks>
    [Fact]
    public async Task Identical_saves_without_an_id_are_two_meals()
    {
        using var client = await RegisterOwnerAsync();

        using var first = await client.PostAsJsonAsync("/api/log", Payloads.AdHocLog());
        using var second = await client.PostAsJsonAsync("/api/log", Payloads.AdHocLog());

        first.EnsureSuccessStatusCode();
        second.EnsureSuccessStatusCode();

        var entries = await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log");

        Assert.Equal(2, entries!.Length);
    }

    /// <summary>
    /// Sending it eight times at once is still one meal.
    /// </summary>
    /// <remarks>
    /// The look-up before the write is two statements, so two requests can both find nothing. The
    /// unique index is what makes that a losing race rather than a second dinner, and the handler
    /// answers the loser with the winner's entry. Which of the two paths a given request takes
    /// depends on timing that cannot be pinned down from out here - what is asserted is the part
    /// that matters either way.
    /// </remarks>
    [Fact]
    public async Task Eight_simultaneous_sends_of_one_meal_store_one_meal()
    {
        using var client = await RegisterOwnerAsync();

        var request = Payloads.AdHocLog();
        request.ClientRequestId = Guid.CreateVersion7();

        var responses = await Task.WhenAll(Enumerable
            .Range(0, 8)
            .Select(_ => client.PostAsJsonAsync("/api/log", request)));

        try
        {
            Assert.All(responses, response => response.EnsureSuccessStatusCode());
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var entries = await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log");

        Assert.Single(entries!);
    }

    /// <summary>
    /// A queued entry carries the moment it was approved, and the server keeps it.
    /// </summary>
    /// <remarks>
    /// The whole point of the queue: a breakfast confirmed in a tunnel at 08:00 and flushed at
    /// 18:00 is still breakfast. The client sends an instant, never a date - which day it lands in
    /// is still decided by the account's time zone on this side of the wire.
    /// </remarks>
    [Fact]
    public async Task A_save_keeps_the_time_the_client_stamped_on_it()
    {
        using var client = await RegisterOwnerAsync();

        var eaten = DateTimeOffset.UtcNow.AddHours(-9);

        var request = Payloads.AdHocLog();
        request.ClientRequestId = Guid.CreateVersion7();
        request.LoggedUtc = eaten;

        using var response = await client.PostAsJsonAsync("/api/log", request);
        response.EnsureSuccessStatusCode();

        var entry = await response.Content.ReadFromJsonAsync<LogEntryResponse>();

        Assert.Equal(eaten, entry!.LoggedUtc, TimeSpan.FromSeconds(1));
        Assert.True(entry.CreatedUtc > entry.LoggedUtc);
    }
}

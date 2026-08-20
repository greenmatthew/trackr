using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Milestone 13: the profile beyond credentials - a name, the units numbers are drawn in, and the
/// optional body measurements behind an energy estimate.
/// </summary>
/// <remarks>
/// Two rules carry most of these. <strong>Units are a display preference</strong>, so nothing the
/// API stores or reports changes when one does; and <strong>an estimate is declined rather than
/// guessed</strong>, because a number built on measurements nobody entered would wear the authority
/// of a calculation without any of its input.
/// </remarks>
public sealed class ProfileTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task A_new_account_has_no_name_no_measurements_and_metric_kilocalories()
    {
        using var client = await RegisterOwnerAsync();

        var profile = await ProfileAsync(client);

        Assert.Null(profile.DisplayName);
        Assert.Equal(EnergyUnit.Kilocalories, profile.EnergyUnit);
        Assert.Equal(MeasurementSystem.Metric, profile.MeasurementSystem);
        Assert.False(profile.Body.HasAnything);
        Assert.Null(profile.Energy);
    }

    /// <remarks>
    /// On <c>/me</c> as well as on the profile, because the phone draws initials and greetings from
    /// its cached account before the server has answered anything.
    /// </remarks>
    [Fact]
    public async Task A_name_and_a_unit_choice_reach_the_account_itself()
    {
        using var client = await RegisterOwnerAsync();

        var saved = await SaveProfileAsync(client, new SaveProfileRequest
        {
            DisplayName = "  Sam  ",
            EnergyUnit = EnergyUnit.Kilojoules,
            MeasurementSystem = MeasurementSystem.Imperial
        });

        // Trimmed, because a trailing space in a greeting is somebody's typo rather than their name.
        Assert.Equal("Sam", saved.DisplayName);

        var me = await MeAsync(client);

        Assert.Equal("Sam", me.DisplayName);
        Assert.Equal(EnergyUnit.Kilojoules, me.EnergyUnit);
        Assert.Equal(MeasurementSystem.Imperial, me.MeasurementSystem);
    }

    [Fact]
    public async Task A_blank_name_puts_the_account_back_to_its_email_address()
    {
        using var client = await RegisterOwnerAsync();

        await SaveProfileAsync(client, new SaveProfileRequest { DisplayName = "Sam" });

        Assert.Null((await SaveProfileAsync(client, new SaveProfileRequest { DisplayName = "   " })).DisplayName);
    }

    /// <summary>
    /// The units change how a client draws a number, and nothing else.
    /// </summary>
    /// <remarks>
    /// The whole of the decision. Converting on the server would mean a stored figure changing
    /// meaning when a setting changed, so a chart of last month would silently redraw - and a
    /// snapshot on a log item exists precisely so that history does not move.
    /// </remarks>
    [Fact]
    public async Task Choosing_kilojoules_changes_nothing_the_api_reports()
    {
        using var client = await RegisterOwnerAsync();

        using var logged = await client.PostAsJsonAsync("/api/log", Payloads.AdHocLog(energyKcal: 500m));
        logged.EnsureSuccessStatusCode();

        await SaveProfileAsync(client, new SaveProfileRequest { EnergyUnit = EnergyUnit.Kilojoules });

        var stats = await client.GetFromJsonAsync<StatsResponse>("/api/stats");

        Assert.Equal(500m, stats!.Total.EnergyKcal);
    }

    [Fact]
    public async Task Body_measurements_are_stored_in_metric_and_rounded_to_what_the_column_keeps()
    {
        using var client = await RegisterOwnerAsync();

        var profile = await SaveBodyAsync(client, new SaveBodyMetricsRequest
        {
            Sex = BiologicalSex.Female,
            BirthDate = new DateOnly(1990, 5, 17),
            HeightCm = 165.44m,
            WeightKg = 60.06m,
            ActivityLevel = ActivityLevel.Moderate
        });

        // numeric(5,1): the response to a write is what was written, not what was sent.
        Assert.Equal(165.4m, profile.Body.HeightCm);
        Assert.Equal(60.1m, profile.Body.WeightKg);
        Assert.Equal(new DateOnly(1990, 5, 17), profile.Body.BirthDate);
        Assert.Equal(ActivityLevel.Moderate, profile.Body.ActivityLevel);
    }

    /// <remarks>
    /// The reason the body write is wholesale rather than a merge: a request where every field is
    /// optional cannot tell "leave my weight alone" from "I want no weight stored", and this is
    /// exactly the data somebody may want gone.
    /// </remarks>
    [Fact]
    public async Task Sending_nothing_is_how_measurements_are_cleared()
    {
        using var client = await RegisterOwnerAsync();

        await SaveBodyAsync(client, new SaveBodyMetricsRequest
        {
            Sex = BiologicalSex.Male,
            BirthDate = new DateOnly(1990, 5, 17),
            HeightCm = 180m,
            WeightKg = 80m
        });

        var cleared = await SaveBodyAsync(client, new SaveBodyMetricsRequest());

        Assert.False(cleared.Body.HasAnything);
        Assert.Null(cleared.Energy);
    }

    /// <summary>
    /// Mifflin-St Jeor, on a case worked by hand.
    /// </summary>
    /// <remarks>
    /// A woman of 30, 165 cm and 60 kg: 600 + 1031.25 - 150 - 161 = 1320.25 kcal at rest, and
    /// sedentary multiplies it by 1.2. Whole kilocalories, because the equation's own error bar is
    /// in the hundreds and decimal places would be precision it does not have.
    /// </remarks>
    [Fact]
    public async Task The_estimate_is_the_published_equation_and_not_a_variation_on_it()
    {
        using var client = await RegisterOwnerAsync();

        var profile = await SaveBodyAsync(client, new SaveBodyMetricsRequest
        {
            Sex = BiologicalSex.Female,
            BirthDate = ThirtyYearsAgo(),
            HeightCm = 165m,
            WeightKg = 60m,
            ActivityLevel = ActivityLevel.Sedentary
        });

        Assert.Equal(1_320m, profile.Energy!.BasalKcal);
        Assert.Equal(1_584m, profile.Energy.MaintenanceKcal);
    }

    /// <remarks>
    /// The sex term is the two constants in the equation and nothing else, which is why it is
    /// documented as a coefficient: the same body with the male constant is 166 kcal apart.
    /// </remarks>
    [Fact]
    public async Task The_two_constants_are_the_whole_of_what_the_sex_term_does()
    {
        using var client = await RegisterOwnerAsync();

        var profile = await SaveBodyAsync(client, new SaveBodyMetricsRequest
        {
            Sex = BiologicalSex.Male,
            BirthDate = ThirtyYearsAgo(),
            HeightCm = 165m,
            WeightKg = 60m,
            ActivityLevel = ActivityLevel.Sedentary
        });

        Assert.Equal(1_486m, profile.Energy!.BasalKcal);
    }

    /// <summary>
    /// A measurement missing means no estimate, never a filled-in average.
    /// </summary>
    /// <remarks>
    /// The refusal is the point. CLAUDE.md section 9.12 is explicit that a hand-typed target must
    /// always work, so declining to guess costs nothing and inventing a height would put a number
    /// in front of somebody's day that nothing supports.
    /// </remarks>
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task An_estimate_needs_every_measurement_it_uses(bool sex, bool height, bool weight)
    {
        using var client = await RegisterOwnerAsync();

        var profile = await SaveBodyAsync(client, new SaveBodyMetricsRequest
        {
            Sex = sex ? BiologicalSex.Female : BiologicalSex.Unspecified,
            BirthDate = ThirtyYearsAgo(),
            HeightCm = height ? 165m : null,
            WeightKg = weight ? 60m : null
        });

        Assert.Null(profile.Energy);
    }

    [Fact]
    public async Task A_birth_date_that_has_not_happened_yet_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await PutBodyAsync(client, new SaveBodyMetricsRequest
        {
            BirthDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("birthDate", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// Wide bounds, catching a slipped decimal point rather than an unusual body: 8 kg is a
    /// mistyped 80, and no bound tight enough to be interesting is one worth telling somebody real.
    /// </remarks>
    [Fact]
    public async Task A_weight_outside_the_bounds_is_refused_rather_than_stored()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await PutBodyAsync(client, new SaveBodyMetricsRequest { WeightKg = 8m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("weightKg", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// The string form of an enum is rejected by the deserialiser when it is not a member; a raw
    /// number is not, and would be stored as text that reads back as nothing at all.
    /// </remarks>
    [Fact]
    public async Task A_number_that_is_not_one_of_the_choices_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.PutAsJsonAsync(
            "/api/account/profile",
            new { displayName = (string?)null, energyUnit = 7, measurementSystem = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <remarks>
    /// The most personal data in the application, and one account's copy of it must not be visible
    /// from - or moved by - another.
    /// </remarks>
    [Fact]
    public async Task A_profile_belongs_to_one_account()
    {
        using var owner = await RegisterOwnerAsync();

        await SaveProfileAsync(owner, new SaveProfileRequest
        {
            DisplayName = "Sam",
            EnergyUnit = EnergyUnit.Kilojoules
        });

        await SaveBodyAsync(owner, new SaveBodyMetricsRequest { WeightKg = 80m });

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        var profile = await ProfileAsync(member);

        Assert.Null(profile.DisplayName);
        Assert.Equal(EnergyUnit.Kilocalories, profile.EnergyUnit);
        Assert.Null(profile.Body.WeightKg);
    }

    /// <summary>Exactly thirty, so the arithmetic above has one age and not two.</summary>
    private static DateOnly ThirtyYearsAgo() =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-30);

    private static async Task<ProfileResponse> ProfileAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ProfileResponse>("/api/account/profile"))!;

    private static async Task<MeResponse> MeAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!;

    private static async Task<ProfileResponse> SaveProfileAsync(
        HttpClient client,
        SaveProfileRequest request)
    {
        using var response = await client.PutAsJsonAsync("/api/account/profile", request);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
    }

    private static Task<HttpResponseMessage> PutBodyAsync(
        HttpClient client,
        SaveBodyMetricsRequest request) =>
        client.PutAsJsonAsync("/api/account/body", request);

    private static async Task<ProfileResponse> SaveBodyAsync(
        HttpClient client,
        SaveBodyMetricsRequest request)
    {
        using var response = await PutBodyAsync(client, request);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ProfileResponse>())!;
    }
}

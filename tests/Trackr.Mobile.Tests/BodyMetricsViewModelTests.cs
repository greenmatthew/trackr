using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.ViewModels;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Mobile.Tests;

/// <summary>
/// The body measurements screen: optional throughout, metric on the wire, and honest about what
/// it can and cannot estimate.
/// </summary>
public sealed class BodyMetricsViewModelTests
{
    [Fact]
    public async Task An_account_with_no_measurements_shows_an_empty_form_and_no_estimate()
    {
        var (body, api) = Build();

        api.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(Profile());

        await body.LoadCommand.ExecuteAsync(null);

        Assert.False(body.HasBirthDate);
        Assert.Equal(string.Empty, body.HeightCmText);
        Assert.Equal(string.Empty, body.WeightText);
        Assert.False(body.HasEstimate);
        Assert.Null(body.Problem);
    }

    /// <summary>
    /// Feet, inches and pounds are converted here; the server is only ever told metric.
    /// </summary>
    /// <remarks>
    /// Two units for one column is how a weight becomes ambiguous, which is why the conversion is
    /// on this side of the wire and the contract has one unit per field.
    /// </remarks>
    [Fact]
    public async Task Imperial_measurements_reach_the_server_in_centimetres_and_kilograms()
    {
        var (body, api) = Build(MeasurementSystem.Imperial);

        api.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(Profile());
        api.SaveBodyMetricsAsync(Arg.Any<SaveBodyMetricsRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Profile(Body((SaveBodyMetricsRequest)call[0])));

        await body.LoadCommand.ExecuteAsync(null);

        body.HeightFeetText = "5";
        body.HeightInchesText = "9";
        body.WeightText = "165";

        await body.SaveCommand.ExecuteAsync(null);

        var request = Sent(api);

        // 5 ft 9 in is 175.26 cm, kept to the one decimal place the column holds.
        Assert.Equal(175.3m, request.HeightCm);
        Assert.Equal(74.8m, request.WeightKg);
    }

    /// <remarks>
    /// The classic way "5 ft 12 in" reaches a screen: 152.4 cm is exactly 5 ft, but a height a
    /// fraction under it rounds its inches up to twelve.
    /// </remarks>
    [Fact]
    public async Task A_height_that_rounds_to_twelve_inches_becomes_another_foot()
    {
        var (body, api) = Build(MeasurementSystem.Imperial);

        api.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(Profile(Body(heightCm: 152.3m)));

        await body.LoadCommand.ExecuteAsync(null);

        Assert.Equal("5", body.HeightFeetText);
        Assert.Equal("0", body.HeightInchesText);
    }

    /// <remarks>
    /// Blank is a complete answer - "I am not telling you my weight" - and it must reach the
    /// server as a null rather than as a zero.
    /// </remarks>
    [Fact]
    public async Task Clearing_the_form_sends_nulls_rather_than_zeroes()
    {
        var (body, api) = Build();

        api.GetProfileAsync(Arg.Any<CancellationToken>())
            .Returns(Profile(Body(heightCm: 180m, weightKg: 80m, sex: BiologicalSex.Male)));

        api.SaveBodyMetricsAsync(Arg.Any<SaveBodyMetricsRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Profile(Body((SaveBodyMetricsRequest)call[0])));

        await body.LoadCommand.ExecuteAsync(null);
        await body.ClearCommand.ExecuteAsync(null);

        var request = Sent(api);

        Assert.Null(request.HeightCm);
        Assert.Null(request.WeightKg);
        Assert.Null(request.BirthDate);
        Assert.Equal(BiologicalSex.Unspecified, request.Sex);
    }

    /// <remarks>
    /// A date picker always has a value, and "today" is not an answer anybody meant to give - so
    /// nothing is sent unless the switch beside it is on.
    /// </remarks>
    [Fact]
    public async Task A_birth_date_is_only_sent_once_it_has_been_switched_on()
    {
        var (body, api) = Build();

        api.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(Profile());
        api.SaveBodyMetricsAsync(Arg.Any<SaveBodyMetricsRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Profile(Body((SaveBodyMetricsRequest)call[0])));

        await body.LoadCommand.ExecuteAsync(null);

        body.BirthDate = new DateTime(1990, 5, 17);

        await body.SaveCommand.ExecuteAsync(null);

        Assert.Null(Sent(api).BirthDate);

        body.HasBirthDate = true;

        await body.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new DateOnly(1990, 5, 17), Sent(api, index: 1).BirthDate);
    }

    /// <remarks>
    /// Blank is allowed; "72kg" with the unit typed in is a mistake to point at rather than a
    /// field to quietly clear, which is what binding a decimal directly would have done.
    /// </remarks>
    [Fact]
    public async Task A_measurement_that_does_not_read_as_a_number_blocks_the_save()
    {
        var (body, api) = Build();

        api.GetProfileAsync(Arg.Any<CancellationToken>()).Returns(Profile());

        await body.LoadCommand.ExecuteAsync(null);

        body.WeightText = "72kg";

        Assert.False(body.CanSave);

        await body.SaveCommand.ExecuteAsync(null);

        await api.DidNotReceive().SaveBodyMetricsAsync(
            Arg.Any<SaveBodyMetricsRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// The estimate is the server's, and it is drawn in the account's own energy unit like every
    /// other figure in the app.
    /// </remarks>
    [Fact]
    public async Task The_estimate_is_shown_in_the_unit_the_account_reads_in()
    {
        var (body, api) = Build(MeasurementSystem.Metric, EnergyUnit.Kilojoules);

        api.GetProfileAsync(Arg.Any<CancellationToken>())
            .Returns(Profile(Body(heightCm: 165m, weightKg: 60m), new EnergyEstimateResponse(1_320m, 1_584m)));

        await body.LoadCommand.ExecuteAsync(null);

        Assert.True(body.HasEstimate);
        Assert.Equal("6627.456 kJ", body.MaintenanceDescription);
    }

    [Fact]
    public async Task An_unreachable_server_is_not_drawn_as_an_account_with_nothing_filled_in()
    {
        var (body, api) = Build();

        api.GetProfileAsync(Arg.Any<CancellationToken>()).Returns((ProfileResponse?)null);

        await body.LoadCommand.ExecuteAsync(null);

        Assert.Contains("Could not reach the server", body.Problem!, StringComparison.Ordinal);
    }

    private static SaveBodyMetricsRequest Sent(ITrackrApiClient api, int index = 0) =>
        api.ReceivedCalls()
            .Select(call => call.GetArguments().FirstOrDefault())
            .OfType<SaveBodyMetricsRequest>()
            .ElementAt(index);

    private static (BodyMetricsViewModel Body, ITrackrApiClient Api) Build(
        MeasurementSystem measurements = MeasurementSystem.Metric,
        EnergyUnit energy = EnergyUnit.Kilocalories)
    {
        var api = Substitute.For<ITrackrApiClient>();

        return (new BodyMetricsViewModel(api, Units.For(energy, measurements)), api);
    }

    private static ProfileResponse Profile(
        BodyMetricsResponse? body = null,
        EnergyEstimateResponse? energy = null) =>
        new(
            null,
            null,
            EnergyUnit.Kilocalories,
            MeasurementSystem.Metric,
            body ?? BodyMetricsResponse.Empty,
            energy);

    private static BodyMetricsResponse Body(
        decimal? heightCm = null,
        decimal? weightKg = null,
        BiologicalSex sex = BiologicalSex.Unspecified,
        DateOnly? birthDate = null) =>
        new(sex, birthDate, heightCm, weightKg, ActivityLevel.Sedentary);

    private static BodyMetricsResponse Body(SaveBodyMetricsRequest request) =>
        new(request.Sex, request.BirthDate, request.HeightCm, request.WeightKg, request.ActivityLevel);
}

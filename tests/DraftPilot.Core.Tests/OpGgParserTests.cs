using DraftPilot.Meta;
using DraftPilot.Meta.OpGg;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Runs against payloads captured from the live OP.GG endpoint, so a format change shows up here
/// rather than in the middle of a draft.
/// </summary>
public class OpGgParserTests
{
    private static string FixtureDirectory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "OpGg");

    private static string Load(string name) => File.ReadAllText(Path.Combine(FixtureDirectory, name));

    [Fact]
    public void ChampionList_IsParsed()
    {
        var root = OpGgResponseParser.Parse(Load("champions.txt"));
        var champions = root["data"]["champions"].Items;

        Assert.NotEmpty(champions);

        var annie = champions[0];
        Assert.Equal(1, annie["champion_id"].AsInt());
        Assert.Equal("Annie", annie["key"].AsText());
        Assert.Equal("Annie", annie["name"].AsText());
    }

    [Fact]
    public void ChampionList_KeepsApostrophesInNames()
    {
        var champions = OpGgResponseParser.Parse(Load("champions.txt"))["data"]["champions"].Items;

        Assert.Contains(champions, champion => champion["name"].AsText() == "Kog'Maw");
    }

    [Fact]
    public void LaneMeta_ExposesAllFiveLanes()
    {
        var positions = OpGgResponseParser.Parse(Load("lane-meta-all.txt"))["data"]["positions"];

        foreach (var lane in new[] { "top", "mid", "jungle", "adc", "support" })
        {
            Assert.NotEmpty(positions[lane].Items);
        }
    }

    /// <summary>
    /// The field set the shipped build asks for, pinned against an older capture. The win rate in
    /// here is the rounded one; since the win counter was added, scoring no longer uses it — see
    /// <see cref="LaneMeta_ReadsTheWinCountBesideTheRoundedRate"/>.
    /// </summary>
    [Fact]
    public void LaneMeta_StillExposesTheRoundedRateWeUsedToScoreOn()
    {
        var top = OpGgResponseParser.Parse(Load("lane-meta-all.txt"))["data"]["positions"]["top"].Items;
        var malphite = top.First(entry => entry["champion"].AsText() == "Malphite");

        Assert.Equal(376, malphite["play"].AsInt());
        Assert.Equal(0.57, malphite["win_rate"].AsNumber(), precision: 3);
        Assert.Equal(0.78, malphite["role_rate"].AsNumber(), precision: 3);
        Assert.Equal(1, malphite["tier"].AsInt());
    }

    [Fact]
    public void LaneMeta_ReadsTheWinCountBesideTheRoundedRate()
    {
        var top = OpGgResponseParser.Parse(Load("lane-meta-win.txt"))["data"]["positions"]["top"].Items;
        var sett = top.First(entry => entry["champion"].AsText() == "Sett");

        Assert.Equal(61702, sett["play"].AsInt());
        Assert.Equal(31118, sett["win"].AsInt());

        // The whole point of asking for the counter: the reported rate cannot tell these apart.
        var darius = top.First(entry => entry["champion"].AsText() == "Darius");
        Assert.Equal(sett["win_rate"].AsNumber(), darius["win_rate"].AsNumber(), precision: 6);
    }

    [Fact]
    public void AResponseWithFieldDiagnostics_NamesTheFieldsThatDidNotMatch()
    {
        var root = OpGgResponseParser.Parse(Load("lane-meta-win.txt"));
        var unmatched = root["_field_diagnostics"]["unmatched_fields"].Items;

        Assert.Equal("data.positions.top[].gibtsnicht", Assert.Single(unmatched).AsText());
    }

    [Fact]
    public void ChampionAnalysis_PositionStatsCarrySampleSizeAndTier()
    {
        var positions = OpGgResponseParser.Parse(Load("analysis-fields.txt"))["data"]["summary"]["positions"].Items;
        var stats = Assert.Single(positions)["stats"];

        Assert.Equal(394825, stats["play"].AsInt());
        Assert.Equal(1, stats["tier_data"]["tier"].AsInt());
        Assert.Equal(0.09, stats["pick_rate"].AsNumber(), precision: 3);
        Assert.Equal(0.07, stats["ban_rate"].AsNumber(), precision: 3);
    }

    [Fact]
    public void ChampionAnalysis_ReportsTheDataVersionItWasBuiltFrom()
    {
        var (version, asOf) = SnapshotBuilder.ReadDataStamp(OpGgResponseParser.Parse(Load("analysis-fields.txt")));

        Assert.Equal("16.17", version);
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 11, 23, 37, TimeSpan.FromHours(9)), asOf);
    }

    /// <summary>
    /// The counter is the one field the analysis endpoint refuses, which is why the fallback lane
    /// rows keep a rounded rate. Pinned so a future capture that suddenly carries it gets noticed.
    /// </summary>
    [Fact]
    public void ChampionAnalysis_DoesNotOfferAWinCountForALanesOverallStats()
    {
        var root = OpGgResponseParser.Parse(Load("analysis-fields.txt"));
        var unmatched = root["_field_diagnostics"]["unmatched_fields"].Items.Select(field => field.AsText());

        Assert.Contains("data.summary.positions[].stats.win", unmatched);
    }

    [Fact]
    public void LaneMeta_SharedTypeNameStillMapsFieldsPerLane()
    {
        // Every lane list is emitted with the first declared type name; positional mapping has to
        // keep working for the later ones too.
        var positions = OpGgResponseParser.Parse(Load("lane-meta-all.txt"))["data"]["positions"];
        var firstMid = positions["mid"].Items[0];

        Assert.Equal("Zed", firstMid["champion"].AsText());
        Assert.True(firstMid["play"].AsInt() > 0);
    }

    [Fact]
    public void ChampionAnalysis_ReadsDamageTypeCountersAndSynergies()
    {
        var data = OpGgResponseParser.Parse(Load("analysis-thresh.txt"))["data"];

        Assert.Equal("AP", data["damage_type"].AsText());

        var weak = data["weak_counters"].Items;
        Assert.NotEmpty(weak);
        Assert.Equal("Morgana", weak[0]["champion_name"].AsText());
        Assert.Equal(116, weak[0]["play"].AsInt());
        Assert.Equal(0.43, weak[0]["my_win_rate"].AsNumber(), precision: 3);

        var synergies = data["synergies"]["adc"].Items;
        Assert.NotEmpty(synergies);
        Assert.Equal("Xayah", synergies[0]["synergy_champion_name"].AsText());
        Assert.Equal(1, synergies[0]["synergy_tier_data"]["tier"].AsInt());
    }

    [Fact]
    public void ChampionAnalysis_PositionStatsAreReachable()
    {
        var positions = OpGgResponseParser.Parse(Load("analysis-thresh.txt"))["data"]["summary"]["positions"].Items;
        var support = positions.First(p => p["name"].AsText() == "SUPPORT");

        Assert.Equal(0.51, support["stats"]["win_rate"].AsNumber(), precision: 3);
        Assert.Equal(0.99, support["stats"]["role_rate"].AsNumber(), precision: 3);
    }

    [Fact]
    public void MissingFields_ReadAsAbsentInsteadOfThrowing()
    {
        // The Yasuo capture has no damage_type and no synergies at all.
        var data = OpGgResponseParser.Parse(Load("analysis-yasuo.txt"))["data"];

        Assert.False(data["damage_type"].Exists);
        Assert.False(data["synergies"]["adc"].Exists);
        Assert.Empty(data["synergies"]["adc"].Items);

        // Deep access through a missing branch must not throw either.
        Assert.False(data["nope"]["still_nope"]["deeper"].Exists);
        Assert.Equal(0, data["nope"]["still_nope"].AsInt());
    }

    [Fact]
    public void Synergies_AreParsedFromTheDedicatedEndpoint()
    {
        var synergies = OpGgResponseParser.Parse(Load("synergies-jhin.txt"))["data"]["synergies"].Items;

        Assert.Equal(10, synergies.Count);
        Assert.Equal("Xerath", synergies[0]["synergy_champion_name"].AsText());
        Assert.Equal(53, synergies[0]["play"].AsInt());
        Assert.Equal(3, synergies[0]["synergy_tier_data"]["tier"].AsInt());
    }

    [Theory]
    [InlineData("class A: x\n\nA(null)", OpGgNodeKind.Null)]
    [InlineData("class A: x\n\nA(None)", OpGgNodeKind.Null)]
    [InlineData("class A: x\n\nA(true)", OpGgNodeKind.Boolean)]
    [InlineData("class A: x\n\nA(\"text\")", OpGgNodeKind.String)]
    [InlineData("class A: x\n\nA(-1.5)", OpGgNodeKind.Number)]
    [InlineData("class A: x\n\nA([])", OpGgNodeKind.List)]
    public void Literals_AreRecognised(string payload, OpGgNodeKind expected)
    {
        Assert.Equal(expected, OpGgResponseParser.Parse(payload)["x"].Kind);
    }

    [Fact]
    public void NegativeAndFractionalNumbers_RoundTrip()
    {
        var node = OpGgResponseParser.Parse("class A: a,b,c\n\nA(-3,0.125,42)");

        Assert.Equal(-3, node["a"].AsInt());
        Assert.Equal(0.125, node["b"].AsNumber(), precision: 6);
        Assert.Equal(42, node["c"].AsInt());
    }

    [Fact]
    public void UndeclaredArguments_StayReachablePositionally()
    {
        // If OP.GG adds a field without declaring it, the value must not vanish silently.
        var node = OpGgResponseParser.Parse("class A: a\n\nA(1,2)");

        Assert.Equal(1, node["a"].AsInt());
        Assert.Equal(2, node["1"].AsInt());
    }

    [Fact]
    public void EmptyPayload_Throws()
    {
        Assert.Throws<OpGgParseException>(() => OpGgResponseParser.Parse("   "));
    }

    [Fact]
    public void HeaderWithoutExpression_Throws()
    {
        Assert.Throws<OpGgParseException>(() => OpGgResponseParser.Parse("class A: a\n"));
    }

    [Fact]
    public void UnclosedConstructor_Throws()
    {
        Assert.Throws<OpGgParseException>(() => OpGgResponseParser.Parse("class A: a\n\nA(1"));
    }
}

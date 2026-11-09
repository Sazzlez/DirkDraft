using DraftPilot.Core.Lcu;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// The E1 probe reports where the client tells the own party — and never a name or an id, whatever
/// the session and the lobby carry (fixed rule: no other player's name).
/// </summary>
public sealed class PartyProbeTests
{
    private const string Session = """
        {"localPlayerCellId":2,"queueId":420,
         "myTeam":[
           {"cellId":0,"gameName":"Fremder","tagLine":"EUW","puuid":"","obfuscatedPuuid":"obf-0","summonerId":0,"nameVisibilityType":"HIDDEN"},
           {"cellId":1,"gameName":"Duopartner","tagLine":"EUW","puuid":"puuid-duo","summonerId":4711,"nameVisibilityType":"VISIBLE"},
           {"cellId":2,"gameName":"Ich","tagLine":"EUW","puuid":"puuid-me","summonerId":42,"nameVisibilityType":"VISIBLE"},
           {"cellId":3,"puuid":"","obfuscatedPuuid":"obf-3","summonerId":0,"nameVisibilityType":"HIDDEN"},
           {"cellId":4,"puuid":"","obfuscatedPuuid":"obf-4","summonerId":0,"nameVisibilityType":"HIDDEN"}],
         "theirTeam":[{"cellId":5,"gameName":"Gegner","nameVisibilityType":"HIDDEN"},{"cellId":6,"nameVisibilityType":"HIDDEN"}]}
        """;

    private const string Lobby = """
        {"members":[{"puuid":"puuid-me","summonerId":42,"gameName":"Ich","isLeader":true},
                    {"puuid":"puuid-duo","summonerId":4711,"gameName":"Duopartner","isLeader":false}]}
        """;

    [Fact]
    public void FindsTheDuo_InLobbyAndVisibility()
    {
        var report = PartyProbe.Report("ChampSelect", Session, Lobby);

        Assert.Contains("Party laut Lobby (ohne dich): Platz 1", report);
        Assert.Contains("Nicht verborgen (ohne dich): Platz 1", report);
        Assert.Contains("→ Beide Quellen stimmen überein.", report);
        Assert.Contains(report, line => line.StartsWith("  Platz 1:") && line.Contains("Sichtbarkeit VISIBLE") && line.Contains("in der Lobby ja"));
        Assert.Contains(report, line => line.StartsWith("  Platz 0:") && line.Contains("puuid leer") && line.Contains("in der Lobby nein"));
        Assert.Contains("Gegner: Sichtbarkeit 2 × HIDDEN", report);
    }

    [Fact]
    public void NeverPrintsANameOrAnId()
    {
        var text = string.Join("\n", PartyProbe.Report("ChampSelect", Session, Lobby));

        foreach (var secret in new[] { "Fremder", "Duopartner", "Ich ", "Gegner,", "puuid-duo", "puuid-me", "obf-0", "obf-3", "4711", "EUW" })
            Assert.DoesNotContain(secret, text);
        Assert.Contains("Felder je Mitglied: gameName, isLeader, puuid, summonerId", text); // field names only
    }

    [Fact]
    public void SaysWhatIsMissing()
    {
        Assert.Contains("Lobby: keine Antwort (404 oder leer)", PartyProbe.Report("ChampSelect", Session, null));
        Assert.Contains("Champ Select: keine Session — die Probe braucht einen laufenden Champ Select.", PartyProbe.Report("Lobby", null, Lobby));
        Assert.Contains("Gameflow-Phase: keine Antwort", PartyProbe.Report(null, null, null));
    }
}

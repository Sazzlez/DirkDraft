using System.Security.Authentication;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using DraftPilot.Core.Lcu;

namespace DraftPilot.Tools;

/// <summary>
/// Walks the client connection one layer at a time and reports where it breaks: install discovery,
/// lockfile, TLS certificate, HTTP, then the event socket. Written because a failure at any of those
/// layers looks identical from the outside — "not connected".
/// </summary>
internal static class ProbeCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        Console.WriteLine("=== 1. Installation ===");
        var install = LeagueInstall.Locate();
        Console.WriteLine(install is null ? "  nicht gefunden" : $"  {install}");

        if (install is null)
            return 1;

        Console.WriteLine();
        Console.WriteLine("=== 2. Lockfile ===");
        var watcher = new LockfileWatcher();
        await watcher.StartAsync(ct);
        var credentials = watcher.Current;
        Console.WriteLine($"  Pfad: {watcher.LockfilePath}");

        if (credentials is null)
        {
            Console.WriteLine("  nicht lesbar oder Client nicht gestartet");
            return 1;
        }

        Console.WriteLine($"  Port {credentials.Port}, Passwort {credentials.Password.Length} Zeichen");

        Console.WriteLine();
        Console.WriteLine("=== 3. Zertifikat ===");
        var pinnedKey = ReportCertificates(credentials);

        Console.WriteLine();
        Console.WriteLine("=== 4. HTTP ===");
        await ReportHttpAsync(credentials, ct);

        Console.WriteLine();
        Console.WriteLine("=== 5. Event-Socket ===");
        await ReportSocketAsync(credentials, pinnedKey, ct);

        watcher.Dispose();
        return 0;
    }

    /// <summary>
    /// Compares the certificate the client presents against the pinned one and returns whether they
    /// match, which is the single most likely reason for a refused connection.
    /// </summary>
    private static bool ReportCertificates(LcuCredentials credentials)
    {
        using var pinned = RiotCertificate.LoadPinnedCertificate();

        if (pinned is null)
        {
            Console.WriteLine("  eingebettetes riotgames.pem: NICHT LESBAR");
            return false;
        }

        Console.WriteLine("  eingebettet:");
        Describe(pinned);

        var presented = FetchPresentedCertificate(credentials);

        if (presented is null)
        {
            Console.WriteLine("  vom Client: TLS-Handshake nicht möglich");
            return false;
        }

        using (presented)
        {
            Console.WriteLine("  vom Client:");
            Describe(presented);

            // The client serves a leaf issued by the pinned authority, so the question is whether
            // the chain builds against it, not whether the keys are identical.
            var (accepted, detail) = RiotCertificate.Inspect(presented);
            Console.WriteLine($"  Kette gegen Riot-CA: {(accepted ? "GUELTIG" : "ABGELEHNT")} ({detail})");
            return accepted;
        }
    }

    private static void Describe(X509Certificate2 certificate)
    {
        var thumb = certificate.Thumbprint;
        var keyHash = Convert.ToHexString(SHA256.HashData(certificate.GetPublicKey()))[..24];

        Console.WriteLine($"    Subject    {certificate.Subject}");
        Console.WriteLine($"    Issuer     {certificate.Issuer}");
        Console.WriteLine($"    Gültig     {certificate.NotBefore:yyyy-MM-dd} .. {certificate.NotAfter:yyyy-MM-dd}");
        Console.WriteLine($"    Thumbprint {thumb}");
        Console.WriteLine($"    PubKey     SHA256 {keyHash}…");
    }

    /// <summary>
    /// Opens a raw TLS stream purely to capture what the client offers. Accepts anything on purpose:
    /// the point is to see the certificate, not to trust it.
    /// </summary>
    private static X509Certificate2? FetchPresentedCertificate(LcuCredentials credentials)
    {
        try
        {
            using var socket = new TcpClient();
            socket.Connect("127.0.0.1", credentials.Port);

            X509Certificate2? captured = null;

            using var ssl = new SslStream(socket.GetStream(), leaveInnerStreamOpen: false, (_, certificate, _, _) =>
            {
                if (certificate is not null)
                    captured = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

                return true;
            });

            ssl.AuthenticateAsClient("127.0.0.1");
            return captured;
        }
        catch (Exception ex) when (ex is SocketException or AuthenticationException or IOException)
        {
            Console.WriteLine($"    ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static async Task ReportHttpAsync(LcuCredentials credentials, CancellationToken ct)
    {
        using var client = new LcuClient(credentials);

        var phase = await client.GetGameflowPhaseAsync(ct);
        Console.WriteLine($"  gameflow-phase (mit Pinning): {phase ?? "keine Antwort"}");

        var session = await client.GetChampSelectSessionRawAsync(ct);
        Console.WriteLine($"  champ-select session:        {(session is null ? "kein Champ Select" : $"{session.Length} Zeichen")}");
    }

    /// <summary>
    /// Tries the event socket the way the app does, and reports the failure verbatim. A pinning
    /// rejection and a protocol problem both surface as "not connected" otherwise.
    /// </summary>
    private static async Task ReportSocketAsync(LcuCredentials credentials, bool pinMatches, CancellationToken ct)
    {
        if (!pinMatches)
            Console.WriteLine("  Hinweis: der Pin passt nicht, ein Fehlschlag hier ist die Folge davon.");

        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("wamp");
        socket.Options.RemoteCertificateValidationCallback = RiotCertificate.ValidateSocket;

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{credentials.Password}"));
        socket.Options.SetRequestHeader("Authorization", $"Basic {token}");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));

            await socket.ConnectAsync(credentials.WebSocketUri, timeout.Token);
            Console.WriteLine($"  verbunden, State={socket.State}, Subprotokoll={socket.SubProtocol ?? "keins"}");

            var frame = Encoding.UTF8.GetBytes("[5,\"OnJsonApiEvent_lol-gameflow_v1_gameflow-phase\"]");
            await socket.SendAsync(frame, WebSocketMessageType.Text, endOfMessage: true, timeout.Token);
            Console.WriteLine("  Abo gesendet");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FEHLER {ex.GetType().Name}: {ex.Message}");

            for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
                Console.WriteLine($"    -> {inner.GetType().Name}: {inner.Message}");
        }
    }
}

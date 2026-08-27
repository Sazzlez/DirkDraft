using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DraftPilot.Core.Lcu;

/// <summary>
/// Validates the certificate the League client presents, instead of accepting whatever answers on
/// loopback.
/// <para>
/// The embedded <c>riotgames.pem</c> is Riot's <em>certificate authority</em>, not the certificate
/// the client serves. The client presents a leaf named <c>CN=rclient</c> that the authority issued.
/// So the check is a chain build against that authority as a custom trust root — comparing public
/// keys directly would compare a CA against a leaf and always fail.
/// </para>
/// <para>
/// Two deviations from ordinary TLS are expected here and are deliberately tolerated: Riot's
/// authority is not in the Windows trust store (that is the entire point of shipping it), and the
/// leaf is named <c>rclient</c> rather than <c>127.0.0.1</c>, so the host name never matches. The
/// chain build against the pinned authority is what decides; the platform's own verdict is not.
/// </para>
/// </summary>
public static class RiotCertificate
{
    private const string ResourceName = "DraftPilot.Core.Lcu.Resources.riotgames.pem";

    private static readonly Lazy<X509Certificate2?> Authority = new(LoadPinnedCertificate, isThreadSafe: true);

    /// <summary>True when the pinned authority was found and can be used.</summary>
    public static bool IsPinAvailable => Authority.Value is not null;

    /// <summary>Callback for <see cref="HttpClientHandler.ServerCertificateCustomValidationCallback"/>.</summary>
    public static bool ValidateHttp(HttpRequestMessage _, X509Certificate2? presented, X509Chain? __, SslPolicyErrors ___)
        => IsIssuedByRiot(presented);

    /// <summary>Callback for <see cref="RemoteCertificateValidationCallback"/> (used by the web socket).</summary>
    public static bool ValidateSocket(object _, X509Certificate? presented, X509Chain? __, SslPolicyErrors ___)
    {
        // Dispose only what AsCertificate2 created. Runs on every reconnect of the backoff loop —
        // undisposed, each handshake leaked an unmanaged certificate handle for hours on end.
        var converted = AsCertificate2(presented);

        try
        {
            return IsIssuedByRiot(converted);
        }
        finally
        {
            if (!ReferenceEquals(converted, presented))
                converted?.Dispose();
        }
    }

    /// <summary>
    /// The pinned authority, for diagnostics. <see langword="null"/> when the embedded resource is
    /// missing or unreadable.
    /// </summary>
    public static X509Certificate2? LoadPinnedCertificate()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);

        try
        {
            return X509Certificate2.CreateFromPem(reader.ReadToEnd());
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the certificate chain against the pinned authority. Returns the chain status alongside
    /// the verdict so the diagnostic command can explain a rejection.
    /// </summary>
    public static (bool Accepted, string Detail) Inspect(X509Certificate2? presented)
    {
        var authority = Authority.Value;

        if (authority is null)
            return (false, "Riot-Zertifizierungsstelle nicht eingebettet");

        if (presented is null)
            return (false, "kein Zertifikat vorgelegt");

        // The client occasionally serves the authority itself; accept that without a chain build.
        if (string.Equals(presented.Thumbprint, authority.Thumbprint, StringComparison.OrdinalIgnoreCase))
            return (true, "Zertifizierungsstelle direkt vorgelegt");

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority);

        // Loopback has no route to a revocation list, and asking for one only adds a timeout.
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags =
            X509VerificationFlags.IgnoreEndRevocationUnknown
            | X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown
            | X509VerificationFlags.IgnoreRootRevocationUnknown;

        if (chain.Build(presented))
            return (true, $"Kette gültig über {chain.ChainElements.Count} Stufen");

        var problems = chain.ChainStatus.Length == 0
            ? "unbekannter Kettenfehler"
            : string.Join(", ", chain.ChainStatus.Select(status => status.Status.ToString()));

        return (false, problems);
    }

    private static bool IsIssuedByRiot(X509Certificate2? presented) => Inspect(presented).Accepted;

    private static X509Certificate2? AsCertificate2(X509Certificate? certificate) => certificate switch
    {
        null => null,
        X509Certificate2 typed => typed,
        _ => X509CertificateLoader.LoadCertificate(certificate.GetRawCertData()),
    };
}

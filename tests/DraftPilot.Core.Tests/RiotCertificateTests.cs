using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DraftPilot.Core.Lcu;
using Xunit;

namespace DraftPilot.Core.Tests;

/// <summary>
/// Guards the certificate check. The original implementation compared public keys, which silently
/// rejected every real connection: the embedded PEM is Riot's certificate authority, while the client
/// serves a leaf that authority issued. These tests pin down what the check must accept and reject.
/// </summary>
public class RiotCertificateTests
{
    [Fact]
    public void PinnedAuthority_IsEmbedded()
    {
        Assert.True(RiotCertificate.IsPinAvailable);

        using var authority = RiotCertificate.LoadPinnedCertificate();
        Assert.NotNull(authority);
        Assert.Contains("LoL Game Engineering Certificate Authority", authority.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedAuthority_IsSelfSigned()
    {
        using var authority = RiotCertificate.LoadPinnedCertificate()!;

        // A certificate authority signs itself; if this ever differs, the embedded file is not a CA
        // and the chain build below would be meaningless.
        Assert.Equal(authority.Subject, authority.Issuer);
    }

    [Fact]
    public void NoCertificate_IsRejected()
    {
        var (accepted, detail) = RiotCertificate.Inspect(null);

        Assert.False(accepted);
        Assert.Contains("kein Zertifikat", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ForeignSelfSignedCertificate_IsRejected()
    {
        // Anything on loopback could answer; only what Riot's authority signed may be trusted.
        using var impostor = CreateSelfSigned("CN=rclient");

        var (accepted, detail) = RiotCertificate.Inspect(impostor);

        Assert.False(accepted);
        Assert.NotEmpty(detail);
    }

    [Fact]
    public void ForeignCertificate_IsRejectedEvenWhenItCopiesTheAuthoritySubject()
    {
        // Naming yourself after the authority is not the same as being signed by it.
        using var authority = RiotCertificate.LoadPinnedCertificate()!;
        using var impostor = CreateSelfSigned(authority.Subject);

        Assert.False(RiotCertificate.Inspect(impostor).Accepted);
    }

    [Fact]
    public void PinnedAuthority_PresentedDirectly_IsAccepted()
    {
        // Some client builds serve the authority itself rather than a leaf.
        using var authority = RiotCertificate.LoadPinnedCertificate()!;

        Assert.True(RiotCertificate.Inspect(authority).Accepted);
    }

    private static X509Certificate2 CreateSelfSigned(string subject)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(30));
    }
}

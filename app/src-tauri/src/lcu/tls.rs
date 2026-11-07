// Accepts exactly the certificates Riot's local services present (port of RiotCertificate.cs), for
// the client (LCU, HTTP and WebSocket) and the game (Live Client Data API on port 2999).
//
// riotgames.pem is Riot's certificate AUTHORITY, an X.509 v1 certificate signed with SHA-1 — old
// enough that the usual Rust chain builder (webpki) refuses it as a trust anchor. So the check is
// done by hand, and it is the same check a two-level chain build makes: the presented certificate's
// signature must verify with the authority's key, and it must be within its validity period. Two
// deviations from ordinary TLS are expected and tolerated, as in 1.5: the authority is in no system
// store, and the client's certificate is named "rclient", never 127.0.0.1. The client sometimes
// presents the authority itself; that is accepted as is.

use std::sync::{Arc, OnceLock};

use rustls::client::danger::{HandshakeSignatureValid, ServerCertVerified, ServerCertVerifier};
use rustls::crypto::ring as provider;
use rustls::pki_types::pem::PemObject;
use rustls::pki_types::{CertificateDer, ServerName, UnixTime};
use rustls::{CertificateError, DigitallySignedStruct, Error, SignatureScheme};
use x509_parser::prelude::{FromDer, X509Certificate};

const AUTHORITY_PEM: &[u8] = include_bytes!("riotgames.pem");

/// The TLS setup every connection to the client or the game uses.
pub fn client_config() -> Arc<rustls::ClientConfig> {
    static CONFIG: OnceLock<Arc<rustls::ClientConfig>> = OnceLock::new();
    CONFIG
        .get_or_init(|| {
            let provider = Arc::new(provider::default_provider());
            let authority = CertificateDer::from_pem_slice(AUTHORITY_PEM).expect("riotgames.pem ist eingebettet und lesbar");
            let config = rustls::ClientConfig::builder_with_provider(provider)
                .with_safe_default_protocol_versions()
                .expect("TLS 1.2/1.3 mit ring")
                .dangerous()
                .with_custom_certificate_verifier(Arc::new(RiotVerifier { authority }))
                .with_no_client_auth();
            Arc::new(config)
        })
        .clone()
}

#[derive(Debug)]
struct RiotVerifier {
    authority: CertificateDer<'static>,
}

/// Whether `presented` is the authority itself or was signed by it and is currently valid.
pub fn issued_by_riot(presented: &[u8], authority: &[u8]) -> bool {
    if presented == authority {
        return true;
    }
    let (Ok((_, leaf)), Ok((_, ca))) = (X509Certificate::from_der(presented), X509Certificate::from_der(authority)) else {
        return false;
    };
    leaf.validity().is_valid() && leaf.verify_signature(Some(ca.public_key())).is_ok()
}

impl ServerCertVerifier for RiotVerifier {
    fn verify_server_cert(
        &self,
        end_entity: &CertificateDer<'_>,
        _intermediates: &[CertificateDer<'_>],
        _server_name: &ServerName<'_>,
        _ocsp_response: &[u8],
        _now: UnixTime,
    ) -> Result<ServerCertVerified, Error> {
        if issued_by_riot(end_entity.as_ref(), self.authority.as_ref()) {
            Ok(ServerCertVerified::assertion())
        } else {
            Err(Error::InvalidCertificate(CertificateError::UnknownIssuer))
        }
    }

    fn verify_tls12_signature(&self, message: &[u8], cert: &CertificateDer<'_>, dss: &DigitallySignedStruct) -> Result<HandshakeSignatureValid, Error> {
        verify_handshake(message, cert, dss)
    }

    fn verify_tls13_signature(&self, message: &[u8], cert: &CertificateDer<'_>, dss: &DigitallySignedStruct) -> Result<HandshakeSignatureValid, Error> {
        verify_handshake(message, cert, dss)
    }

    fn supported_verify_schemes(&self) -> Vec<SignatureScheme> {
        SCHEMES.iter().map(|(scheme, _)| *scheme).collect()
    }
}

/// The handshake signature, checked with the certificate's own key. Also by hand, for the same
/// reason as above: rustls' helper would parse the certificate with webpki first.
fn verify_handshake(message: &[u8], cert: &CertificateDer<'_>, dss: &DigitallySignedStruct) -> Result<HandshakeSignatureValid, Error> {
    let algorithm = SCHEMES
        .iter()
        .find(|(scheme, _)| *scheme == dss.scheme)
        .map(|(_, algorithm)| *algorithm)
        .ok_or(Error::PeerIncompatible(rustls::PeerIncompatible::NoSignatureSchemesInCommon))?;
    let (_, parsed) = X509Certificate::from_der(cert.as_ref()).map_err(|_| Error::InvalidCertificate(CertificateError::BadEncoding))?;
    let key = ring::signature::UnparsedPublicKey::new(algorithm, parsed.public_key().subject_public_key.data.as_ref());
    key.verify(message, dss.signature())
        .map(|_| HandshakeSignatureValid::assertion())
        .map_err(|_| Error::InvalidCertificate(CertificateError::BadSignature))
}

type Algorithm = &'static dyn ring::signature::VerificationAlgorithm;

static SCHEMES: [(SignatureScheme, Algorithm); 8] = [
    (SignatureScheme::RSA_PSS_SHA256, &ring::signature::RSA_PSS_2048_8192_SHA256),
    (SignatureScheme::RSA_PSS_SHA384, &ring::signature::RSA_PSS_2048_8192_SHA384),
    (SignatureScheme::RSA_PSS_SHA512, &ring::signature::RSA_PSS_2048_8192_SHA512),
    (SignatureScheme::RSA_PKCS1_SHA256, &ring::signature::RSA_PKCS1_2048_8192_SHA256),
    (SignatureScheme::RSA_PKCS1_SHA384, &ring::signature::RSA_PKCS1_2048_8192_SHA384),
    (SignatureScheme::RSA_PKCS1_SHA512, &ring::signature::RSA_PKCS1_2048_8192_SHA512),
    (SignatureScheme::ECDSA_NISTP256_SHA256, &ring::signature::ECDSA_P256_SHA256_ASN1),
    (SignatureScheme::ECDSA_NISTP384_SHA384, &ring::signature::ECDSA_P384_SHA384_ASN1),
];

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_authority_is_embedded_and_accepted_as_itself() {
        let authority = CertificateDer::from_pem_slice(AUTHORITY_PEM).unwrap();
        assert!(issued_by_riot(authority.as_ref(), authority.as_ref()));
        // The config builds (provider, protocol versions, verifier).
        let _ = client_config();
    }

    #[test]
    fn rejects_garbage() {
        let authority = CertificateDer::from_pem_slice(AUTHORITY_PEM).unwrap();
        assert!(!issued_by_riot(b"not a certificate", authority.as_ref()));
    }
}

using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace XIVChat.Relay.Transport;

public static class RelayTls {
    // Schannel needs a temporary user key container; Dispose removes it. Do not use PersistKeySet.
    public static X509KeyStorageFlags KeyStorage => X509KeyStorageFlags.Exportable | (OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
    public static X509Certificate2 CreateCertificate() {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=XIVChat private relay endpoint", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null, KeyStorage);
    }
    public static string Fingerprint(X509Certificate certificate) => Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
    public static async Task<SslStream> ConnectAsync(Stream stream, string fingerprint, CancellationToken token) {
        if (!Protocol.RelayProtocol.ValidFingerprint(fingerprint)) throw new ArgumentException("Invalid endpoint fingerprint.");
        var expected = Convert.FromHexString(fingerprint);
        var tls = new SslStream(stream, false, (_, certificate, _, _) => {
            if (certificate == null || !CryptographicOperations.FixedTimeEquals(expected, SHA256.HashData(certificate.GetRawCertData()))) return false;
            using var inspected = new X509Certificate2(certificate);
            var now = DateTime.UtcNow;
            return now >= inspected.NotBefore.ToUniversalTime() && now <= inspected.NotAfter.ToUniversalTime();
        });
        try {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
                TargetHost = "xivchat-private-endpoint", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                AllowRenegotiation = false,
            }, token);
            return tls;
        } catch { tls.Dispose(); throw; }
    }
    public static async Task<SslStream> AcceptAsync(Stream stream, X509Certificate2 certificate, CancellationToken token) {
        var tls = new SslStream(stream, false);
        try {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificateRequired = false, AllowRenegotiation = false,
            }, token);
            return tls;
        } catch { tls.Dispose(); throw; }
    }
}

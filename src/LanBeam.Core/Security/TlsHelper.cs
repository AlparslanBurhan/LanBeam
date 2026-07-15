using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace LanBeam.Core.Security;

/// <summary>
/// TLS akışı kurulumu. Sertifika doğrulaması zincir üzerinden DEĞİL, uygulama katmanında
/// parmak izi karşılaştırmasıyla yapılır (self-signed sertifikalar + güvenilir cihaz listesi).
/// </summary>
public static class TlsHelper
{
    /// <summary>Sunucu tarafı: TLS el sıkışması yapar, karşı tarafın sertifika parmak izini döndürür.</summary>
    public static async Task<(SslStream Stream, string PeerFingerprint)> AuthenticateAsServerAsync(
        Socket socket, X509Certificate2 localCertificate, CancellationToken ct)
    {
        var network = new NetworkStream(socket, ownsSocket: true);
        var ssl = new SslStream(network, leaveInnerStreamOpen: false,
            userCertificateValidationCallback: static (_, cert, _, _) => cert is not null);
        try
        {
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = localCertificate,
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, ct).ConfigureAwait(false);

            if (ssl.RemoteCertificate is null)
                throw new AuthenticationException("Karşı taraf istemci sertifikası sunmadı.");

            return (ssl, CertificateManager.GetFingerprint(ssl.RemoteCertificate));
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>İstemci tarafı: TLS el sıkışması yapar, karşı tarafın sertifika parmak izini döndürür.</summary>
    public static async Task<(SslStream Stream, string PeerFingerprint)> AuthenticateAsClientAsync(
        Socket socket, X509Certificate2 localCertificate, CancellationToken ct)
    {
        var network = new NetworkStream(socket, ownsSocket: true);
        var ssl = new SslStream(network, leaveInnerStreamOpen: false,
            userCertificateValidationCallback: static (_, cert, _, _) => cert is not null);
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "lanbeam",
                ClientCertificates = new X509CertificateCollection { localCertificate },
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            }, ct).ConfigureAwait(false);

            if (ssl.RemoteCertificate is null)
                throw new AuthenticationException("Sunucu sertifikası alınamadı.");

            return (ssl, CertificateManager.GetFingerprint(ssl.RemoteCertificate));
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static void TuneSocket(Socket socket)
    {
        socket.NoDelay = true;
        socket.SendBufferSize = Protocol.ProtocolConstants.SocketBufferSize;
        socket.ReceiveBufferSize = Protocol.ProtocolConstants.SocketBufferSize;
    }
}

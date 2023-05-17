using System.Net;
using System.Net.Quic;
using System.Net.Security;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal class Program
{
    private static async Task Main(string[] args)
    {
        await using var clientConnection = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions()
        {
            DefaultCloseErrorCode = 0,
            DefaultStreamErrorCode = 0,
            RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 12345),
            ClientAuthenticationOptions = new SslClientAuthenticationOptions()
            {
                ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol("foo") },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            }
        });

        for (int i = 0; i < 50000; ++i)
        {
            Console.WriteLine($"Creating stream {i}");
            await using var localStream = await clientConnection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional);
            await localStream.WriteAsync(new byte[64 * 1024 - 1], completeWrites: true);
            await localStream.ReadAsync(new byte[64]); // Reply
        }

        await Task.Delay(TimeSpan.FromHours(1));
    }
}

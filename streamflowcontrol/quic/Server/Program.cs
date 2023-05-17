using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal class Program
{
    private static async Task Main(string[] args)
    {
        await using var listener = await QuicListener.ListenAsync(
            new QuicListenerOptions()
            {
                ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol("foo") },
                ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 12345),
                ConnectionOptionsCallback = (connection, clientInfo, cancel) => new(new QuicServerConnectionOptions()
                {
                    DefaultCloseErrorCode = 0,
                    DefaultStreamErrorCode = 58,
                    ServerAuthenticationOptions = new()
                    {
                        ApplicationProtocols = new List<SslApplicationProtocol> { new SslApplicationProtocol("foo") },
                        ClientCertificateRequired = false,
                        ServerCertificate = new X509Certificate2("server.p12", "password")
                    },
                    MaxInboundBidirectionalStreams = 1
                })
            });


        await using QuicConnection serverConnection = await listener!.AcceptConnectionAsync();
        byte[] responsePayload = new byte[32];

        int i = 0;
        while (true)
        {
            Console.WriteLine($"Accepting stream {i++}");
            _ = ProcessStreamAsync(await serverConnection.AcceptInboundStreamAsync(), i);
        }

        async Task ProcessStreamAsync(QuicStream stream, int i)
        {
            byte[] readBuffer = new byte[10];
            await stream.WriteAsync(responsePayload, completeWrites: true);
            while (true)
            {
                int length = await stream.ReadAsync(readBuffer);
                if (length == 0)
                {
                    break;
                }
                // Console.Error.WriteLine($"{i}: read {length} bytes");
                await Task.Delay(10);
            }
            await stream.DisposeAsync();
        }
    }
}

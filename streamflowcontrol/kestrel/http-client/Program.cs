using System.Net;
using System.Net.Security;

[System.Runtime.Versioning.SupportedOSPlatform("linux")]
internal class Program
{
    static readonly byte[] payload = new byte[62 * 1024];

    private static async Task Main(string[] args)
    {
        var socketsHandler = new SocketsHttpHandler()
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            }
        };

        using var httpClient = new HttpClient(socketsHandler)
        {
            BaseAddress = new Uri("https://localhost:5001"),
            // DefaultRequestVersion = HttpVersion.Version20, // it works with HTTP/2
            DefaultRequestVersion = HttpVersion.Version30,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        for (int i = 0; i < 50000; ++i)
        {
            Console.WriteLine($"{i}: performing PUT HTTP request");
            using HttpResponseMessage response = await httpClient.PutAsync("/", new ByteArrayContent(payload));
        }
    }
}

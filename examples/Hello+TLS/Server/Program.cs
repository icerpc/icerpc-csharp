using Demo;
using IceRpc;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Create the authentication options with the certificate defined in the configured
// certificate file.
var authenticationOptions = new SslServerAuthenticationOptions()
{
    ServerCertificate = new X509Certificate2("../../certs/server.p12", "password")
};

await using var server = new Server(new Hello(), authenticationOptions);

// Destroy the server on Ctrl+C or Ctrl+Break
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;

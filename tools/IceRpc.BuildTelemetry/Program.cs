// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.BuildTelemetry;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

const int timeout = 3000; // The timeout for the complete telemetry upload process.
const string uri = "icerpc://localhost"; // The URI of the server.

// Load the root CA certificate
using var rootCA = new X509Certificate2("certs/cacert.der");
var clientAuthenticationOptions = new SslClientAuthenticationOptions
{
    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
    {
        using var customChain = new X509Chain();
        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        customChain.ChainPolicy.DisableCertificateDownloads = true;
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        customChain.ChainPolicy.CustomTrustStore.Add(rootCA);
        return customChain.Build((X509Certificate2)certificate!);
    }
};

try
{
    // Create a cancellation token source to cancel the telemetry upload if it
    // takes too long.
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));

    // Create a client connection
    await using var connection = new ClientConnection(new Uri(uri), clientAuthenticationOptions);

    // Create a reporter proxy with this client connection.
    var reporter = new ReporterProxy(connection);

    // Upload the telemetry to the server.
    await reporter.UploadAsync(new Telemetry(args), cancellationToken: cts.Token);

    // Shutdown the connection.
    await connection.ShutdownAsync(cts.Token);
}
catch (Exception)
{
    // Consume all exceptions.
}

// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.BuildTelemetry.Reporter;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;

var timeout = TimeSpan.FromSeconds(3); // The timeout for the complete telemetry upload process.

// TODO: Update the URI to the build telemetry server once it is deployed.
const string uri = "icerpc://localhost"; // The URI of the server.

// TODO: Remove loading this test certificate once the build telemetry server is deployed.
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
    using var cts = new CancellationTokenSource(timeout);

    // Create a client connection
    await using var connection = new ClientConnection(new Uri(uri), clientAuthenticationOptions);

    // Create a reporter proxy with this client connection.
    var reporter = new ReporterProxy(connection);

    // Parse the IDL
    string? idl = args
        .SkipWhile(arg => arg != "--idl")
        .Skip(1)
        .FirstOrDefault() ?? "unknown";

    // Determine the IceRPC version using the assembly version. The assembly version is kept in sync with the
    // IceRPC version.
    var assembly = Assembly.GetAssembly(typeof(SliceTelemetry));
    string version = assembly!.GetName().Version!.ToString();

    // Parse the compilation hash
    string compilationHash = args
        .SkipWhile(arg => arg != "--hash")
        .Skip(1)
        .FirstOrDefault() ?? "unknown";

    // Parse the contains-slice1 argument
    bool containsSlice1 = bool.TryParse(args
        .SkipWhile(arg => arg != "--contains-slice1")
        .Skip(1)
        .FirstOrDefault(), out var result) && result;

    // Create the appropriate telemetry object based on the IDL
    BuildTelemetry buildTelemetry = idl switch
    {
        "Slice" => new BuildTelemetry.Slice(new SliceTelemetry(version, compilationHash, containsSlice1)),
        _ => new BuildTelemetry.Protobuf(new ProtobufTelemetry(version, compilationHash))
    };

    // Upload the telemetry to the server.
    await reporter.UploadAsync(buildTelemetry, cancellationToken: cts.Token);

    // Shutdown the connection.
    await connection.ShutdownAsync(cts.Token);
}
catch
{
    // Consume all exceptions.
}

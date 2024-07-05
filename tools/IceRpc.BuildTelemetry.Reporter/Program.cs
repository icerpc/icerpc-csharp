// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.BuildTelemetry;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;

var timeout = TimeSpan.FromSeconds(3); // The timeout for the complete telemetry upload process.

const string uri = "icerpc://telemetry.icerpc.dev"; // The URI of the server.

try
{
    // Create a cancellation token source to cancel the telemetry upload if it
    // takes too long.
    using var cts = new CancellationTokenSource(timeout);

    // Create a client connection
    var clientAuthenticationOptions = new SslClientAuthenticationOptions();
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
    var assembly = Assembly.GetAssembly(typeof(SliceTelemetryData));
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
        "Slice" => new BuildTelemetry.Slice(new SliceTelemetryData(version, compilationHash, containsSlice1)),
        _ => new BuildTelemetry.Protobuf(new ProtobufTelemetryData(version, compilationHash))
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

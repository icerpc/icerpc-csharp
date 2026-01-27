// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using Google.Protobuf.Compiler;
using Google.Protobuf.Reflection;
using IceRpc;
using IceRpc.BuildTelemetry;
using IceRpc.Transports.Quic;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using System.Net.Quic;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

// The protoc compiler executes this program and writes the Protobuf serialized CodeGeneratorRequest to standard input.

// Read the input and deserialize it using the Protobuf CodeGeneratorRequest object.
using Stream stdin = Console.OpenStandardInput();
var request = new CodeGeneratorRequest();
request.MergeFrom(stdin);

// Extract Protobuf encoded data from the request's proto files.
var sources = new List<ByteString>();
foreach (FileDescriptorProto? proto in request.ProtoFile)
{
    sources.Add(proto.ToByteString());
}

// Build the FileDescriptor objects from the collected encoded data.
IReadOnlyList<FileDescriptor> descriptors = FileDescriptor.BuildFromByteStrings(sources);

int fileCount = 0;
byte[] hashBytes = [];

foreach (FileDescriptor descriptor in descriptors)
{
    if (!request.FileToGenerate.Contains(descriptor.Name))
    {
        // This descriptor represents a reference file for which we are not generating code.
        continue;
    }

    byte[] newHash = SHA256.HashData(descriptor.SerializedData.Memory.Span);
    if (hashBytes.Length > 0)
    {
        hashBytes = SHA256.HashData(newHash.Concat(hashBytes).ToArray());
    }
    else
    {
        hashBytes = newHash;
    }
    fileCount++;
}

var response = new CodeGeneratorResponse();

if (fileCount > 0)
{
    // Determine the IceRPC version using the assembly version.
    var assembly = Assembly.GetExecutingAssembly();
    string toolVersion = assembly!.GetName().Version!.ToString();

    var protobufTelemetryData = new ProtobufTelemetryData(
        RuntimeInformation.ProcessArchitecture.ToString(),
        compilationHash: Convert.ToHexString(hashBytes).ToLowerInvariant(),
        fileCount,
        IsCi(),
        RuntimeInformation.OSDescription,
        new TargetLanguage.CSharp(Environment.Version.ToString()),
        toolVersion);

    const string uri = "icerpc://build-telemetry.icerpc.dev";
    string responseContent;

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await using var connection = new ClientConnection(
            new Uri(uri),
            new SslClientAuthenticationOptions(),
            multiplexedClientTransport: QuicConnection.IsSupported ?
                new QuicClientTransport() : new SlicClientTransport(new TcpClientTransport()));

        // Create a reporter proxy with this client connection.
        var reporter = new ReporterProxy(connection);

        // Upload the telemetry to the server.
        await reporter.UploadAsync(new BuildTelemetry.Protobuf(protobufTelemetryData), cancellationToken: cts.Token);

        // Shutdown the connection.
        await connection.ShutdownAsync(cts.Token);

        responseContent = @$"Build telemetry reported successfully to {uri}.

{protobufTelemetryData}";
    }
    catch (OperationCanceledException)
    {
        responseContent = $"Failed to report build telemetry to {uri}: operation canceled";
    }
    catch (IceRpcException ex) when (ex.IceRpcError is IceRpcError.InvocationCanceled or IceRpcError.ServerUnreachable)
    {
        responseContent = $"Failed to report build telemetry to {uri}: {ex.IceRpcError}";
    }
    catch (Exception ex)
    {
        string message = ex.Message;
        if (ex.InnerException is not null)
        {
            message += $"\n  Inner exception: {ex.InnerException.Message}";
        }
        responseContent = $"Failed to report build telemetry to {uri}: {message}";
    }

    // We return a single file containing the result of the build telemetry reporting.
    response.File.Add(
        new CodeGeneratorResponse.Types.File
        {
            Name = $"{protobufTelemetryData.CompilationHash[..8]}.icerpc_build_telemetry.txt",
            Content = responseContent
        });
}
// else fileCount is 0 and we don't do anything

using Stream stdout = Console.OpenStandardOutput();
response.WriteTo(stdout);

static bool IsCi()
{
    string[] ciEnvironmentVariables =
    [
        "TF_BUILD", // Azure Pipelines / DevOpsServer
        "GITHUB_ACTIONS", // GitHub Actions
        "APPVEYOR", // AppVeyor
        "CI", // General, set by many build agents
        "TRAVIS", // Travis CI
        "CIRCLECI", // Circle CI
        "CODEBUILD_BUILD_ID", // AWS CodeBuild
        "AWS_REGION", // AWS CodeBuild region
        "BUILD_ID", // Jenkins, Google Cloud Build
        "BUILD_URL", // Jenkins
        "PROJECT_ID", // Google Cloud Build
        "TEAMCITY_VERSION", // TeamCity
        "JB_SPACE_API_URL" // JetBrains Space
    ];

    // Check if any of the CI environment variables are set
    return ciEnvironmentVariables.Any(
        name => Environment.GetEnvironmentVariable(name) is string value && value.Length != 0);
}

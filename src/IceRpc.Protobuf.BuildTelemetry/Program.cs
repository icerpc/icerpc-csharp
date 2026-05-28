// Copyright (c) ZeroC, Inc.

using Google.Protobuf;
using Google.Protobuf.Compiler;
using Google.Protobuf.Reflection;
using IceRpc;
using IceRpc.CaseConverter.Internal;
using IceRpc.Protobuf.BuildTelemetry;
using IceRpc.Transports.Quic;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.InteropServices;

using static Google.Protobuf.Compiler.CodeGeneratorResponse.Types;
using CompilerVersion = Google.Protobuf.Compiler.Version;

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

// We use the first file name as the base name for the response file.
string? fileName = null;

int serviceCount = 0;
int rpcCount = 0;
int messageCount = 0;

foreach (FileDescriptor descriptor in descriptors)
{
    if (!request.FileToGenerate.Contains(descriptor.Name))
    {
        // This descriptor represents a reference file for which we are not generating code.
        continue;
    }

    fileName ??= Path.GetFileNameWithoutExtension(descriptor.Name).ToPascalCase();

    serviceCount += descriptor.Services.Count;
    rpcCount += descriptor.Services.Sum(service => service.Methods.Count);
    messageCount += descriptor.MessageTypes.Count;
}

var response = new CodeGeneratorResponse
{
    // We support all since we only generate code for services.
    SupportedFeatures = (ulong)(Feature.Proto3Optional | Feature.SupportsEditions),
    MaximumEdition = (int)Edition.Max
};

CompilerVersion compilerVersion = request.CompilerVersion;
string compilerVersionString = $"{compilerVersion.Major}.{compilerVersion.Minor}.{compilerVersion.Patch}";
if (compilerVersion.Suffix.Length > 0)
{
    compilerVersionString += $"-{compilerVersion.Suffix}";
}

var telemetryData = new TelemetryData
{
    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    OperatingSystem = RuntimeInformation.OSDescription,
    ProtocVersion = compilerVersionString,
    ServiceCount = (uint)serviceCount,
    RpcCount = (uint)rpcCount,
    MessageCount = (uint)messageCount,
};

bool debug = false;
try
{
    debug = ParseParameter(request.Parameter, telemetryData);
}
catch (FormatException exception)
{
    // Always converted to an error (debug or not).
    response.Error = exception.Message;
}

if (fileName is not null && response.Error.Length == 0)
{
    const string uri = "icerpc://build-telemetry.icerpc.dev";

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Create a client connection to the telemetry server. We use QUIC when supported, otherwise we use Slic over
        // TCP.
        await using var connection = new ClientConnection(
            new Uri(uri),
            new SslClientAuthenticationOptions(),
            multiplexedClientTransport: QuicConnection.IsSupported ?
                new QuicClientTransport() : new SlicClientTransport(new TcpClientTransport()));

        // Use a one-way invocation unless we're in debug mode.
        var invoker = new InlineInvoker((request, cancellationToken) =>
        {
            request.IsOneway = true; // TODO: use !debug once the server is implemented
            return connection.InvokeAsync(request, cancellationToken);
        });

        // Create a proxy with this invoker.
        var buildObserver = new BuildObserverClient(invoker);

        // Upload the telemetry to the server.
        await buildObserver.ReportProtobufBuildAsync(telemetryData, cancellationToken: cts.Token);

        // Shutdown the connection.
        await connection.ShutdownAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        if (debug)
        {
            response.Error = $"Failed to report build telemetry to {uri}: operation canceled";
        }
        // else, we ignore this exception
    }
    catch (IceRpcException ex) when (ex.IceRpcError is IceRpcError.InvocationCanceled or IceRpcError.ServerUnreachable)
    {
        if (debug)
        {
            response.Error = $"Failed to report build telemetry to {uri}: {ex.IceRpcError}";
        }
        // else, we ignore these exceptions
    }
    catch (Exception ex)
    {
        if (debug)
        {
            response.Error = $"Failed to report build telemetry to {uri}: {ex.Message}";
        }
        // else, we ignore these exceptions
    }

    if (debug && response.Error.Length == 0)
    {
        // We return a single file containing the result of the build telemetry reporting.
        response.File.Add(
            new CodeGeneratorResponse.Types.File
            {
                Name = $"{fileName}.BuildTelemetry.txt",
                Content = @$"Build telemetry reported successfully to {uri}.

{telemetryData}"
            });
    }
}
// else there is no source file and we don't do anything.

using Stream stdout = Console.OpenStandardOutput();
response.WriteTo(stdout);

// Parses the parameter string and fills the telemetry data accordingly. Returns true if debug mode is enabled.
static bool ParseParameter(string parameter, TelemetryData telemetryData)
{
    bool debug = false;

    if (parameter.Length > 0)
    {
        foreach (string entry in parameter.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] array = entry.Split('=', 2);
            string name = array[0].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            string value = array.Length > 1 ? array[1].Trim() : "";

            switch (name)
            {
                case "debug":
                    if (value.Length > 0)
                    {
                        throw new FormatException("The 'debug' parameter does not accept any value.");
                    }
                    debug = true;
                    break;
                case "dry_run":
                    if (value.Length > 0)
                    {
                        throw new FormatException("The 'dry_run' parameter does not accept any value.");
                    }
                    telemetryData.DryRun = true;
                    break;
                case "ci":
                    if (value.Length > 0)
                    {
                        throw new FormatException("The 'ci' parameter does not accept any value.");
                    }
                    telemetryData.Ci = true;
                    break;
                case "toolchain":
                    string[] toolchainInfo = value.Split(':', 2);
                    string toolchainName = toolchainInfo[0].Trim();
                    string toolchainVersion = toolchainInfo.Length > 1 ? toolchainInfo[1].Trim() : "";
                    if (toolchainName.Length == 0 || toolchainVersion.Length == 0)
                    {
                        throw new FormatException(
                            "The 'toolchain' parameter requires a value in the format '<name>:<version>'.");
                    }
                    telemetryData.Toolchain = new ToolchainInfo
                    {
                        Name = toolchainName,
                        Version = toolchainVersion
                    };
                    break;
                case "plugin":
                    string[] pluginInfo = value.Split(':', 2);
                    string pluginName = pluginInfo[0].Trim();
                    string pluginVersion = pluginInfo.Length > 1 ? pluginInfo[1].Trim() : "";
                    if (pluginName.Length == 0 || pluginVersion.Length == 0)
                    {
                        throw new FormatException(
                            "The 'plugin' parameter requires a value in the format '<name>:<version>'.");
                    }
                    telemetryData.Plugins.Add(new PluginInfo { Name = pluginName, Version = pluginVersion });
                    break;
                default:
                    throw new FormatException($"Unknown parameter: '{name}'");
            }
        }
    }

    return debug;
}

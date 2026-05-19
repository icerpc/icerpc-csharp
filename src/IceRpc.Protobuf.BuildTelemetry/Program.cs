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
    rpcCount += descriptor.Services.SelectMany(s => s.Methods).Count();
    messageCount += descriptor.MessageTypes.Count;
}

var response = new CodeGeneratorResponse();

if (fileName is not null)
{
    (bool debug, bool dryRun, List<PluginInfo> plugins) = ParseParameter(request.Parameter);

    var telemetryData = new TelemetryData
    {
        Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        OperatingSystem = RuntimeInformation.OSDescription,
        ProtocVersion = request.CompilerVersion,
        Plugins = { plugins },
        ServiceCount = (uint)serviceCount,
        RpcCount = (uint)rpcCount,
        MessageCount = (uint)messageCount,
        DryRun = dryRun
    };

    string uri = "icerpc://build-telemetry.icerpc.dev";

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

        // Add path to URI.
        uri += buildObserver.ServiceAddress.Path;

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

    if (debug)
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

static (bool Debug, bool DryRun, List<PluginInfo> Plugins) ParseParameter(string parameter)
{
    bool debug = false;
    bool dryRun = false;
    var plugins = new List<PluginInfo>();

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
                        throw new FormatException($"Invalid value for 'debug' parameter: '{value}'");
                    }
                    debug = true;
                    break;
                case "dry_run":
                    if (value.Length > 0)
                    {
                        throw new FormatException($"Invalid value for 'dry_run' parameter: '{value}'");
                    }
                    dryRun = true;
                    break;
                case "plugin":
                    string[] pluginInfo = value.Split(':', 2);
                    string pluginName = pluginInfo[0].Trim();
                    string pluginVersion = pluginInfo.Length > 1 ? pluginInfo[1].Trim() : "";
                    plugins.Add(new PluginInfo { Name = pluginName, Version = pluginVersion });
                    break;
                default:
                    throw new FormatException($"Unknown parameter: '{name}'");
            }
        }
    }

    return (debug, dryRun, plugins);
}

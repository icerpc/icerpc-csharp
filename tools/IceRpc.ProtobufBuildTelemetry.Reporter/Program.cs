// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.BuildTelemetry;
using System.CommandLine;
using System.Net.Security;
using System.Reflection;
using System.Runtime.InteropServices;

var rootCommand = new RootCommand("Protobuf build telemetry reporter");

var compilationHashOption = new Option<string>(
    name: "--compilation-hash",
    description: "The compilation hash.")
{
    IsRequired = true,
};
rootCommand.AddOption(compilationHashOption);

var fileCountOption = new Option<int>(
    name: "--file-count",
    description: "The number of files included in the compilation.");
rootCommand.AddOption(fileCountOption);

rootCommand.SetHandler(
    async (string compilationHash, int fileCount) =>
    {
        const string uri = "icerpc://telemetry.icerpc.dev";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var connection = new ClientConnection(new Uri(uri), new SslClientAuthenticationOptions());

        // Create a reporter proxy with this client connection.
        var reporter = new ReporterProxy(connection);

        // Determine the IceRPC version using the assembly version.
        var assembly = Assembly.GetAssembly(typeof(ProtobufTelemetryData));
        string toolVersion = assembly!.GetName().Version!.ToString();

        var protobufTelemetryData = new ProtobufTelemetryData(
            RuntimeInformation.ProcessArchitecture.ToString(),
            compilationHash,
            fileCount,
            IsCi(),
            RuntimeInformation.OSDescription,
            new TargetLanguage.CSharp(Environment.Version.ToString()),
            toolVersion);

        // Upload the telemetry to the server.
        await reporter.UploadAsync(new BuildTelemetry.Protobuf(protobufTelemetryData), cancellationToken: cts.Token);

        // Shutdown the connection.
        await connection.ShutdownAsync(cts.Token);
    },
    compilationHashOption,
    fileCountOption);

return await rootCommand.InvokeAsync(args);

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

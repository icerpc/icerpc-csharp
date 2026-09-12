// Copyright (c) ZeroC, Inc.

using GreeterExample;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;
using static Program;

namespace IceRpc.Examples;

public static class PipelineExamples
{
    public static async Task CreatingAndUsingThePipeline()
    {
        #region CreatingThePipeline
        // Create a simple console logger factory and configure the log level for category IceRpc.
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            builder
                .AddSimpleConsole()
                .AddFilter("IceRpc", LogLevel.Information));

        // The server uses a test certificate, so we trust its root CA. CreateClientAuthenticationOptions
        // is a helper from examples/common/Program.Authentication.cs in the icerpc-csharp repo.
        using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

        await using var connection = new ClientConnection(
            new Uri("icerpc://localhost"),
            clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA));

        // Create an invocation pipeline and install the logger interceptor.
        Pipeline pipeline = new Pipeline()
            .UseLogger(loggerFactory)
            .Into(connection);
        #endregion

        {
        #region CreateSliceProxy
        // Create a Slice proxy that uses pipeline as its invocation pipeline.
        var greeter = new GreeterProxy(pipeline);
        #endregion
        }

        {
        #region CreateProtobufClient
        // Create a Protobuf client that uses pipeline as its invocation pipeline.
        var greeter = new GreeterClient(pipeline);
        #endregion
        }
    }

    public static void UseWithInlineInterceptor()
    {
        #region UseWithInlineInterceptor
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancel) =>
            {
                // Add some logic before processing the request
                Console.WriteLine("before next.InvokeAsync");
                // Call the next invoker on the invocation pipeline.
                IncomingResponse response =
                    await next.InvokeAsync(request, cancel).ConfigureAwait(false);
                Console.WriteLine(
                    $"after next.InvokeAsync; the response status code is {response.StatusCode}");
                // Add some logic after receiving the response.
                return response;
            }));
        #endregion
    }
}

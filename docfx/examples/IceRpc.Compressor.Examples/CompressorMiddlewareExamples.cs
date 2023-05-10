// Copyright (c) ZeroC, Inc.

using GreeterExample;

namespace IceRpc.Telemetry.Examples;

public static class CompressorMiddlewareExamples
{
    public static async Task UseCompressor()
    {
        #region UseCompressor
        // Create a connection.
        await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

        // Add the compressor middleware to the dispatch pipeline.
        Router router = new Router()
            .UseCompressor(CompressionFormat.Brotli)
            .Map<IGreeterService>(new Chatbot());

        await using var server = new Server(router);
        server.Listen();
        #endregion
    }
}

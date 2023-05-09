// Copyright (c) ZeroC, Inc.

namespace IceRpc.Compressor.Examples;

public static class CompressorInterceptorExamples
{
    public static async Task UseCompressor()
    {
        #region UseCompressor
        // Create a connection.
        await using var connection = new ClientConnection(new Uri("icerpc://localhost"));

        // Create an invocation pipeline and install the compressor interceptor.
        Pipeline pipeline = new Pipeline()
            .UseCompressor(CompressionFormat.Brotli)
            .Into(connection);
        #endregion
    }
}

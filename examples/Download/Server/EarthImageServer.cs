// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using System.IO.Pipelines;

namespace DownloadExample;

internal class EarthImageServer : Service, IDownloaderService
{
    public ValueTask<PipeReader> DownloadImageAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine("Streaming image of the Earth to the client ...");

        // Create a pipe reader over the file stream we want to transmit. The pipe reader takes ownership of the file
        // stream and will dispose it once the IceRPC runtime completes it.
        var reader = PipeReader.Create(new FileStream("Server/images/Earth.png", FileMode.Open));

        return new ValueTask<PipeReader>(reader);
    }
}

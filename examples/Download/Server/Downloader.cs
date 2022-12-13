// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using System.IO.Pipelines;

namespace Demo;

public class Downloader : Service, IDownloader
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

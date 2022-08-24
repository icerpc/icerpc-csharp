// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using System.IO.Pipelines;

namespace Demo;

public class Downloader : Service, IDownloader
{
    public ValueTask<PipeReader> DownloadImageAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        // Create a FileStream for the image to be sent to the client. This stream will be disposed by IceRPC when it
        // completes `reader` (created later on).
        var image = new FileStream("Server/images/Earth.png", FileMode.Open);

        Console.WriteLine("Streaming image of the Earth to the client ...");

        // Wrap the FileStream in a PipeReader.
        PipeReader reader = PipeReader.Create(image);

        return new ValueTask<PipeReader>(reader);
    }
}

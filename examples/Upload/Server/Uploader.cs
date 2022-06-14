// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using System.IO.Pipelines;

namespace Demo;

public class Uploader : Service, IUploader
{
    public async ValueTask UploadImageAsync(PipeReader image, IFeatureCollection features, CancellationToken cancel)
    {
        Console.WriteLine("Downloading image...");

        // leaveOpen will cleanup the stream when the PipeReader is completed (false by default).
        Stream imageStream = image.AsStream(leaveOpen: false);

        // Create the file, or overwrite if the file exists.
        using (FileStream fs = File.Create($"Server/uploads/uploaded_earth.png"))
        {
            // Copy the image to the file stream.
            await imageStream.CopyToAsync(fs, cancel);

            // Complete and cleanup the pipe reader.
            await image.CompleteAsync();
        }

        Console.WriteLine("Image downloaded");
    }
}

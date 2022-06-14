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

        Stream imageStream = image.AsStream(leaveOpen: false);

        // Create the file, or overwrite if the file exists.
        using (FileStream fs = File.Create("Server/images/zeroc_icon.png"))
        {
            // Copy the image to the file stream.
            await imageStream.CopyToAsync(fs);
        }

        Console.WriteLine("Image downloaded.");
    }
}

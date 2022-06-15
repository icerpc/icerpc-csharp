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
        try
        {
            // AsStream has an argument `leaveOpen` which is set to `false` by default. When `leaveOpen` is `false`, the
            // stream will be closed and disposed when the PipeReader is completed.
            Stream imageStream = image.AsStream();

            // Create the file, or overwrite if the file exists.
            using FileStream fs = File.Create($"Server/uploads/uploaded_earth.png");

            // Copy the image to the file stream.
            await imageStream.CopyToAsync(fs, cancel);

            // Complete and cleanup the pipe reader.
            await image.CompleteAsync();

            Console.WriteLine("Image downloaded");
        }
        catch (Exception exception)
        {
            // Complete and cleanup the pipe reader.
            await image.CompleteAsync(exception);
            throw;
        }
    }
}

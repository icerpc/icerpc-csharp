// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using System.IO.Pipelines;

namespace UploadExample;

internal class EarthImageStore : Service, IUploaderService
{
    public async ValueTask UploadImageAsync(
        PipeReader image,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Reading image...");

        // AsStream has a parameter `leaveOpen` which is set to `false` by default. When the stream is disposed, if
        // leaveOpen` is `false` then the PipeReader used to create the stream is completed.
        using Stream imageStream = image.AsStream();

        // Create the file, or overwrite if the file exists.
        using FileStream fs = File.Create("Server/uploads/uploaded_earth.png");

        // Copy the image to the file stream.
        await imageStream.CopyToAsync(fs, cancellationToken);

        // Complete and cleanup the pipe reader.
        image.Complete();

        Console.WriteLine("Image fully read and saved to disk.");
    }
}

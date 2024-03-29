// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using Repository;
using System.IO.Pipelines;

namespace UploadServer;

[SliceService]
internal partial class EarthImageStore : IUploaderService
{
    public async ValueTask UploadImageAsync(
        PipeReader image,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Reading image...");

        // Create the file, or overwrite if the file exists.
        using FileStream fs = File.Create("Server/uploads/uploaded_earth.png");

        // Copy the image to the file stream.
        await image.CopyToAsync(fs, cancellationToken);

        // Complete and cleanup the pipe reader.
        image.Complete();

        Console.WriteLine("Image fully read and saved to disk.");
    }
}

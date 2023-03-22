// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.IO.Pipelines;
using UploadExample;

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));
var uploader = new UploaderProxy(connection);

Console.WriteLine("Uploading image of the Earth...");

// Create a pipe reader that wraps the image we want to upload. The pipe reader takes ownership of the
// file stream and disposes it once it is completed by the IceRPC runtime.
var reader = PipeReader.Create(new FileStream("Client/images/Earth.png", FileMode.Open));

// This call waits until the uploader service returns. The uploader service returns after reading the full image.
await uploader.UploadImageAsync(reader);

Console.WriteLine("Image of the Earth uploaded");

await connection.ShutdownAsync();

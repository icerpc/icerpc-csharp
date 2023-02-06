// Copyright (c) ZeroC, Inc.

using IceRpc;
using System.IO.Pipelines;
using UploadExample;

// Establish the connection to the server
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"));
var uploader = new UploaderProxy(connection);

Console.WriteLine("Uploading image of the Earth...");

// Create a pipe reader that wraps the image we want to upload. The pipe reader takes ownership of the
// file stream and disposes it once it is completed by the IceRPC runtime.
var reader = PipeReader.Create(new FileStream("Client/images/Earth.png", FileMode.Open));

// Begin streaming the data to the server.
// TODO I find this misleading, the completion of `UploadImageAsync` doesn't ensure that the server finish reading
// the stream argument.
// The IceRpc runtime will automatically complete the PipeReader for the client once the await returns.
await uploader.UploadImageAsync(reader);

Console.WriteLine("Image of the Earth uploaded");

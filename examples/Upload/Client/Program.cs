// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.IO.Pipelines;

// Establish the connection to the server
await using var connection = new ClientConnection("icerpc://127.0.0.1");
var uploader = new UploaderProxy(connection);

// Create a FileStream for the image to be uploaded. This stream will be disposed by IceRPC when it completes the
// `PipeReader`.
var image = new FileStream("Client/images/Earth.png", FileMode.Open);

Console.WriteLine("Uploading image of the Earth...");

// Wrap the FileStream in a PipeReader.
PipeReader reader = PipeReader.Create(image);

// Begin streaming the data to the server.
// The IceRpc runtime will automatically complete the PipeReader for the client once the await returns.
await uploader.UploadImageAsync(reader);

Console.WriteLine("Image of the Earth uploaded");

// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.IO.Pipelines;

// Establish the connection to the server
await using var connection = new ClientConnection("icerpc://127.0.0.1");
IUploaderPrx uploader = UploaderPrx.FromConnection(connection);

// Create a FileStream for the image to be uploaded.
await using var image = new FileStream("Client/images/Earth.png", FileMode.Open);

Console.WriteLine("Uploading image of the Earth...");

// Wrap the FileStream in a PipeReader and begin streaming the data to the server.
PipeReader reader = PipeReader.Create(image);
await uploader.UploadImageAsync(reader);

// The IceRpc runtime will automatically complete the PipeReader for the client.
Console.WriteLine("Image of the Earth uploaded");

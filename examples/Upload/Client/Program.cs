// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using System.IO.Pipelines;

await using var connection = new ClientConnection("icerpc://127.0.0.1");

IUploaderPrx uploader = UploaderPrx.FromConnection(connection);

Console.Write("To upload the image enter any key: ");

if (Console.ReadLine() is string _)
{
    // Create a pipe using a FileStream.

    FileStream file = new FileStream("Client/images/zeroc_icon.png", FileMode.Open);
    PipeReader pipeReader = PipeReader.Create(file);

    await uploader.UploadImageAsync(pipeReader);
}

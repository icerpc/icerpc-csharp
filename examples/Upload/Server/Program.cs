// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using UploadExample;

await using var server = new Server(new Uploader());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();

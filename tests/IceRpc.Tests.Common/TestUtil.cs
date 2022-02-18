// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

public static class TestUtil
{
    public static IncomingRequest CreateIncomingRequest(string path, string operation) =>
        new(
            Protocol.IceRpc,
            path: path,
            fragment: "",
            operation: operation,
            PipeReader.Create(ReadOnlySequence<byte>.Empty),
            Encoding.Slice20,
            responseWriter: InvalidPipeWriter.Instance);

    public static OutgoingRequest CreateOutgoingRequest(Proxy proxy, string operation) =>
        new(proxy, operation: operation);

    public static Proxy CreateProxy(string path) =>
         new(Protocol.IceRpc) { Path = path };

    public static Proxy CreateProxy(Protocol protocol, string path) =>
         new(protocol) { Path = path };
}
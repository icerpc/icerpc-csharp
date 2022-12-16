// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Conformance.Tests;


internal static class MultiplexedConformanceTestsHelper
{
    internal static async ValueTask CleanupStreamsAsync(params IMultiplexedStream[] streams)
    {
        foreach (IMultiplexedStream stream in streams)
        {
            if (stream.IsBidirectional)
            {
                stream.Output.Complete();
                stream.Input.Complete();
            }
            else if (stream.IsRemote)
            {
                stream.Input.Complete();
            }
            else
            {
                stream.Output.Complete();
            }
            await stream.DisposeAsync();
        }
    }

    internal static async Task<IMultiplexedConnection> ConnectAndAcceptConnectionAsync(
        IListener<IMultiplexedConnection> listener,
        IMultiplexedConnection connection)
    {
        var connectTask = connection.ConnectAsync(default);
        var acceptTask = listener.AcceptAsync(default);
        if (connectTask.IsFaulted)
        {
            await connectTask;
        }
        if (acceptTask.IsFaulted)
        {
            await acceptTask;
        }
        var serverConnection = (await acceptTask).Connection;
        await serverConnection.ConnectAsync(default);
        await connectTask;
        return serverConnection;
    }

    internal static async Task<LocalAndRemoteStreams> CreateAndAcceptStreamAsync(
        IMultiplexedConnection localConnection,
        IMultiplexedConnection remoteConnection,
        bool isBidirectional = true)
    {
        IMultiplexedStream localStream = await localConnection.CreateStreamAsync(
            bidirectional: isBidirectional,
            default).ConfigureAwait(false);
        _ = await localStream.Output.WriteAsync(new ReadOnlyMemory<byte>(new byte[] { 0xFF }));
        IMultiplexedStream remoteStream = await remoteConnection.AcceptStreamAsync(default);
        ReadResult readResult = await remoteStream.Input.ReadAsync();
        remoteStream.Input.AdvanceTo(readResult.Buffer.End);
        return new LocalAndRemoteStreams(localStream, remoteStream);
    }
}

internal readonly struct LocalAndRemoteStreams : IAsyncDisposable
{
    internal IMultiplexedStream LocalStream { get; }

    internal IMultiplexedStream RemoteStream { get; }

    public ValueTask DisposeAsync() =>
        MultiplexedConformanceTestsHelper.CleanupStreamsAsync(LocalStream, RemoteStream);

    internal LocalAndRemoteStreams(IMultiplexedStream localStream, IMultiplexedStream remoteStream)
    {
        LocalStream = localStream;
        RemoteStream = remoteStream;
    }
}

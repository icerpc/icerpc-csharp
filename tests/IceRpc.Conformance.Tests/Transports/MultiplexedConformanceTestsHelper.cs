// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Conformance.Tests;

internal static class MultiplexedConformanceTestsHelper
{
    internal static void CleanupStreams(params IMultiplexedStream[] streams)
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
        }
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

internal readonly struct LocalAndRemoteStreams : IDisposable
{
    internal IMultiplexedStream LocalStream { get; }

    internal IMultiplexedStream RemoteStream { get; }

    public void Dispose() => MultiplexedConformanceTestsHelper.CleanupStreams(LocalStream, RemoteStream);

    internal LocalAndRemoteStreams(IMultiplexedStream localStream, IMultiplexedStream remoteStream)
    {
        LocalStream = localStream;
        RemoteStream = remoteStream;
    }
}

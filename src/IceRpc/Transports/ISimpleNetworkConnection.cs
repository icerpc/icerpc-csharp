// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IO.Pipelines;

namespace IceRpc.Transports
{
    // TODO: ISimpleNetworkConnection still has ReadAsync/WriteAsync methods for the Ice protocol support. This
    // will be removed once we refactor the Ice1 protocol to use the pipes.

    /// <summary>Represents a network connection created by a simple transport. The IceRPC core calls <see
    /// cref="INetworkConnection.ConnectAsync"/> before calling other methods.</summary>
    public interface ISimpleNetworkConnection : INetworkConnection, IDuplexPipe
    {
        /// <summary>Reads data from the connection.</summary>
        /// <param name="buffer">The buffer that holds the read data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes read.</returns>
        ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Writes data over the connection.</summary>
        /// <param name="buffers">The buffers containing the data to write.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are written.</returns>
        ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, CancellationToken cancel);
    }
}

// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A multiplexed stream factory creates multiplexed streams.</summary>
    public interface IMultiplexedStreamFactory
    {
        /// <summary>Accepts a remote stream.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The remote stream.</return>
        ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancel);

        /// <summary>Closes the underlying multiplexed network connection.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        ValueTask CloseAsync(CancellationToken cancel);

        /// <summary>Creates a local stream.</summary>
        /// <param name="bidirectional"><c>True</c> to create a bidirectional stream, <c>false</c>
        /// otherwise.</param>
        /// <return>The local stream.</return>
        IMultiplexedStream CreateStream(bool bidirectional);
    }
}

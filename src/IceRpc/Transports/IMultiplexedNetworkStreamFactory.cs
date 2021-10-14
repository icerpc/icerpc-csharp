// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A multi-stream connection enables byte data exchange over multiple <see
    /// cref="IMultiplexedNetworkStream"/>.</summary>
    public interface IMultiplexedNetworkStreamFactory : IDisposable
    {
        /// <summary>Accepts a remote stream.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The remote stream.</return>
        ValueTask<IMultiplexedNetworkStream> AcceptStreamAsync(CancellationToken cancel);

        /// <summary>Creates a local stream.</summary>
        /// <param name="bidirectional"><c>True</c> to create a bidirectional stream, <c>false</c>
        /// otherwise.</param>
        /// <return>The local stream.</return>
        IMultiplexedNetworkStream CreateStream(bool bidirectional);
    }
}

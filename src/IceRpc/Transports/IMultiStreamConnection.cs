// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>A multi-stream connection enable byte data exchange over multiple <see
    /// cref="INetworkStream"/>.</summary>
    public interface IMultiStreamConnection
    {
        /// <summary>Accepts a remote stream.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <return>The remote stream.</return>
        ValueTask<INetworkStream> AcceptStreamAsync(CancellationToken cancel);

        /// <summary>Creates a local stream.</summary>
        /// <param name="bidirectional"><c>True</c> to create a bidirectional stream, <c>false</c>
        /// otherwise.</param>
        /// <return>The local stream.</return>
        INetworkStream CreateStream(bool bidirectional);

        /// <summary>Perform multi-stream connection specific initialization.</summary>
        ValueTask InitializeAsync(CancellationToken cancel);
    }
}

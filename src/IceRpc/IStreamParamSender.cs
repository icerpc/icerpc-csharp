// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc
{
    /// <summary>An interface that encapsulates a stream param and knows how to encode and send the param over an
    /// <see cref="INetworkStream"/>.</summary>
    public interface IStreamParamSender
    {
        /// <summary>Creates one or more frames appropriate to send the stream param and sends constructed
        /// frames using the given <see cref="INetworkStream"/>.</summary>
        /// <param name="stream">The stream used to send the frames.</param>
        /// <param name="streamCompressor">The compressor to apply to the encoded data.</param>
        Task SendAsync(
            INetworkStream stream,
            Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor);
    }
}

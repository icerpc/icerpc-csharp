// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An interface that encapsulates a stream param and knows how to send the param using a
    /// <see cref="RpcStream"/>.</summary>
    public interface IStreamParamSender
    {
        /// <summary>Creates one or more frames appropriate to send the stream param and sends the frames using the given
        /// <see cref="RpcStream"/>.</summary>
        /// <param name="stream">The stream used to send the frames.</param>
        /// <param name="streamCompressor">The compressor to apply to the encoded data.</param>
        Task SendAsync(RpcStream stream, Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? streamCompressor);
    }
}

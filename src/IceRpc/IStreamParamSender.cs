// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc
{
    /// <summary>An interface that encapsulates a stream param and knows how to encode and send the param over an
    /// <see cref="IMultiplexedStream"/>.</summary>
    public interface IStreamParamSender
    {
        /// <summary>Creates one or more frames appropriate to send the stream param and sends constructed
        /// frames using the given <see cref="IMultiplexedStream"/>.</summary>
        /// <param name="stream">The stream used to send the frames.</param>
        Task SendAsync(IMultiplexedStream stream);
    }
}

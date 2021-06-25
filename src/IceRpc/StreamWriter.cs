// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;

namespace IceRpc
{
    /// <summary>A stream writer to write a stream param.</summary>
    public sealed class RpcStreamWriter
    {
        private readonly Action<RpcStream> _writer;

        internal void Send(RpcStream stream) => _writer(stream);

        /// <summary>Creates a stream writer that writes the data from the given <see cref="System.IO.Stream"/> to the
        /// request stream.</summary>
        /// <param name="byteStream">The stream to read data from.</param>
        public RpcStreamWriter(System.IO.Stream byteStream)
            : this(stream => stream.SendData(byteStream))
        {
        }

        private RpcStreamWriter(Action<RpcStream> writer) => _writer = writer;
    }
}

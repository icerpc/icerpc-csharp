// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;

namespace IceRpc
{
    /// <summary>A stream writer to write a stream param.</summary>
    public sealed class StreamWriter
    {
        private readonly Action<Stream> _writer;

        internal void Send(Stream stream) => _writer(stream);

        /// <summary>Creates a stream writer that writes the data from the given <see cref="System.IO.Stream"/> to the
        /// request stream.</summary>
        /// <param name="byteStream">The stream to read data from.</param>
        public StreamWriter(System.IO.Stream byteStream)
            : this(stream => stream.SendData(byteStream))
        {
        }

        private StreamWriter(Action<Stream> writer) => _writer = writer;
    }
}

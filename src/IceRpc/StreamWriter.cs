// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;

namespace IceRpc
{
    /// <summary>A stream writer to write a stream param.</summary>
    public sealed class StreamWriter
    {
        private readonly Action<Stream> _writer;

        /// <summary>Writes the data from the <see cref="System.IO.Stream"/> to the request stream.</summary>
        /// <param name="ioStream">The stream to read data from.</param>
        static public StreamWriter FromIOStream(System.IO.Stream ioStream) => new(stream => stream.SendData(ioStream));

        internal void Send(Stream stream) => _writer(stream);

        internal StreamWriter(Action<Stream> writer) => _writer = writer;
    }
}

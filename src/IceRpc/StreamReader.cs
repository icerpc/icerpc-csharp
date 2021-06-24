// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;

namespace IceRpc
{
    /// <summary>A stream reader to read a stream param.</summary>
    public sealed class StreamReader
    {
        private readonly Stream _stream;

        /// <summary>Reads the stream data from the given dispatch with a <see cref="System.IO.Stream"/>.</summary>
        /// <returns>The read-only stream to read the data from the request stream.</returns>
        static public System.IO.Stream ToByteStream(Dispatch dispatch) => dispatch.IncomingRequest.Stream.ReceiveData();

        /// <summary>Reads the stream data with a <see cref="System.IO.Stream"/>.</summary>
        /// <returns>The read-only stream to read the data from the request stream.</returns>
        public System.IO.Stream ToByteStream() => _stream.ReceiveData();

        internal StreamReader(Stream stream) => _stream = stream;
    }
}

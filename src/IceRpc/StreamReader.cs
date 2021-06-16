// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;

namespace IceRpc
{
    /// <summary>A stream reader to read a stream param.</summary>
    public sealed class StreamReader
    {
        private readonly Stream? _stream;

        /// <summary>Reads the stream data with a <see cref="System.IO.Stream"/>.</summary>
        /// <return>The read-only stream to read the data from the request stream.</return>
        /// <exception cref="InvalidDataException">Thrown if no stream data is available</exception>
        public System.IO.Stream ToByteStream()
        {
            if (_stream == null)
            {
                throw new InvalidDataException("expected stream data but no stream data available");
            }
            return _stream.ReceiveData();
        }

        internal StreamReader(Stream? stream) => _stream = stream;
    }
}

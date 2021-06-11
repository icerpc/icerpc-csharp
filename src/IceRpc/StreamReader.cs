// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System;

namespace IceRpc
{
    /// <summary>A stream reader to read a stream param.</summary>
    public sealed class StreamReader : IDisposable
    {
        // The NoStreamData object is used as the default for IncomingFrame.StreamReader instead of using a nullable
        // which would require for null values.
        internal static StreamReader NoStreamData = new(null, releaseStream: false);

        private readonly bool _releaseStream;
        private readonly Stream? _stream;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_releaseStream)
            {
                 _stream?.Release();
            }
        }

        /// <summary>Read the stream data with a <see cref="System.IO.Stream"/>.</summary>
        /// <return>The read-only stream to read the data from the request stream.</return>
        /// <exception cref="InvalidDataException">Thrown if no stream data is available</exception>
        public System.IO.Stream ToIOStream()
        {
            if (_stream == null)
            {
                throw new InvalidDataException("expected stream data but no stream data available");
            }
            return _stream.ReceiveData();
        }

        internal StreamReader(Stream? stream, bool releaseStream)
        {
             _stream = stream;
             _releaseStream = releaseStream;
        }
    }
}

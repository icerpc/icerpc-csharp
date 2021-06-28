// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc
{
    /// <summary>A stream reader to read a stream param from a <see cref="RpcStream"/>.</summary>
    public sealed class RpcStreamReader
    {
        private readonly RpcStream _stream;

        /// <summary>Reads the stream data from the given dispatch's <see cref="RpcStream"/> with a
        /// <see cref="System.IO.Stream"/>.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to read the data from the request stream.</returns>
        public static System.IO.Stream ToByteStream(Dispatch dispatch) => dispatch.IncomingRequest.Stream.ReceiveData();

        /// <summary>Reads the stream data with a <see cref="System.IO.Stream"/>.</summary>
        /// <returns>The read-only <see cref="System.IO.Stream"/> to read the data from the request stream.</returns>
        public System.IO.Stream ToByteStream() => _stream.ReceiveData();

        internal RpcStreamReader(RpcStream stream) => _stream = stream;
    }
}

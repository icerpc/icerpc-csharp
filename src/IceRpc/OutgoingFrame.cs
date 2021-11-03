// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Base class for outgoing frames.</summary>
    public abstract class OutgoingFrame
    {
        /// <summary>Returns a dictionary used to set the fields of this frame. The full fields are a combination of
        /// these fields plus the <see cref="FieldsDefaults"/>.</summary>
        /// <remarks>The actions set in this dictionary are executed when the frame is sent.</remarks>
        public Dictionary<int, Action<IceEncoder>> Fields { get; } = new();

        /// <summary>Returns the defaults fields set during construction of this frame. The fields are used only when
        /// there is no corresponding entry in <see cref="Fields"/>.</summary>
        public IReadOnlyDictionary<int, ReadOnlyMemory<byte>> FieldsDefaults { get; init; } =
              ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>The features of this frame.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Gets or sets the payload of this frame.</summary>
        public ReadOnlyMemory<ReadOnlyMemory<byte>> Payload
        {
            get => _payload;
            set
            {
                _payload = value;
                _payloadSize = -1;
            }
        }

        /// <summary>Returns the encoding of the payload of this frame.</summary>
        /// <remarks>The header of the frame is always encoded using the frame protocol's encoding.</remarks>
        public Encoding PayloadEncoding { get; init; } = Encoding.Unknown;

        /// <summary>Returns the number of bytes in the payload.</summary>
        public int PayloadSize
        {
            get
            {
                if (_payloadSize == -1)
                {
                    _payloadSize = Payload.GetByteCount();
                }
                return _payloadSize;
            }
        }

        /// <summary>Returns the Ice protocol of this frame.</summary>
        public Protocol Protocol { get; }

        /// <summary>A stream parameter compressor. Middleware or interceptors can use this property to
        /// compress a stream parameter or return value.</summary>
        public Func<System.IO.Stream, (CompressionFormat, System.IO.Stream)>? StreamCompressor { get; set; }

        /// <summary>The stream param sender, if the request or response has a stream param. The sender is called
        /// after the request or response frame is sent over the stream.</summary>
        internal IStreamParamSender? StreamParamSender { get; init; }

        private ReadOnlyMemory<ReadOnlyMemory<byte>> _payload = ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty;
        private int _payloadSize = -1;

        /// <summary>Constructs an outgoing frame.</summary>
        /// <param name="protocol">The protocol used to send the frame.</param>
        protected OutgoingFrame(Protocol protocol) => Protocol = protocol;

        internal void SendStreamParam(IMultiplexedStream stream)
        {
            Debug.Assert(StreamParamSender != null);
            _ = Task.Run(() =>
                {
                    try
                    {
                        StreamParamSender.SendAsync(stream, StreamCompressor);
                    }
                    catch
                    {
                    }
                },
                default);
        }
    }
}

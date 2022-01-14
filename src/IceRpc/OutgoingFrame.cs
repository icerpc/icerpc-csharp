// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Base class for outgoing frames.</summary>
    public abstract class OutgoingFrame
    {
        /// <summary>Returns a dictionary used to set the fields of this frame. The full fields are a combination of
        /// these fields plus the <see cref="FieldsDefaults"/>.</summary>
        /// <remarks>The actions set in this dictionary are executed when the frame is sent.</remarks>
        public Dictionary<int, EncodeAction> Fields { get; } = new();

        /// <summary>Returns the defaults fields set during construction of this frame. The fields are used only when
        /// there is no corresponding entry in <see cref="Fields"/>.</summary>
        public IReadOnlyDictionary<int, ReadOnlyMemory<byte>> FieldsDefaults { get; init; } =
              ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>The features of this frame.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Returns the encoding of the payload of this frame.</summary>
        /// <remarks>The header of the frame is always encoded using the frame protocol's encoding.</remarks>
        public Encoding PayloadEncoding { get; init; } = Encoding.Unknown;

        /// <summary>Gets or sets the payload sink of this frame.</summary>
        public PipeWriter PayloadSink { get; set; }

        /// <summary>Gets or sets the payload source of this frame. The payload source is sent together with the frame
        /// header and the sending operation awaits until the payload source is fully sent.</summary>
        public PipeReader PayloadSource { get; set; } = EmptyPipeReader.Instance;

        /// <summary>Gets or sets the payload source stream of this frame. The payload source stream (if specified) is
        /// sent after the payload source. It's sent in the background: the sending operation does not await it.
        /// </summary>
        public PipeReader? PayloadSourceStream { get; set; }

        /// <summary>Returns the Ice protocol of this frame.</summary>
        public Protocol Protocol { get; }

        /// <summary>Constructs an outgoing frame.</summary>
        /// <param name="protocol">The protocol used to send the frame.</param>
        /// <param name="payloadSink">The outgoing frame's payload sink.</param>
        protected OutgoingFrame(Protocol protocol, PipeWriter payloadSink)
        {
            if (!protocol.IsSupported)
            {
                throw new NotSupportedException($"cannot create outgoing frame for protocol {protocol}");
            }

            Protocol = protocol;
            PayloadSink = payloadSink;
        }
    }
}

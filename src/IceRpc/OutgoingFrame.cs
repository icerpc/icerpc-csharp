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
        /// <summary>Gets or sets the fields of this outgoing frame. The full fields are a combination of this
        /// these fields plus <see cref="FieldsOverride"/>.</summary>
        public IDictionary<int, ReadOnlyMemory<byte>> Fields { get; set; } =
              ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;

        /// <summary>Gets or sets the fields override of this outgoing frame. The full fields are a combination of this
        /// <see cref="Fields"/> plus these overrides.</summary>
        /// <remarks>The actions set in this dictionary are executed when the frame is sent.</remarks>
        public IDictionary<int, EncodeAction> FieldsOverride { get; set; } = new Dictionary<int, EncodeAction>();

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
                if (protocol == Protocol.Relative)
                {
                    throw new NotSupportedException($"cannot create an outgoing frame for a relative proxy");
                }
                else
                {
                    throw new NotSupportedException($"cannot create an outgoing frame for protocol '{protocol}'");
                }
            }

            Protocol = protocol;
            PayloadSink = payloadSink;
        }
    }
}

// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using System.Buffers;
using System.Collections.Immutable;
using System.IO.Pipelines;

namespace IceRpc
{
    /// <summary>Base class for outgoing frames.</summary>
    public abstract class OutgoingFrame
    {
        /// <summary>Gets or sets the fields of this outgoing frame. The full fields are a combination of these fields
        /// plus <see cref="FieldsOverrides"/>.</summary>
        public IDictionary<int, ReadOnlySequence<byte>> Fields { get; set; } =
            ImmutableDictionary<int, ReadOnlySequence<byte>>.Empty;

        /// <summary>Gets or sets the fields overrides of this outgoing frame. The full fields are a combination of
        /// <see cref="Fields"/> plus these overrides.</summary>
        /// <remarks>The actions set in this dictionary are executed when the frame is just about to be sent.</remarks>
        public IDictionary<int, EncodeAction> FieldsOverrides { get; set; } =
            ImmutableDictionary<int, EncodeAction>.Empty;

        /// <summary>Gets or sets the payload sink of this frame.</summary>
        public abstract PipeWriter PayloadSink { get; set; }

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
        protected OutgoingFrame(Protocol protocol)
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
        }

        /// <summary>Completes the frame payload pipe readers.</summary>
        /// <remarks>The completion of the payload sink is the responsibility of the subclasses that implement the
        /// abstract <see cref="PayloadSink"/> property.</remarks>
        internal virtual async ValueTask CompleteAsync(Exception? exception = null)
        {
            await PayloadSource.CompleteAsync(exception).ConfigureAwait(false);
            if (PayloadSourceStream != null)
            {
                await PayloadSourceStream.CompleteAsync(exception).ConfigureAwait(false);
            }
        }
    }
}

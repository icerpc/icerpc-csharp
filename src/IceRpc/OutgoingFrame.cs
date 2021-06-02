// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Base class for outgoing frames.</summary>
    public abstract class OutgoingFrame
    {
        /// <summary>Returns a dictionary used to override the fields of this frame. The full fields are a combination
        /// of the <see cref="InitialFields"/> plus these overrides.</summary>
        /// <remarks>The actions set in this dictionary are executed when the frame is sent.</remarks>
        public Dictionary<int, Action<OutputStream>> FieldsOverride
        {
            get
            {
                if (_fieldsOverride == null)
                {
                    if (Protocol == Protocol.Ice1)
                    {
                        throw new NotSupportedException("ice1 does not support header fields");
                    }

                    _fieldsOverride = new Dictionary<int, Action<OutputStream>>();
                }
                return _fieldsOverride;
            }
        }

        /// <summary>The features of this frame.</summary>
        public FeatureCollection Features { get; set; } = FeatureCollection.Empty;

        /// <summary>Returns true when the payload is compressed; otherwise, returns false.</summary>
        public bool HasCompressedPayload => PayloadCompressionFormat != CompressionFormat.NotCompressed;

        /// <summary>Returns the initial fields set during construction of this frame. See also
        /// <see cref="FieldsOverride"/>.</summary>
        public abstract IReadOnlyDictionary<int, ReadOnlyMemory<byte>> InitialFields { get; }

        /// <summary>Gets or sets the payload of this frame.</summary>
        public abstract IList<ArraySegment<byte>> Payload { get; set; }

        /// <summary>Returns the payload's compression format.</summary>
        public abstract CompressionFormat PayloadCompressionFormat { get; private protected set; }

        /// <summary>Returns the encoding of the payload of this frame.</summary>
        /// <remarks>The header of the frame is always encoded using the frame protocol's encoding.</remarks>
        public abstract Encoding PayloadEncoding { get; private protected set; }

        /// <summary>Returns the number of bytes in the payload.</summary>
        public abstract int PayloadSize { get; }

        /// <summary>Returns the Ice protocol of this frame.</summary>
        public Protocol Protocol { get; }

        /// <summary>The stream data writer if the request or response has an outgoing stream param. The writer is
        /// called after the request or response frame is sent over a socket stream.</summary>
        internal Action<SocketStream>? StreamDataWriter { get; set; }

        private Dictionary<int, Action<OutputStream>>? _fieldsOverride;

        /// <summary>Returns a new incoming frame built from this outgoing frame. This method is used for colocated
        /// calls.</summary>
        internal abstract IncomingFrame ToIncoming();

        /// <summary>Gets or builds a combined fields dictionary using InitialFields and _fieldsOverride. This method is
        /// used for colocated calls.</summary>
        internal IReadOnlyDictionary<int, ReadOnlyMemory<byte>> GetFields()
        {
            if (_fieldsOverride == null)
            {
                return InitialFields;
            }
            else
            {
                // Need to marshal/unmarshal these fields
                var buffer = new List<ArraySegment<byte>>();
                var ostr = new OutputStream(Encoding.V20, buffer);
                WriteFields(ostr);
                ostr.Finish();
                return buffer.AsArraySegment().AsReadOnlyMemory().Read(istr => istr.ReadFieldDictionary());
            }
        }

        /// <summary>Writes the header of a frame. This header does not include the frame's prologue.</summary>
        /// <param name="ostr">The output stream.</param>
        internal abstract void WriteHeader(OutputStream ostr);

        private protected OutgoingFrame(Protocol protocol, FeatureCollection features)
        {
            Protocol = protocol;
            Protocol.CheckSupported();
            Features = features;
        }

        private protected void WriteFields(OutputStream ostr)
        {
            Debug.Assert(Protocol == Protocol.Ice2);
            Debug.Assert(ostr.Encoding == Encoding.V20);

            int sizeLength =
                OutputStream.GetSizeLength20(InitialFields.Count + (_fieldsOverride?.Count ?? 0));

            int size = 0;

            OutputStream.Position start = ostr.StartFixedLengthSize(sizeLength);

            // First write the overrides, then the InitialFields lines that were not overridden.

            if (_fieldsOverride is Dictionary<int, Action<OutputStream>> fieldsOverride)
            {
                foreach ((int key, Action<OutputStream> action) in fieldsOverride)
                {
                    ostr.WriteVarInt(key);
                    OutputStream.Position startValue = ostr.StartFixedLengthSize(2);
                    action(ostr);
                    ostr.EndFixedLengthSize(startValue, 2);
                    size++;
                }
            }
            foreach ((int key, ReadOnlyMemory<byte> value) in InitialFields)
            {
                if (_fieldsOverride == null || !_fieldsOverride.ContainsKey(key))
                {
                    ostr.WriteVarInt(key);
                    ostr.WriteSize(value.Length);
                    ostr.WriteByteSpan(value.Span);
                    size++;
                }
            }
            ostr.RewriteFixedLengthSize20(size, start, sizeLength);
        }
    }
}

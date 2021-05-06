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
        /// <summary>Returns a dictionary used to override the binary context of this frame. The full binary context
        /// is a combination of the <see cref="InitialBinaryContext"/> plus these overrides.</summary>
        /// <remarks>The actions set in this dictionary are executed when the frame is sent.</remarks>
        public Dictionary<int, Action<OutputStream>> BinaryContextOverride
        {
            get
            {
                if (_binaryContextOverride == null)
                {
                    if (Protocol == Protocol.Ice1)
                    {
                        throw new NotSupportedException("ice1 does not support binary contexts");
                    }

                    _binaryContextOverride = new Dictionary<int, Action<OutputStream>>();
                }
                return _binaryContextOverride;
            }
        }

        /// <summary>The features of this frame.</summary>
        public FeatureCollection Features
        {
            get => _features ??= new FeatureCollection();
            set => _features = value;
        }

        /// <summary>Returns true when the payload is compressed; otherwise, returns false.</summary>
        public bool HasCompressedPayload => PayloadCompressionFormat != CompressionFormat.Decompressed;

        /// <summary>Returns the initial binary context set during construction of this frame. See also
        /// <see cref="BinaryContextOverride"/>.</summary>
        public abstract IReadOnlyDictionary<int, ReadOnlyMemory<byte>> InitialBinaryContext { get; }

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

        private Dictionary<int, Action<OutputStream>>? _binaryContextOverride;
        private FeatureCollection? _features;
        private IList<ArraySegment<byte>> _payload = new List<ArraySegment<byte>>();
        private int _payloadSize = -1; // -1 means not initialized

        /// <summary>Returns a new incoming frame built from this outgoing frame. This method is used for colocated
        /// calls.</summary>
        internal abstract IncomingFrame ToIncoming();

        /// <summary>Gets or builds a combined binary context using InitialBinaryContext and _binaryContextOverride.
        /// This method is used for colocated calls.</summary>
        internal IReadOnlyDictionary<int, ReadOnlyMemory<byte>> GetBinaryContext()
        {
            if (_binaryContextOverride == null)
            {
                return InitialBinaryContext;
            }
            else
            {
                // Need to marshal/unmarshal this binary context
                var buffer = new List<ArraySegment<byte>>();
                var ostr = new OutputStream(Encoding.V20, buffer);
                WriteBinaryContext(ostr);
                ostr.Finish();
                return buffer.AsArraySegment().AsReadOnlyMemory().Read(istr => istr.ReadBinaryContext());
            }
        }

        /// <summary>Writes the header of a frame. This header does not include the frame's prologue.</summary>
        /// <param name="ostr">The output stream.</param>
        internal abstract void WriteHeader(OutputStream ostr);

        private protected OutgoingFrame(Protocol protocol, FeatureCollection? features)
        {
            Protocol = protocol;
            Protocol.CheckSupported();
            _features = features;
        }

        private protected void WriteBinaryContext(OutputStream ostr)
        {
            Debug.Assert(Protocol == Protocol.Ice2);
            Debug.Assert(ostr.Encoding == Encoding.V20);

            int sizeLength =
                OutputStream.GetSizeLength20(InitialBinaryContext.Count + (_binaryContextOverride?.Count ?? 0));

            int size = 0;

            OutputStream.Position start = ostr.StartFixedLengthSize(sizeLength);

            // First write the overrides, then the InitialBinaryContext entries that were not overridden.

            if (_binaryContextOverride is Dictionary<int, Action<OutputStream>> binaryContextOverride)
            {
                foreach ((int key, Action<OutputStream> action) in binaryContextOverride)
                {
                    ostr.WriteVarInt(key);
                    OutputStream.Position startValue = ostr.StartFixedLengthSize(2);
                    action(ostr);
                    ostr.EndFixedLengthSize(startValue, 2);
                    size++;
                }
            }
            foreach ((int key, ReadOnlyMemory<byte> value) in InitialBinaryContext)
            {
                if (_binaryContextOverride == null || !_binaryContextOverride.ContainsKey(key))
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

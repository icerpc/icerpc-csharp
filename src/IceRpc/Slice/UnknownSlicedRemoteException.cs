// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Slice
{
    /// <summary>A remote exception that was fully sliced during decoding.</summary>
    public sealed class UnknownSlicedRemoteException : RemoteException
    {
        /// <summary>The type ID of the remote exception we received but could not decode.</summary>
        public string TypeId { get; }

        /// <inheritdoc/>
        public override void EncodeTrait(ref SliceEncoder encoder) =>
            throw new InvalidOperationException("cannot encode unknown remote exceptions");

        /// <inheritdoc/>
        protected override string? DefaultMessage =>
            $"{nameof(UnknownSlicedRemoteException)} {{ TypeId = {TypeId} }}";

        /// <inheritdoc/>
        protected override void DecodeCore(ref SliceDecoder decoder) => Debug.Assert(false);

        /// <inheritdoc/>
        protected override void EncodeCore(ref SliceEncoder encoder) =>
            throw new InvalidOperationException("cannot encode unknown remote exceptions");

        // Encode uses base class and does not include TypeId.

        /// <summary>Constructs an unknown sliced remote exception.</summary>
        /// <param name="typeId">The remote exception type ID.</param>
        internal UnknownSlicedRemoteException(string typeId) => TypeId = typeId;

        /// <summary>Constructs an unknown sliced remote exception.</summary>
        /// <param name="typeId">The remote exception type ID.</param>
        /// <param name="decoder">The Slice decoder.</param>
        internal UnknownSlicedRemoteException(string typeId, ref SliceDecoder decoder)
            : base(ref decoder) => TypeId = typeId;
    }
}

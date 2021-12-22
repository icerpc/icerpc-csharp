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
        protected override string? DefaultMessage =>
            $"{nameof(UnknownSlicedRemoteException)} {{ TypeId = {TypeId}, Origin = {Origin} }}";

        /// <inheritdoc/>
        protected override void IceDecode(ref IceDecoder decoder) => Debug.Assert(false);

        // IceEncode use base class and does not include TypeId.

        /// <summary>Constructs an unknown sliced remote exception.</summary>
        /// <param name="typeId">The remote exception type ID.</param>
        internal UnknownSlicedRemoteException(string typeId) => TypeId = typeId;

        /// <summary>Constructs an unknown sliced remote exception.</summary>
        /// <param name="typeId">The remote exception type ID.</param>
        /// <param name="decoder">The Ice decoder.</param>
        internal UnknownSlicedRemoteException(string typeId, ref IceDecoder decoder)
            : base(ref decoder) => TypeId = typeId;
    }
}

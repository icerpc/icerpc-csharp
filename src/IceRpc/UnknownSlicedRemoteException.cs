// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc
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
        protected override void IceDecode(Ice11Decoder decoder) => Debug.Assert(false);

        // IceEncode use base class and does not include TypeId.

        /// <summary>Constructs an unknown sliced remote exception with a type ID.</summary>
        /// <param name="typeId">The remote exception type ID.</param>
        internal UnknownSlicedRemoteException(string typeId) => TypeId = typeId;

        /// <summary>Constructs an unknown sliced remote exception with the provided message, origin and type ID.
        /// </summary>
        /// <param name="message">Message that describes the exception.</param>
        /// <param name="origin">The remote exception origin.</param>
        /// <param name="typeId">The remote exception type ID.</param>
        internal UnknownSlicedRemoteException(string message, RemoteExceptionOrigin origin, string typeId)
            : base(message, origin) => TypeId = typeId;
    }
}

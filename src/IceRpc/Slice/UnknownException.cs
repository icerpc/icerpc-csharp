// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    // Extends the generated UnknownException class.
    public partial class UnknownException
    {
        /// <inheritdoc/>
        protected override string? DefaultMessage =>
            $"{nameof(UnknownException)} {{ TypeId = {TypeId}, Message = {SliceMessage} }}";
    }
}

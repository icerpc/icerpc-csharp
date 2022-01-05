// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Features.Internal
{
    /// <summary>A feature that represents an ice request context or an icerpc Context request header field.</summary>
    internal sealed class Context
    {
        /// <summary>The value of this context feature.</summary>
        internal IDictionary<string, string> Value { get; init; } = ImmutableSortedDictionary<string, string>.Empty;
    }
}

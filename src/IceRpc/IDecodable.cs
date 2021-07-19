// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace IceRpc
{
    /// <summary>Represents a type that can be decoded from an Ice decoder.</summary>
    public interface IDecodable
    {
        /// <summary>Decodes the fields and properties of this new instance.</summary>
        [SuppressMessage("Microsoft.Design",
                         "CA1044: Because property IceDecoder is write-only",
                         Justification = "we use IceDecoder only for initialization; a get would be meaningless")]
        IceDecoder IceDecoder { init; }
    }
}

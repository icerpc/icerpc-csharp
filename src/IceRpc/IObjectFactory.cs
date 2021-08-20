// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Reflection;

namespace IceRpc
{

    /// <summary>Helper used by Ice decoders to create instances of classes and exceptions.</summary>
    public interface IObjectFactory<T> where T : IceDecoder
    {
        /// <summary>Creates an instance of a class or remote exceptions based on a type ID.</summary>
        /// <param name="typeId">The Ice type ID.</param>
        /// <param name="decoder">The decoder.</param>
        /// <returns>A new instance of {T}. This instance may be fully decoded using decoder, or only partially decoded.
        /// </returns>
        object? CreateInstance(string typeId, T decoder);
    }
}

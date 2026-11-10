// Copyright (c) ZeroC, Inc.

using System.Collections.Generic;

namespace ZeroC.Slice.Generator.Tests;

/// <summary>A dictionary type with a capacity constructor, the contract for the argument of a cs::type attribute on a
/// dictionary type.</summary>
public class CustomDictionary<TKey, TValue> : Dictionary<TKey, TValue> where TKey : notnull
{
    public CustomDictionary(int capacity)
        : base(capacity)
    {
    }
}

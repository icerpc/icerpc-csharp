// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Internal
{
    /// <summary>Extension methods for dictionaries.</summary>
    internal static class DictionaryExtensions
    {
        /// <summary>Checks if two dictionaries are equal. The order of the elements in the dictionaries does not
        /// matter.</summary>
        internal static bool DictionaryEqual<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> lhs,
            IReadOnlyDictionary<TKey, TValue> rhs,
            IEqualityComparer<TValue>? valueComparer = null)
        {
            if (rhs.Count != lhs.Count)
            {
                return false;
            }

            valueComparer ??= EqualityComparer<TValue>.Default;

            foreach ((TKey key, TValue value) in lhs)
            {
                if (!rhs.TryGetValue(key, out TValue? otherValue) || !valueComparer.Equals(value, otherValue))
                {
                    return false;
                }
            }

            return true;
        }
    }
}

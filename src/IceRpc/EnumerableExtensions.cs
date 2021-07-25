// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace IceRpc
{
    /// <summary>This class contains extensions methods to compute sequences hash code.</summary>
    public static class EnumerableExtensions
    {
        /// <summary>Computes the hash code for a sequence, using each element's default comparer.</summary>
        /// <param name="sequence">The sequence.</param>
        /// <typeparam name="T">The type of sequence's element.</typeparam>
        /// <returns>A hash code computed using the sequence's elements.</returns>
        public static int GetSequenceHashCode<T>(this IEnumerable<T> sequence) =>
            GetSequenceHashCode(sequence, comparer: null);

        /// <summary>Computes the hash code for a sequence.</summary>
        /// <typeparam name="T">The type of sequence's element.</typeparam>
        /// <param name="sequence">The sequence.</param>
        /// <param name="comparer">The comparer used to get each element's hash code. When null, this method uses the
        /// default comparer.</param>
        /// <returns>A hash code computed using the sequence's elements.</returns>
        public static int GetSequenceHashCode<T>(this IEnumerable<T> sequence, IEqualityComparer<T>? comparer)
        {
            var hash = new HashCode();
            foreach (T element in sequence)
            {
                hash.Add(element == null ? 0 : comparer?.GetHashCode(element) ?? element.GetHashCode());
            }
            return hash.ToHashCode();
        }
    }
}

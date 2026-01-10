// Copyright (c) ZeroC, Inc.

using System.Runtime.CompilerServices;

namespace IceRpc.Transports.Internal;

/// <summary>The flag enum extensions help with atomically set/get enumeration flags on an integer value. The
/// enumeration underlying type must be an integer as well.</summary>
internal static class FlagEnumExtensions
{
    /// <summary>Extension methods for <see cref="int" />.</summary>
    /// <param name="source">The source integer value.</param>
    extension(ref int source)
    {
        internal bool TrySetFlag<T>(T flag) where T : Enum
        {
            int flagValue = Unsafe.As<T, int>(ref flag);
            int previousValue = Interlocked.Or(ref source, flagValue);
            return previousValue != (previousValue | flagValue);
        }

        internal bool TrySetFlag<T>(T flag, out int newValue) where T : Enum
        {
            int flagValue = Unsafe.As<T, int>(ref flag);
            int previousValue = Interlocked.Or(ref source, flagValue);
            newValue = previousValue | flagValue;
            return previousValue != newValue;
        }

        internal bool HasFlag<T>(T flag) where T : Enum
        {
            int flagValue = Unsafe.As<T, int>(ref flag);
            return (Volatile.Read(ref source) & flagValue) == flagValue;
        }

        internal void ClearFlag<T>(T flag) where T : Enum
        {
            int flagValue = Unsafe.As<T, int>(ref flag);
            int previousValue = Interlocked.And(ref source, ~flagValue);
            if (previousValue == (previousValue & ~flagValue))
            {
                throw new InvalidOperationException("The state was already cleared.");
            }
        }
    }
}

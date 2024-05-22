// Copyright (c) ZeroC, Inc.

using System.Runtime.CompilerServices;

namespace IceRpc.Transports.Internal;

/// <summary>The flag enum extensions help with atomically set/get enumeration flags on an integer value. The
/// enumeration underlying type must be an integer as well.</summary>
internal static class FlagEnumExtensions
{
    internal static bool TrySetFlag<T>(this ref int source, T flag) where T : Enum
    {
        int flagValue = Unsafe.As<T, int>(ref flag);
        int previousValue = Interlocked.Or(ref source, flagValue);
        return previousValue != (previousValue | flagValue);
    }

    internal static bool TrySetFlag<T>(this ref int source, T flag, out int newValue) where T : Enum
    {
        int flagValue = Unsafe.As<T, int>(ref flag);
        int previousValue = Interlocked.Or(ref source, flagValue);
        newValue = previousValue | flagValue;
        return previousValue != newValue;
    }

    internal static bool HasFlag<T>(this ref int source, T flag) where T : Enum
    {
        int flagValue = Unsafe.As<T, int>(ref flag);
        return (Volatile.Read(ref source) & flagValue) == flagValue;
    }

    internal static void ClearFlag<T>(this ref int source, T flag) where T : Enum
    {
        int flagValue = Unsafe.As<T, int>(ref flag);
        int previousValue = Interlocked.And(ref source, ~flagValue);
        if (previousValue == (previousValue & ~flagValue))
        {
            throw new InvalidOperationException("The state was already cleared.");
        }
    }
}

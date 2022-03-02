// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Runtime.CompilerServices;

namespace IceRpc.Transports.Internal
{
    /// <summary>The flag enum extensions help with atomically set/get enumeration flags on a integer value. The
    /// enumeration underlying type must be an integer as well.</summary>
    internal static class FlagEnumExtensions
    {
        internal static bool TrySetFlag<T>(this ref int source, T value) where T : Enum
        {
            int state = Unsafe.As<T, int>(ref value);
            int previousState = Interlocked.Or(ref source, state);
            return previousState != (previousState | state);
        }

        internal static bool TrySetFlag<T>(this ref int source, T value, out T newValue) where T : Enum
        {
            int state = Unsafe.As<T, int>(ref value);
            int previousState = Interlocked.Or(ref source, state);
            int newState = previousState | state;
            newValue = Unsafe.As<int, T>(ref newState);
            return previousState != newState;
        }

        internal static bool HasFlag<T>(this ref int source, T value) where T : Enum =>
            (Thread.VolatileRead(ref source) & Unsafe.As<T, int>(ref value)) > 0;

        internal static void ClearFlag<T>(this ref int source, T value) where T : Enum
        {
            int state = Unsafe.As<T, int>(ref value);
            int previousState = Interlocked.And(ref source, ~state);
            if (previousState == (previousState & ~state))
            {
                throw new InvalidOperationException("state was already cleared");
            }
        }
    }
}

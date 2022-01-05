// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    /// <summary>An enumerator over one or more <see cref="Span{T}"/> of bytes. Used by <see cref="BitSequenceWriter"/>.
    /// </summary>
    internal ref struct SpanEnumerator
    {
        /// <summary>Returns the current span.</summary>
        internal Span<byte> Current => _position >= 0 ? _currentSpan :
            throw new InvalidOperationException("enumerator not initialized");

        private Span<byte> _currentSpan;
        private readonly Span<byte> _firstSpan;
        private readonly Span<byte> _secondSpan;
        private readonly IList<Memory<byte>>? _additionalMemory;
        private int _position;

        /// <summary>Moves to the next span.</summary>
        /// <returns><c>true</c> when the operation was successful, and <c>false</c> when the current span is the last
        /// span.</returns>
        internal bool MoveNext()
        {
            switch (_position)
            {
                case -1:
                    _position = 0;
                    _currentSpan = _firstSpan;
                    return true;
                case 0:
                    if (_secondSpan.Length > 0)
                    {
                        _position = 1;
                        _currentSpan = _secondSpan;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                default:
                    if (_additionalMemory != null && _additionalMemory.Count > _position - 1)
                    {
                        _position += 1;
                        _currentSpan = _additionalMemory[_position - 2].Span;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
            }
        }

        /// <summary>Constructs a span enumerator.</summary>
        internal SpanEnumerator(
            Span<byte> firstSpan,
            Span<byte> secondSpan = default,
            IList<Memory<byte>>? additionalMemory = null)
        {
            _firstSpan = firstSpan;
            _secondSpan = secondSpan;
            _additionalMemory = additionalMemory;

            _currentSpan = default;
            _position = -1;
        }
    }
}

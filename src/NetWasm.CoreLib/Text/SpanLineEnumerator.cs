// Portions derived from dotnet/runtime System.Private.CoreLib SpanLineEnumerator.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;

namespace System.Text
{
    /// <summary>Enumerates lines in a UTF-16 span.</summary>
    public ref struct SpanLineEnumerator : IEnumerator<ReadOnlySpan<char>>, IEnumerator, IDisposable
    {
        private ReadOnlySpan<char> _remaining;
        private ReadOnlySpan<char> _current;
        private bool _isEnumeratorActive;

        internal SpanLineEnumerator(ReadOnlySpan<char> source)
        {
            _remaining = source;
            _current = default;
            _isEnumeratorActive = true;
        }

        public ReadOnlySpan<char> Current
        {
            get { return _current; }
        }

        public SpanLineEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (!_isEnumeratorActive)
            {
                _current = default;
                return false;
            }

            var remaining = _remaining;

            var lineBreak = -1;
            for (var index = 0; index < remaining.Length; index++)
            {
                if (remaining[index] is '\r' or '\n' or '\f' or '\u0085' or '\u2028' or '\u2029')
                {
                    lineBreak = index;
                    break;
                }
            }

            if (lineBreak < 0)
            {
                _current = remaining;
                _remaining = default;
                _isEnumeratorActive = false;
                return true;
            }

            var breakLength = 1;
            if (remaining[lineBreak] == '\r' && lineBreak + 1 < remaining.Length && remaining[lineBreak + 1] == '\n')
            {
                breakLength = 2;
            }
            _current = remaining.Slice(0, lineBreak);
            _remaining = remaining.Slice(lineBreak + breakLength);
            return true;
        }

        object IEnumerator.Current => throw new NotSupportedException();
        void IEnumerator.Reset() => throw new NotSupportedException();
        void IDisposable.Dispose() { }
    }
}

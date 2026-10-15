// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Buffers
{
    internal readonly struct AhoCorasick
    {
        private readonly AhoCorasickNode[] _nodes;

        public AhoCorasick(AhoCorasickNode[] nodes)
        {
            _nodes = nodes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly int IndexOfAny(ReadOnlySpan<char> span)
        {
            ref AhoCorasickNode nodes = ref MemoryMarshal.GetArrayDataReference(_nodes);
            var nodeIndex = 0;
            var result = -1;
            var index = 0;

        Loop:
            if ((uint)index >= (uint)span.Length)
            {
                goto Return;
            }

            var c = Unsafe.Add(ref MemoryMarshal.GetReference(span), index);

            while (true)
            {
                Debug.Assert((uint)nodeIndex < (uint)_nodes.Length);
                ref AhoCorasickNode node = ref Unsafe.Add(ref nodes, (uint)nodeIndex);

                if (node.TryGetChild(c, out var childIndex))
                {
                    nodeIndex = childIndex;

                    Debug.Assert((uint)nodeIndex < (uint)_nodes.Length);
                    var matchLength = Unsafe.Add(ref nodes, (uint)nodeIndex).MatchLength;
                    if (matchLength != 0)
                    {
                        Debug.Assert(result == -1 || result >= index + 1 - matchLength);
                        result = index + 1 - matchLength;
                    }

                    index++;
                    goto Loop;
                }

                if (nodeIndex == 0)
                {
                    if (result >= 0)
                    {
                        goto Return;
                    }

                    index++;
                    goto Loop;
                }

                nodeIndex = node.SuffixLink;
                if (nodeIndex < 0)
                {
                    Debug.Assert(nodeIndex == -1);
                    Debug.Assert(result >= 0);
                    goto Return;
                }
            }

        Return:
            return result;
        }
    }
}

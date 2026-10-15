// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;

namespace System.Buffers
{
    internal ref struct AhoCorasickBuilder
    {
        private readonly ReadOnlySpan<string> _values;
        private ValueListBuilder<AhoCorasickNode> _nodes;
        private ValueListBuilder<int> _parents;

        public AhoCorasickBuilder(
            ReadOnlySpan<string> values,
            ref HashSet<string>? unreachableValues)
        {
            Debug.Assert(!values.IsEmpty);
            Debug.Assert(!string.IsNullOrEmpty(values[0]));

#if DEBUG
            for (var index = 1; index < values.Length; index++)
            {
                Debug.Assert(values[index - 1].Length <= values[index].Length);
            }
#endif

            _values = values;
            BuildTrie(ref unreachableValues);
        }

        public AhoCorasick Build()
        {
            AddSuffixLinks();
            Debug.Assert(_nodes[0].MatchLength == 0);

            for (var index = 0; index < _nodes.Length; index++)
            {
                _nodes[index].OptimizeChildren();
            }

            return new AhoCorasick(_nodes.AsSpan().ToArray());
        }

        public void Dispose()
        {
            _nodes.Dispose();
            _parents.Dispose();
        }

        private void BuildTrie(ref HashSet<string>? unreachableValues)
        {
            _nodes.Append(new AhoCorasickNode());
            _parents.Append(0);

            foreach (string value in _values)
            {
                var nodeIndex = 0;
                ref AhoCorasickNode node = ref _nodes[nodeIndex];

                for (var index = 0; index < value.Length; index++)
                {
                    var c = value[index];
                    if (!node.TryGetChild(c, out var childIndex))
                    {
                        childIndex = _nodes.Length;
                        node.AddChild(c, childIndex);
                        _nodes.Append(new AhoCorasickNode());
                        _parents.Append(nodeIndex);
                    }

                    node = ref _nodes[childIndex];
                    nodeIndex = childIndex;

                    if (node.MatchLength != 0)
                    {
                        unreachableValues ??= new HashSet<string>(StringComparer.Ordinal);
                        unreachableValues.Add(value);
                        break;
                    }

                    if (index == value.Length - 1)
                    {
                        node.MatchLength = value.Length;
                        break;
                    }
                }
            }
        }

        private void AddSuffixLinks()
        {
            var queue = new Queue<(char Char, int Index)>();
            queue.Enqueue(((char)0, 0));

            while (queue.TryDequeue(out (char Char, int Index) trieNode))
            {
                ref AhoCorasickNode node = ref _nodes[trieNode.Index];
                var parent = _parents[trieNode.Index];
                var suffixLink = _nodes[parent].SuffixLink;

                if (parent != 0)
                {
                    while (suffixLink >= 0)
                    {
                        ref AhoCorasickNode suffixNode = ref _nodes[suffixLink];
                        if (suffixNode.TryGetChild(trieNode.Char, out var childSuffixLink))
                        {
                            suffixLink = childSuffixLink;
                            break;
                        }

                        if (suffixLink == 0)
                        {
                            break;
                        }

                        suffixLink = suffixNode.SuffixLink;
                    }
                }

                if (node.MatchLength != 0)
                {
                    node.SuffixLink = -1;
                }
                else
                {
                    node.SuffixLink = suffixLink;
                    if (suffixLink >= 0)
                    {
                        node.MatchLength = _nodes[suffixLink].MatchLength;
                    }

                    node.AddChildrenToQueue(queue);
                }
            }
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Buffers
{
    internal static class CharacterFrequencyHelper
    {
        public static ReadOnlySpan<float> AsciiFrequency =>
        [
            0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f,
            0.000f, 0.001f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f,
            0.000f, 0.000f, 0.000f, 0.000f, 0.003f, 0.000f, 0.000f, 0.000f,
            0.000f, 0.004f, 0.000f, 0.000f, 0.006f, 0.006f, 0.000f, 0.000f,
            8.952f, 0.065f, 0.420f, 0.010f, 0.011f, 0.005f, 0.070f, 0.050f,
            3.911f, 3.910f, 0.356f, 2.775f, 1.411f, 0.173f, 2.054f, 0.677f,
            1.199f, 0.870f, 0.729f, 0.491f, 0.335f, 0.269f, 0.435f, 0.240f,
            0.234f, 0.196f, 0.144f, 0.983f, 0.357f, 0.661f, 0.371f, 0.088f,
            0.007f, 0.763f, 0.229f, 0.551f, 0.306f, 0.449f, 0.337f, 0.162f,
            0.131f, 0.489f, 0.031f, 0.035f, 0.301f, 0.205f, 0.253f, 0.228f,
            0.288f, 0.034f, 0.380f, 0.730f, 0.675f, 0.265f, 0.309f, 0.137f,
            0.084f, 0.023f, 0.023f, 0.591f, 0.085f, 0.590f, 0.013f, 0.797f,
            0.001f, 4.596f, 1.296f, 2.081f, 2.005f, 6.903f, 1.494f, 1.019f,
            1.024f, 3.750f, 0.286f, 0.439f, 2.913f, 1.459f, 3.908f, 3.230f,
            1.444f, 0.231f, 4.220f, 3.924f, 5.312f, 2.112f, 0.737f, 0.573f,
            0.992f, 1.067f, 0.181f, 0.391f, 0.056f, 0.391f, 0.002f, 0.000f,
        ];
    }
}

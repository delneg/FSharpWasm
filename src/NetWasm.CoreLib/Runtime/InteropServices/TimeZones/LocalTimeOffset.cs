namespace System.Runtime.InteropServices.TimeZones
{
    internal readonly struct LocalTimeOffset
    {
        internal LocalTimeOffset(
            long ticks,
            bool isDaylightSavingTime,
            bool isAmbiguousDaylightSavingTime = false)
        {
            Ticks = ticks;
            IsDaylightSavingTime = isDaylightSavingTime;
            IsAmbiguousDaylightSavingTime = isAmbiguousDaylightSavingTime;
        }

        internal long Ticks { get; }

        internal bool IsDaylightSavingTime { get; }

        internal bool IsAmbiguousDaylightSavingTime { get; }
    }
}

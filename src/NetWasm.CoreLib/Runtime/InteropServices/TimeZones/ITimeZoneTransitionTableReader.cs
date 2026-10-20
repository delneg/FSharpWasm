namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneTransitionTableReader
    {
        TimeZoneTransition[] Read(TimeZoneAsset asset, TimeZoneAssetEntry zone);
    }
}

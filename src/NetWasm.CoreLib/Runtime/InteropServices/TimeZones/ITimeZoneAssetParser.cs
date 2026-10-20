namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneAssetParser
    {
        TimeZoneAsset Parse(ITimeZoneAssetFile file);
    }
}

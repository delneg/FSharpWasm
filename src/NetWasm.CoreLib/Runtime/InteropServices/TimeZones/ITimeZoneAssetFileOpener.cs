namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneAssetFileOpener
    {
        ITimeZoneAssetFile Open(string assetName);
    }
}

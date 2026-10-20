namespace System.Runtime.InteropServices.TimeZones
{
    internal interface ITimeZoneAssetFile
    {
        byte[] Read(long offset, int length);
    }
}

namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneCatalogSnapshotInstaller :
        ITimeZoneCatalogSnapshotInstaller
    {
        public void Install(TimeZoneCatalogSnapshot snapshot) =>
            PlatformServices.UseTimeZoneCatalogSnapshot(snapshot);
    }
}

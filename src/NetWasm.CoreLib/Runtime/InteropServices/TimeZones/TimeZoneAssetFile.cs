namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class TimeZoneAssetFile(
        int handle,
        IReadOnlyFileReader reader) : ITimeZoneAssetFile
    {
        private readonly IReadOnlyFileReader _reader =
            reader ?? throw new ArgumentNullException();

        public byte[] Read(long offset, int length) =>
            _reader.Read(handle, offset, length);
    }
}

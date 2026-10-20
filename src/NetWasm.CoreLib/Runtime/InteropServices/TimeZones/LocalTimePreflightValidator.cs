namespace System.Runtime.InteropServices.TimeZones
{
    internal sealed class LocalTimePreflightValidator(
        IEnvironmentVariableReader environment,
        ITimeZoneAssetFileOpener assetFiles,
        ITimeZoneAssetParser assets,
        ITimeZoneDefinitionResolverFactory definitions,
        ILocalTimeOffsetResolverFactory offsets,
        ILocalTimeOffsetResolver utcFallback,
        ILocalTimeOffsetResolverInstaller installer,
        ITimeZoneCatalogSnapshotInstaller catalogs) : ILocalTimePreflightValidator
    {
        internal const string AssetName = "netwasm-timezones.nwtz";
        internal const string RequiredDataVersion = "2026c";
        private readonly IEnvironmentVariableReader _environment =
            environment ?? throw new ArgumentNullException();
        private readonly ITimeZoneAssetFileOpener _assetFiles =
            assetFiles ?? throw new ArgumentNullException();
        private readonly ITimeZoneAssetParser _assets =
            assets ?? throw new ArgumentNullException();
        private readonly ITimeZoneDefinitionResolverFactory _definitions =
            definitions ?? throw new ArgumentNullException();
        private readonly ILocalTimeOffsetResolverFactory _offsets =
            offsets ?? throw new ArgumentNullException();
        private readonly ILocalTimeOffsetResolver _utcFallback =
            utcFallback ?? throw new ArgumentNullException();
        private readonly ILocalTimeOffsetResolverInstaller _installer =
            installer ?? throw new ArgumentNullException();
        private readonly ITimeZoneCatalogSnapshotInstaller _catalogs =
            catalogs ?? throw new ArgumentNullException();

        public void Validate()
        {
            var timeZone = _environment.Read("TZ");
            if (timeZone == null || timeZone is "UTC" or "Etc/UTC")
            {
                _installer.Install(_utcFallback);
                _catalogs.Install(new TimeZoneCatalogSnapshot(timeZone, null, null));
                return;
            }
            if (timeZone.Length == 0)
            {
                throw Failure(timeZone, "unavailable", "TZ is empty");
            }

            var identity = "unavailable";
            try
            {
                ITimeZoneAssetFile file;
                try
                {
                    file = _assetFiles.Open(AssetName);
                }
                catch (PlatformNotSupportedException exception)
                {
                    throw new PlatformNotSupportedException(
                        "asset could not be read: " + exception.Message);
                }
                TimeZoneAsset asset;
                try
                {
                    asset = _assets.Parse(file);
                }
                catch (TimeZoneAssetValidationException exception)
                {
                    throw new PlatformNotSupportedException(
                        "asset could not be validated: " + exception.Reason);
                }
                catch (PlatformNotSupportedException exception)
                {
                    throw new PlatformNotSupportedException(
                        "asset could not be validated: " + exception.Message);
                }
                identity = asset.Identity;
                if (asset.DataVersion != RequiredDataVersion)
                {
                    throw new PlatformNotSupportedException(
                        "asset data version is '" + asset.DataVersion + "'");
                }
                var definitions = _definitions.Create(asset);
                TimeZoneDefinition definition;
                try
                {
                    definition = definitions.Resolve(timeZone);
                }
                catch (TimeZoneAssetValidationException exception)
                {
                    throw new PlatformNotSupportedException(
                        "asset could not be validated: " + exception.Reason);
                }
                _installer.Install(_offsets.Create(definition));
                _catalogs.Install(new TimeZoneCatalogSnapshot(
                    timeZone,
                    asset,
                    definitions));
            }
            catch (PlatformNotSupportedException exception)
            {
                throw Failure(timeZone, identity, exception.Message);
            }
        }

        private static PlatformNotSupportedException Failure(
            string timeZone,
            string identity,
            string reason) => new(
                "Timezone preflight failed: TZ='" + timeZone +
                "'; asset='" + AssetName +
                "'; identity='" + identity +
                "'; required-version='" + RequiredDataVersion +
                "'; reason=" + reason + ".");
    }
}

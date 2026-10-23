#!/usr/bin/env bash
# Builds this repository's managed NetWasm packages into a local NuGet feed.
#
#   eng/build-local-feed.sh [--version VERSION] [--feed DIR]
#
# It packs the working tree, uncommitted changes included, in a disposable
# worktree, stamped with VERSION (default 0.10.2-fs.dev). The packages that need
# the maintainer-only native toolchain (NetWasm.Runtime.Pack, NetWasm.Toolchain,
# NetWasm.HostTools.*, NetWasm.Wit.Bindings.Tool) are not built: the SDK takes
# them from the upstream release named by NetWasmUpstreamNativePackageVersion.
# Earlier copies of VERSION are removed from the NuGet cache, so consumers see
# the new build.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd -P)"
VERSION="0.10.2-fs.dev"
FEED="${ROOT}/artifacts/feed"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --feed) FEED="$2"; shift 2 ;;
    -h|--help) sed -n 2,12p "$0"; exit 0 ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
mkdir -p "${FEED}"
FEED="$(cd "${FEED}" && pwd -P)"

work="$(mktemp -d /tmp/netwasm-feed.XXXXXX)"
src="${work}/source"
cleanup() {
  git -C "${ROOT}" worktree remove --force "${src}" >/dev/null 2>&1 || true
  rm -rf "${work}"
}
trap cleanup EXIT

git -C "${ROOT}" worktree add --quiet --detach "${src}" HEAD
# Carry the working tree over: tracked changes, then untracked files.
git -C "${ROOT}" diff --binary HEAD | git -C "${src}" apply --allow-empty --whitespace=nowarn
git -C "${ROOT}" ls-files -z --others --exclude-standard | (cd "${ROOT}" && xargs -0 -I{} rsync -R "{}" "${src}/")
git -C "${src}" add -A >/dev/null
git -C "${src}" -c user.name=feed -c user.email=feed@localhost commit -q --allow-empty -m "local feed ${VERSION}"

python3 "${src}/eng/project-release-version.py" \
  --source-root "${src}" --version "${VERSION}" --receipt "${work}/version.json"

cd "${src}"
dotnet restore NetWasm.slnx --nologo -v:q
dotnet build NetWasm.slnx -c Release --no-restore --nologo -v:q

cache="${NUGET_PACKAGES:-${HOME}/.nuget/packages}"
lower_version="$(printf '%s' "${VERSION}" | tr '[:upper:]' '[:lower:]')"
for dir in "${cache}"/netwasm.*/"${lower_version}"; do
  [[ -d "${dir}" ]] && rm -rf "${dir}"
done
find "${FEED}" -maxdepth 1 -name "NetWasm.*.${VERSION}.nupkg" -delete

projects=(
  src/NetWasm.Compiler.Tasks/NetWasm.Compiler.Tasks.csproj
  src/NetWasm.Compiler.Browser/NetWasm.Compiler.Browser.csproj
  src/NetWasm.Hosting/NetWasm.Hosting.csproj
  src/NetWasm.Hosting.Build/NetWasm.Hosting.Build.csproj
  src/NetWasm.Ref/NetWasm.Ref.csproj
  src/NetWasm.Runtime.Wasm32/NetWasm.Runtime.Wasm32.csproj
  src/NetWasm.Runtime.Wasm64/NetWasm.Runtime.Wasm64.csproj
  src/NetWasm.Sdk/NetWasm.Sdk.csproj
  src/NetWasm.Templates/NetWasm.Templates.csproj
  src/NetWasm.Testing.VSTest/NetWasm.Testing.VSTest.csproj
)
commit="$(git -C "${src}" rev-parse HEAD)"
for project in "${projects[@]}"; do
  dotnet pack "${project}" -c Release --no-restore --nologo -v:q -o "${FEED}" \
    -p:RepositoryCommit="${commit}" \
    -p:NetWasmSdkProvenanceSourceRevision="${commit}"
done
echo "NetWasm ${VERSION} packages in ${FEED}:"
ls -1 "${FEED}" | grep -F ".${VERSION}.nupkg"

#!/usr/bin/env bash

# Builds, packs, and publishes IceRPC NuGet packages to the local global-packages source.

set -ue

# Everything in this script is relative to the repository root.
cd "$(dirname "${BASH_SOURCE[0]}")/.."

# Read version from build/IceRpc.Version.props
version=$(sed -n 's/.*<Version[^>]*>\(.*\)<\/Version>.*/\1/p' build/IceRpc.Version.props)

build_configuration=${CONFIGURATION:-Debug}

echo "Publishing IceRPC $version ($build_configuration) to local global-packages..."

dotnet build -c "$build_configuration"
dotnet pack --no-build -c "$build_configuration"

pushd src/IceRpc.Templates
dotnet pack -c "$build_configuration"
popd

global_packages=$(dotnet nuget locals -l global-packages)
global_packages=${global_packages/global-packages: /""}

rm -rf "$global_packages"/zeroc.slice.*/"$version" \
       "$global_packages/icerpc/$version" \
       "$global_packages"/icerpc.*/"$version"

dotnet nuget push "src/**/$build_configuration/*.$version.nupkg" --source "$global_packages"

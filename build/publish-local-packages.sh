#!/usr/bin/env bash

# Builds, packs, and publishes IceRPC NuGet packages to the local global-packages source.

set -ue

# Everything in this script is relative to the repository root.
cd "$(dirname "${BASH_SOURCE[0]}")/.."

# Read version from build/IceRpc.Version.props
version=$(sed -n 's/.*<Version[^>]*>\(.*\)<\/Version>.*/\1/p' build/IceRpc.Version.props)

echo "Publishing IceRPC $version to local global-packages..."

dotnet build
dotnet pack

pushd src/IceRpc.Templates
dotnet pack
popd

global_packages=$(dotnet nuget locals -l global-packages)
global_packages=${global_packages/global-packages: /""}

rm -rf "$global_packages"/zeroc.slice.*/"$version" \
       "$global_packages/icerpc/$version" \
       "$global_packages"/icerpc.*/"$version"

dotnet nuget push "src/**/Debug/*.$version.nupkg" --source "$global_packages"

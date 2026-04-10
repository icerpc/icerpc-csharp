# Builds, packs, and publishes IceRPC NuGet packages to the local global-packages source.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Everything in this script is relative to the repository root.
Push-Location "$PSScriptRoot\.."

try {
    # Read version from build/IceRpc.Version.props
    Get-Content .\build\IceRpc.Version.props -Raw | Where-Object { $_ -match "<Version .*>(.*)</Version>" } | Out-Null
    $version = $Matches.1

    Write-Host "Publishing IceRPC $version to local global-packages..."

    dotnet build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet pack
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Push-Location "src\IceRpc.Templates"
    dotnet pack
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Pop-Location

    $global_packages = dotnet nuget locals -l global-packages
    $global_packages = $global_packages.replace("global-packages: ", "")

    Remove-Item "$global_packages\IceRpc.Slice.Tools\$version" -Recurse -Force -ErrorAction Ignore
    $packages = Get-ChildItem -Path "." -Include "*.$version.nupkg" -Recurse
    foreach ($package in $packages) {
        $package_name = $package.BaseName
        $package_name = $package_name.Substring(0, $package_name.Length - ".$version".Length)
        Remove-Item "$global_packages\$package_name\$version" -Recurse -Force -ErrorAction Ignore
    }

    dotnet nuget push "src\**\Debug\*.$version.nupkg" --source $global_packages
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} finally {
    Pop-Location
}

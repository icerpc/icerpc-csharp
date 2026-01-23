# Copyright (c) ZeroC, Inc.

[CmdletBinding(PositionalBinding=$false)]
param (
    [string]$config="debug",
    $version="",
    [switch]$build,
    [switch]$clean,
    [switch]$doc,
    [switch]$publish,
    [switch]$help,
    [switch]$coverage,
    [Parameter(ValueFromRemainingArguments=$true)][String[]]$properties
)

if ($version) {
    $versionProperty = "-p:Version=$version"
} else {
    Get-Content .\build\IceRpc.Version.props -Raw | Where { $_ -match "<Version .*>(.*)</Version>" } | Out-Null
    $version = $Matches.1
}

function Build($config) {
    Push-Location "tools\slicec-cs"
    $arguments = @('build')
    if ($config -eq 'release') {
        $arguments += '--release'
    }
    RunCommand 'cargo' $arguments
    Pop-Location

    $dotnetConfiguration = DotnetConfiguration($config)

    RunCommand "dotnet" @('build', $versionProperty, '--configuration', $dotnetConfiguration)
}

function Clean($config) {
    Push-Location "tools\slicec-cs"
    RunCommand "cargo" "clean"
    Pop-Location

    $dotnetConfiguration = DotnetConfiguration($config)

    RunCommand "dotnet" @('clean', $versionProperty, '--configuration', $dotnetConfiguration)

    Push-Location "src\IceRpc.Templates"
    RunCommand "dotnet" @('clean', $versionProperty, '--configuration', $dotnetConfiguration)
    Pop-Location
}

function Doc() {
    $dotnetConfiguration = DotnetConfiguration($config)
    Push-Location "docfx"
    RunCommand "docfx" @('metadata', '--property', "Configuration=$dotnetConfiguration")
    RunCommand "docfx" @('build')
    Pop-Location
}

function DotnetConfiguration($config) {
    if ($config -eq 'release') {
        'Release'
    } else {
        'Debug'
    }
}

function Get-Help() {
    Write-Host "Usage: build [actions] [arguments]"
    Write-Host ""
    Write-Host "Actions (defaults to -build):"
    Write-Host "  -build                    Build the IceRPC assemblies and the slicec-cs compiler."
    Write-Host "  -publish                  Creates and publishes the IceRPC NuGet packages to the local global-packages source."
    Write-Host "  -clean                    Clean all build artifacts."
    Write-Host "  -coverage                 Generate code coverage report from the tests runs."
    Write-Host "                            Requires reportgenerator command from https://github.com/danielpalme/ReportGenerator"
    Write-Host "  -doc                      Generate the C# API documentation"
    Write-Host "                            Requires docfx from https://github.com/dotnet/docfx"
    Write-Host ""
    Write-Host "Arguments:"
    Write-Host "  -config                   Build configuration: debug or release, the default is debug."
    Write-Host "  -version                  The version override for the IceRPC NuGet packages. The default version is the version"
    Write-Host "                            specified in the build/IceRpc.Version.props file."
    Write-Host "  -help                     Print help and exit."
}

function Publish($config) {
    Build $config
    $dotnetConfiguration = DotnetConfiguration($config)

    RunCommand "dotnet"  @('pack', $versionProperty, '--configuration', $dotnetConfiguration)

    Push-Location "src\IceRpc.Templates"
    RunCommand "dotnet" @('pack', $versionProperty, '--configuration', $dotnetConfiguration)
    Pop-Location

    $global_packages = dotnet nuget locals -l global-packages
    $global_packages = $global_packages.replace("global-packages: ", "")
    Remove-Item $global_packages"\IceRpc.Slice.Tools\$version" -Recurse -Force -ErrorAction Ignore
    $packages = Get-ChildItem -Path "." -Include *.$version.nupkg -Recurse
    foreach ($package in $packages)
    {
        $package_name = (Get-Item $package).Basename
        $package_name = $package_name.Substring(0, $package_name.Length - ".$version".Length)
        Remove-Item $global_packages"\$package_name\$version" -Recurse -Force -ErrorAction Ignore
    }
    RunCommand "dotnet" @('nuget', 'push', "src\**\$dotnetConfiguration\*.$version.nupkg", '--source', $global_packages)
}

function RunCommand($command, $arguments) {
    Write-Host $command $arguments
    & $command $arguments
    if ($lastExitCode -ne 0) {
        exit 1
    }
}

function CodeCoverage($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    $runsettings = Resolve-Path -Path "./build/Coverlet.runsettings"
    $arguments = @('test', '--configuration', $dotnetConfiguration, "-p:RunSettingsFilePath=$runsettings", '--collect:"XPlat Code Coverage"')

    RunCommand "dotnet" $arguments

    $arguments = @('-reports:tests/*/TestResults/*/coverage.cobertura.xml', '-targetdir:tests/CodeCoverageReport')
    if ($env:REPORTGENERATOR_LICENSE) {
        $arguments += @("-version:$env:REPORTGENERATOR_LICENSE")
    }
    RunCommand "reportgenerator" $arguments
    # Remove code coverage results after the report has been generated.
    Get-ChildItem -Path .\tests\ -Filter TestResults -Recurse | Remove-Item -Recurse -Force
}

$configs = "debug","release"
if ( $configs -notcontains $config ) {
    Write-Host "Invalid config: '$config', config must 'debug' or 'release'"
    Write-Host ""
    Get-Help
    exit 1
}

$actions = @("build", "clean", "doc", "coverage", "publish")
$passedInActions = @()

foreach ($key in $PSBoundParameters.Keys) {
    if ($actions -contains $key) {
        $passedInActions += @($key)
    }
}

if ($passedInActions.Length -eq 0) {
    $passedInActions = @("build")
}

if ($properties) {
   Write-Host "Unknown argument:" $properties[0]
   Write-Host ""
   Get-Help
   exit 1
}

if ( $help ) {
    Get-Help
    exit 0
}

foreach ($action in $passedInActions) {
    switch ( $action ) {
        "build" {
            Build $config
        }
        "publish" {
            Publish $config
        }
        "clean" {
            Clean $config
        }
        "coverage" {
           CodeCoverage $config
        }
        "doc" {
            Doc
        }
        "help" {
            Get-Help
        }
    }
}

exit 0

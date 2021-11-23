# Copyright (c) ZeroC, Inc. All rights reserved.

param (
    $action="build",
    $config="debug",
    [switch]$examples,
    [switch]$srcdist,
    [switch]$coverage,
    [switch]$help
)

function Build-Compiler($config) {
    Push-Location "tools\slicec-cs"
    $args = @('build')
    if ($config -eq 'release') {
        $args += '--release'
    }
    RunCommand 'cargo' $args
    Pop-Location
}

function Clean-Compiler($config) {
    Push-Location "tools\slicec-cs"
    RunCommand "cargo" "clean"
    Pop-Location
}

function Build-IceRpc($config) {
    $args = @('build')
    if ($config -eq 'release') {
        $args += @('--configuration', 'Release')
    } else {
        $args += @('--configuration', 'Debug')
    }
    RunCommand "dotnet" $args
}

function Build-IceRpcExamples($config) {
    $args = @('build')
    if ($config -eq 'release') {
        $args += @('--configuration', 'Release')
    } else {
        $args += @('--configuration', 'Debug')
    }
    $args += @("examples\Hello\Hello.sln")
    RunCommand "dotnet" $args
}

function Clean-IceRpc($config) {
    RunCommand "dotnet" "clean"
}

function Build($config, $examples, $srcdist) {
    if ($examples) {
        if ($srcdist) {
           Build-Compiler $config
           Pack $config
           $global_packages = dotnet nuget locals -l global-packages
           $global_packages = $global_packages.replace("global-packages: ", "")
           Remove-Item $global_packages"\icerpc" -Recurse -Force -ErrorAction Ignore
           Remove-Item $global_packages"\icerpc.interop" -Recurse -Force -ErrorAction Ignore
           RunCommand "dotnet" @('nuget', 'push', 'lib\*.nupkg', '--source', $global_packages)
        }
        Build-IceRpcExamples $config
    } else {
        Build-Compiler $config
        Build-IceRpc $config
    }
}

function Pack($config) {
    $args = @('pack')
    if ($config -eq 'release') {
        $args += @('--configuration', 'Release')
    } else {
        $args += @('--configuration', 'Debug')
    }
    RunCommand "dotnet" $args
}

function Rebuild($config) {
    Clean $config
    Build $config
}

function Clean($config) {
    Clean-Compiler($config)
    Clean-IceRpc($config)
}

function Test($config, $coverage) {
    $args = @('test', '--no-build')
    if ($config -eq 'release') {
        $args += @('--configuration', 'Release')
    } elseif ($config -eq 'debug') {
        $args += @('--configuration', 'Debug')
    }

    if ($coverage) {
       $args += @('--collect:"XPlat Code Coverage"')
    }
    RunCommand "dotnet" $args
    if ($coverage) {
        $args = @('-reports:tests/*/TestResults/*/coverage.cobertura.xml', '-targetdir:tests/CodeCoverageRerport')
        RunCommand "reportgenerator" $args
    }
}

function Doc() {
    Push-Location "doc"
    RunCommand "docfx"
    RunCommand explorer "_site\index.html"
    Pop-Location
}

function RunCommand($command, $arguments) {
    Write-Host $command $arguments
    & $command $arguments
    if ($lastExitCode -ne 0) {
        exit 1
    }
}

function Get-Help() {
    Write-Host "Usage: build [command] [arguments]"
    Write-Host "Commands (defaults to build):"
    Write-Host "  build                     Build IceRpc sources & slice-cs compiler."
    Write-Host "  pack                      Build the IceRpc NuGet packages."
    Write-Host "  clean                     Clean IceRpc sources & slice-cs compiler."
    Write-Host "  rebuild                   Rebuild IceRpc sources & slice-cs compiler."
    Write-Host "  test                      Runs tests."
    Write-Host "  doc                       Generate documentation"
    Write-Host "Arguments:"
    Write-Host "  -config                   Build configuration: debug or release, the default is debug."
    Write-Host "  -examples                 Build examples solutions instead of the source solutions."
    Write-Host "  -srcdist                  Use NuGet packages from this source distribution when building examples."
    Write-Host "                            The NuGet packages are installed to the local global-packages source."
    Write-Host "  -coverage                 Collect code coverage from test runs."
    Write-Host "                            Requires reportgeneratool from https://github.com/danielpalme/ReportGenerator"
    Write-Host "  -help                     Print help and exit."
}

$configs = "debug","release"
if ( $configs -notcontains $config ) {
    Get-Help
    throw new-object system.ArgumentException "config must debug or release"
}

if ( $help ) {
    Get-Help
    exit 0
}

switch ( $action ) {
    "build" {
        Build $config $examples $srcdist
    }
    "pack" {
        Pack $config
    }
    "rebuild" {
        Rebuild $config $examples $srcdist
    }
    "clean" {
        Clean $config
    }
    "test" {
       Test $config $coverage
    }
    "doc" {
        Doc
    }
    default {
        Write-Error "Invalid action value" $action
        Get-Help
        exit 1
    }
}
exit 0

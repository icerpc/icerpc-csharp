# Copyright (c) ZeroC, Inc. All rights reserved.

param (
    $action="build",
    $config="debug",
    [switch]$examples,
    [switch]$srcdist,
    [switch]$coverage,
    [switch]$help
)

function BuildCompiler($config) {
    Push-Location "tools\slicec-cs"
    $arguments = @('build')
    if ($config -eq 'release') {
        $arguments += '--release'
    }
    RunCommand 'cargo' $arguments
    Pop-Location
}

function CleanCompiler($config) {
    Push-Location "tools\slicec-cs"
    RunCommand "cargo" "clean"
    Pop-Location
}

function BuildIceRpc($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet" @('build', '--configuration', $dotnetConfiguration)
}

function BuildIceRpcExamples($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet" @('build', '--configuration', $dotnetConfiguration, "examples\Minimal\Minimal.sln")
    RunCommand "dotnet" @('build', '--configuration', $dotnetConfiguration, "examples\Hello\Hello.sln")
}

function CleanIceRpc($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet" @('clean', '--configuration', $dotnetConfiguration)
}

function CleanIceRpcExamples($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet" @('clean', '--configuration', $dotnetConfiguration, "examples\Minimal\Minimal.sln")
    RunCommand "dotnet" @('clean', '--configuration', $dotnetConfiguration, 'examples\Hello\Hello.sln')
}

function Build($config, $examples, $srcdist) {
    if ($examples) {
        if ($srcdist) {
           BuildCompiler $config
           Pack $config
           $global_packages = dotnet nuget locals -l global-packages
           $global_packages = $global_packages.replace("global-packages: ", "")
           Remove-Item $global_packages"\icerpc" -Recurse -Force -ErrorAction Ignore
           Remove-Item $global_packages"\icerpc.interop" -Recurse -Force -ErrorAction Ignore
           RunCommand "dotnet" @('nuget', 'push', 'lib\*.nupkg', '--source', $global_packages)
        }
        BuildIceRpcExamples $config
    } else {
        BuildCompiler $config
        BuildIceRpc $config
    }
}

function Pack($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet"  @('pack', '--configuration', $dotnetConfiguration)
}

function Rebuild($config, $examples, $srcdist) {
    Clean $config $examples
    Build $config $examples $srcdist
}

function Clean($config, $examples) {
    CleanCompiler($config)
    CleanIceRpc($config)
    if ($examples)
    {
        CleanIceRpcExamples($config)
    }
}

function Test($config, $coverage) {
    $dotnetConfiguration = DotnetConfiguration($config)
    $arguments = @('test', '--no-build', '--configuration', $dotnetConfiguration)
    if ($coverage) {
       $arguments += @('--collect:"XPlat Code Coverage"')
    }
    RunCommand "dotnet" $arguments
    if ($coverage) {
        $arguments = @('-reports:tests/*/TestResults/*/coverage.cobertura.xml', '-targetdir:tests/CodeCoverageRerport')
        RunCommand "reportgenerator" $arguments
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

function DotnetConfiguration($config) {
    if ($config -eq 'release') {
        'Release'
    } else {
        'Debug'
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
        Clean $config $examples
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

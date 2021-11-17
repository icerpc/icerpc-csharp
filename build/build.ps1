# Copyright (c) ZeroC, Inc. All rights reserved.

param (
    $action="build",
    $config="debug",
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
    } elseif ($config -eq 'debug') {
        $args += @('--configuration', 'Debug')
    } else {
        Write-Error "unknown configuration"
        exit 1
    }
    RunCommand "dotnet" $args
}

function Clean-IceRpc($config) {
    RunCommand "dotnet" "clean"
}

function Build($config) {
    Build-Compiler $config
    Build-IceRpc $config
}

function Rebuild($config) {
    Clean $config
    Build $config
}

function Clean($config) {
    Clean-Compiler($config)
    Clean-IceRpc($config)
}

function Test($config) {
    $args = @('test', '--no-build')
    if ($config -eq 'release') {
        $args += @('--configuration', 'Release')
    } elseif ($config -eq 'debug') {
        $args += @('--configuration', 'Debug')
    }
    RunCommand "dotnet" $args
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
    Write-Host "  clean                     Clean IceRpc sources & slice-cs compiler."
    Write-Host "  rebuild                   Rebuild IceRpc sources & slice-cs compiler."
    Write-Host "  test                      Runs tests."
    Write-Host "  doc                       Generate documentation"
    Write-Host "Arguments:"
    Write-Host "  -config                   Build configuration: debug or release, the default is debug."
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
        Build $config
    }
    "rebuild" {
        Rebuild $config
    }
    "clean" {
        Clean $config
    }
    "test" {
       Test $config
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

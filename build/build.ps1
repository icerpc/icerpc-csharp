# Copyright (c) ZeroC, Inc.

param (
    $action="build",
    $config="debug",
    [switch]$examples,
    [switch]$srcdist,
    [switch]$coverage,
    [switch]$help
)

$exampleProjects = $packages = Get-Childitem -Path "examples" -Include *.sln -Recurse

Get-Content .\build\IceRpc.Version.props -Raw | Where {$_ -match "<IceRpcVersion .*>(.*)</IceRpcVersion>"} | Out-Null
$version = $Matches.1

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

function BuildIceRpcSliceTools($config) {
    Push-Location "tools\IceRpc.Slice.Tools"
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet" @('build', '-nr:false', '--configuration', $dotnetConfiguration)
    Pop-Location
}

function CleanIceRpcSliceTools($config) {
    Push-Location "tools\IceRpc.Slice.Tools"
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet" @('clean', '-nr:false', '--configuration', $dotnetConfiguration)
    Pop-Location
}

function BuildIceRpc($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet" @('build', '-nr:false', '--configuration', $dotnetConfiguration)
}

function CleanIceRpcProjectTemplates($config) {
    Push-Location "src\IceRpc.ProjectTemplates"
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet" @('clean', '--configuration', $dotnetConfiguration)
    Pop-Location
}

function BuildIceRpcExamples($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    foreach ($example in $exampleProjects)
    {
        RunCommand "dotnet" @('build', '-nr:false', '--configuration', $dotnetConfiguration, "$example")
    }
}

function CleanIceRpc($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    RunCommand "dotnet" @('clean', '-nr:false', '--configuration', $dotnetConfiguration)
}

function CleanIceRpcExamples($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    foreach ($example in $exampleProjects)
    {
        RunCommand "dotnet" @('clean', '-nr:false', '--configuration', $dotnetConfiguration, "$example")
    }
}

function Build($config, $examples, $srcdist) {
    if ($examples) {
        if ($srcdist) {
           Push $config
        }
        BuildIceRpcExamples $config
    } else {
        BuildCompiler $config
        BuildIceRpcSliceTools $config
        BuildIceRpc $config
    }
}

function Push($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    BuildCompiler $config
    Pack $config
    $global_packages = dotnet nuget locals -l global-packages
    $global_packages = $global_packages.replace("global-packages: ", "")
    Remove-Item $global_packages"\IceRpc.Slice.Tools\$version" -Recurse -Force -ErrorAction Ignore
    $packages = Get-Childitem -Path "." -Include *.nupkg -Recurse
    foreach ($package in $packages)
    {
        $package_name = (Get-Item $package).Basename
        $package_name = $package_name.Substring(0, $package_name.Length - ".$version".Length)
        Remove-Item $global_packages"\$package_name\$version" -Recurse -Force -ErrorAction Ignore
    }
    RunCommand "dotnet" @('nuget', 'push', "tools\**\$dotnetConfiguration\*.$version.nupkg", '--source', $global_packages)
    RunCommand "dotnet" @('nuget', 'push', "src\**\$dotnetConfiguration\*.$version.nupkg", '--source', $global_packages)
}

function InstallTemplates($config) {
    $dotnet_templates = dotnet new -l
    if ($dotnet_templates.Where({$_.Contains("icerpc-client")}).count -gt 0) {
        RunCommand "dotnet" @('new', 'uninstall', 'IceRpc.ProjectTemplates')
    }

    $dotnetConfiguration = DotnetConfiguration($config)
    Push-Location "src\IceRpc.ProjectTemplates"
    RunCommand "dotnet" @('new', 'install', "bin\Any CPU\$dotnetConfiguration\IceRpc.ProjectTemplates.$version.nupkg")
    Pop-Location
}

function Pack($config) {
    $dotnetConfiguration = DotnetConfiguration($config)
    Push-Location "tools\IceRpc.Slice.Tools"
    RunCommand "dotnet"  @('pack', '--configuration', $dotnetConfiguration)
    Pop-Location
    RunCommand "dotnet"  @('pack', '-nr:false', '--configuration', $dotnetConfiguration)
    Push-Location "src\IceRpc.ProjectTemplates"
    RunCommand "dotnet" @('build', '-nr:false', '--configuration', $dotnetConfiguration)
    Pop-Location
}

function Rebuild($config, $examples, $srcdist) {
    Clean $config $examples
    Build $config $examples $srcdist
}

function Clean($config, $examples) {
    CleanCompiler($config)
    CleanIceRpc($config)
    CleanIceRpcProjectTemplates($config)
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
        $arguments = @('-reports:tests/*/TestResults/*/coverage.cobertura.xml', '-targetdir:tests/CodeCoverageReport')
        RunCommand "reportgenerator" $arguments
    }
}

function Doc() {
    $dotnetConfiguration = DotnetConfiguration($config)
    Push-Location "docfx"
    RunCommand "docfx" @('--property', "Configuration=$dotnetConfiguration")
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
    Write-Host "  build                     Build the IceRPC assemblies and the slicec-cs compiler."
    Write-Host "  pack                      Create the IceRPC NuGet packages."
    Write-Host "  push                      Push the IceRPC NuGet packages to the global-packages source."
    Write-Host "  install-templates         Install the IceRPC dotnet new project templates."
    Write-Host "  clean                     Clean build artifacts."
    Write-Host "  rebuild                   Rebuild."
    Write-Host "  test                      Runs tests."
    Write-Host "  doc                       Generate the C# API documentation"
    Write-Host "                            Requires docfx from https://github.com/dotnet/docfx"
    Write-Host "Arguments:"
    Write-Host "  -config                   Build configuration: debug or release, the default is debug."
    Write-Host "  -examples                 Build examples solutions instead of the source solutions."
    Write-Host "  -srcdist                  Use IceRPC NuGet packages from this source distribution when building the examples."
    Write-Host "                            The NuGet packages are pushed to the local global-packages source."
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
    "push" {
        Push $config
    }
    "install-templates" {
        InstallTemplates $config
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

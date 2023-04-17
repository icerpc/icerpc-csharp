#!/usr/bin/env bash

set -ue

# Read version from icerpc.version.props
version=$(cat build/IceRpc.Version.props | grep IceRpcVersion | sed -E "s/<IceRpcVersion .*>(.*)<\/IceRpcVersion>/\1/g" | sed -e 's/^[[:space:]]*//')

usage()
{
    echo "Usage: build [command] [arguments]"
    echo "Commands (defaults to build):"
    echo "  build                     Build the IceRPC assemblies and the slicec-cs compiler."
    echo "  pack                      Create the IceRPC NuGet packages."
    echo "  push                      Push the IceRPC NuGet packages to the global-packages source."
    echo "  install-templates         Install the IceRPC project templates."
    echo "  clean                     Clean build artifacts."
    echo "  rebuild                   Rebuild."
    echo "  test                      Runs tests."
    echo "  doc                       Generate the C# API documentation."
    echo "                            Requires docfx from https:/github.com/dotnet/docfx."
    echo "Arguments:"
    echo "  --config | -c             Build configuration: debug or release, the default is debug."
    echo "  --examples                Build examples solutions instead of the source solutions."
    echo "  --srcdist                 Use IceRPC NuGet packages from this source distribution when building the examples."
    echo "                            The NuGet packages are pushed to the local global-packages source."
    echo "  --coverage                Collect code coverage from test runs."
    echo "                            Requires reportgeneratool from https://github.com/danielpalme/ReportGenerator."
    echo "  --help   | -h             Print help and exit."
}

build_compiler()
{
    arguments=("build")
    if [ "$config" == "release" ]; then
        arguments+=("--release")
    fi
    pushd tools/slicec-cs
    run_command cargo "${arguments[@]}"
    popd
}

clean_compiler()
{
    pushd tools/slicec-cs
    run_command cargo clean
    popd
}

build_icerpc_slice_tools()
{
    pushd tools/IceRpc.Slice.Tools
    run_command dotnet "build" "-nr:false" "-c" "$dotnet_config"
    popd
}

clean_icerpc_slice_tools()
{
    pushd tools/IceRpc.Slice.Tools
    run_command dotnet "clean" "-nr:false" "-c" "$dotnet_config"
    popd
}

build_icerpc()
{
    run_command dotnet "build" "-nr:false" "-c" "$dotnet_config"
}

clean_icerpc()
{
    run_command dotnet "clean" "-nr:false"
}

clean_icerpc_project_templates()
{
    pushd src/IceRpc.ProjectTemplates
    run_command dotnet "clean" "-nr:false"
    popd
}

pack()
{
    pushd tools/IceRpc.Slice.Tools
    run_command dotnet "pack" "-nr:false" "-c" "$dotnet_config"
    popd
    run_command dotnet "pack" "-nr:false" "-c" "$dotnet_config"
    pushd src/IceRpc.ProjectTemplates
    run_command dotnet "pack" "-nr:false" "-c" "$dotnet_config"
    popd
}

push()
{
    build_compiler
    pack
    global_packages=$(dotnet nuget locals -l global-packages)
    global_packages=${global_packages/global-packages: /""}
    run_command rm "-rf" "$global_packages/icerpc/$version" "$global_packages"/icerpc.*/"$version"
    run_command dotnet "nuget" "push" "tools/**/$dotnet_config/*.$version.nupkg" "--source" "$global_packages"
    run_command dotnet "nuget" "push" "src/**/$dotnet_config/*.$version.nupkg" "--source" "$global_packages"
}

install_templates()
{
    dotnet_templates=$(dotnet new -l)
    if [[ "$dotnet_templates" == *"icerpc-client"* ]]; then
        run_command "dotnet" 'new' 'uninstall' 'IceRpc.ProjectTemplates'
    fi

    pushd src/IceRpc.ProjectTemplates
    run_command dotnet "pack" "-c" "$dotnet_config"
    run_command "dotnet" 'new' 'install' "bin/$dotnet_config/IceRpc.ProjectTemplates.$version.nupkg"
    popd
}

build()
{
    if [ "$examples" == "no" ]; then
        build_compiler
        build_icerpc_slice_tools
        build_icerpc
    else
        if [ "$srcdist" == "yes" ]; then
            push
        fi
        for solution in examples/*/*.sln examples/*/*/*.sln
        do
            run_command dotnet "build" "-nr:false" "-c" "$dotnet_config" "$solution"
        done
    fi
}

rebuild()
{
    clean
    build
}

clean()
{
    clean_compiler
    clean_icerpc_slice_tools
    clean_icerpc
    clean_icerpc_project_templates
}

run_test()
{
    arguments=("test" "--no-build" "-c" "$dotnet_config")
    if [ "$coverage" == "yes" ]; then
        runsettings=$(realpath 'build/Coverlet.runsettings')
        arguments+=("/p:RunSettingsFilePath=$runsettings" "--collect:\"XPlat Code Coverage\"")
    fi
    run_command dotnet "${arguments[@]}"

    if [ "$coverage" == "yes" ]; then
        arguments=("-reports:tests/*/TestResults/*/coverage.cobertura.xml" "-targetdir:tests/CodeCoverageReport")
        run_command reportgenerator "${arguments[@]}"
    fi
}

doc()
{
    pushd docfx
    run_command docfx "--property" "Configuration=$dotnet_config"
    popd
}

run_command()
{
    echo "$@"
    "$@"
    exit_code=$?
    if [ $exit_code -ne 0 ]; then
        echo "Error $exit_code"
        exit $exit_code
    fi
}

action=""
config=""
coverage="no"
examples="no"
srcdist="no"
while [[ $# -gt 0 ]]; do
    key="$1"
    case $key in
        -h|--help)
            usage
            exit 0
            ;;
        -c|--config)
            config=$2
            shift
            shift
            ;;
        --examples)
            examples="yes"
            shift
            ;;
        --srcdist)
            srcdist="yes"
            shift
            ;;
        --coverage)
            coverage="yes"
            shift
            ;;
        *)
            if [ -z "$action" ]
            then
                action=$1
            else
                echo "too many arguments " "$1"
                usage
                exit 1
            fi
            shift
            ;;
    esac
done

if [ -z "$action" ]
then
    action="build"
fi

if [ -z "$config" ]
then
    config="debug"
fi

actions=("build" "clean" "pack" "push" "install-templates" "rebuild" "test" "doc")
if [[ ! " ${actions[*]} " == *" ${action} "* ]]; then
    echo "invalid action: " $action
    usage
    exit 1
fi

configs=("debug" "release")
if [[ ! " ${configs[*]} " == *" ${config} "* ]]; then
    echo "invalid config: " $config
    usage
    exit 1
fi

if [ "$config" == "release" ]; then
    dotnet_config="Release"
else
    dotnet_config="Debug"
fi

case $action in
    "build")
        build
        ;;
    "rebuild")
        rebuild
        ;;
    "pack")
        pack
        ;;
    "push")
        push
        ;;
    "install-templates")
        install_templates
        ;;
    "clean")
        clean
        ;;
    "test")
        run_test
        ;;
    "doc")
        doc
        ;;
esac

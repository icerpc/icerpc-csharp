#!/usr/bin/env bash

set -ue

version="0.1.0-preview1"

usage()
{
    echo "Usage: build [command] [arguments]"
    echo "Commands (defaults to build):"
    echo "  build                     Build IceRpc sources & slice-cs compiler."
    echo "  pack                      Build the IceRpc NuGet packages."
    echo "  install                   Install the IceRpc NuGet packages into the global-packages source."
    echo "  clean                     Clean IceRpc sources & slice-cs compiler."
    echo "  rebuild                   Rebuild IceRpc sources & slice-cs compiler."
    echo "  test                      Runs tests."
    echo "  doc                       Generate documentation"
    echo "Arguments:"
    echo "  --config | -c             Build configuration: debug or release, the default is debug."
    echo "  --examples                Build examples solutions instead of the source solutions."
    echo "  --srcdist                 Use NuGet packages from this source distribution when building examples."
    echo "                            The NuGet packages are installed to the local global-packages source."
    echo "  --coverage                Collect code coverage from test runs."
    echo "                            Requires reportgeneratool from https://github.com/danielpalme/ReportGenerator"
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

build_icerpc()
{
    run_command dotnet "build" "-c" "$dotnet_config"
}

pack()
{
    run_command dotnet "pack" "-c" "$dotnet_config"
}

install()
{
    build_compiler
    pack
    global_packages=$(dotnet nuget locals -l global-packages)
    global_packages=${global_packages/global-packages: /""}
    run_command rm "-rf" "$global_packages/icerpc/$version" "$global_packages/icerpc.coloc/$version" "$global_packages/icerpc.interop/$version"
    run_command dotnet "nuget" "push" "src/**/$dotnet_config/*.nupkg" "--source" "$global_packages"
}

clean_icerpc()
{
    run_command dotnet clean
}

build()
{
    if [ "$examples" == "no" ]; then
        build_compiler
        build_icerpc
    else
        if [ "$srcdist" == "yes" ]; then
            install
        fi
        for solution in examples/*/*.sln
        do
            run_command dotnet "build" "-c" "$dotnet_config" "$solution"
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
    clean_icerpc
}

run_test()
{
    arguments=()
    if [ "$coverage" == "yes" ]; then
        arguments+=("--collect:\"XPlat Code Coverage\"")
    fi
    run_command dotnet "test" "--no-build" "-c" "$dotnet_config" "${arguments[@]}"

    if [ "$coverage" == "yes" ]; then
        arguments=("-reports:tests/*/TestResults/*/coverage.cobertura.xml" "-targetdir:tests/CodeCoverageRerport")
        run_command reportgenerator "${arguments[@]}"
    fi
}

doc()
{
    pushd doc
    run_command docfx
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

actions=("build" "clean" "pack" "install" "rebuild" "test" "doc")
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
    "install")
        install
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

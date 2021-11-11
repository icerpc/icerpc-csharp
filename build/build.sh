#!/usr/bin/env bash

set -ue

usage()
{
    echo "Usage: build [command] [arguments]"
    echo "Commands (defaults to build):"
    echo "  build                     Build IceRpc sources & slice-cs compiler."
    echo "  clean                     Clean IceRpc sources & slice-cs compiler."
    echo "  rebuild                   Rebuild IceRpc sources & slice-cs compiler."
    echo "  test                      Runs tests."
    echo "  doc                       Generate documentation"
    echo "Arguments:"
    echo "  --config | -c             Build configuration: debug or release, the default is debug."
    echo "  --help   | -h             Print help and exit."
}


build_compiler()
{
    arguments=("build")
    if [ $1 == "release" ]; then
        arguments+=("--release")
    fi
    pushd tools/slicec-cs
    run_command cargo ${arguments[@]}
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
    arguments=("build")
    if [ $1 == "release" ]; then
        arguments+=("--configuration" "Release")
    else
        arguments+=("--configuration" "Debug")
    fi
    run_command dotnet ${arguments[@]}
}

clean_icerpc()
{
    run_command dotnet clean
}

build()
{
    build_compiler $1
    build_icerpc $1
}

rebuild()
{
    clean
    build $1
}

clean()
{
    clean_compiler
    clean_icerpc
}

run_test()
{
    arguments=("test")
    if [ $1 == "release" ]; then
        arguments+=("--configuration" "Release")
    else
        arguments+=("--configuration" "Debug")
    fi
    run_command dotnet ${arguments[@]}
}

doc()
{
    pushd doc
    run_command docfx
    popd
}

run_command()
{
    echo $@
    $@
    exit_code=$?
    if [ $exit_code -ne 0 ]; then
        echo "Error $exit_code"
        exit $exit_code
    fi
}

action=""
config=""
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
        *)
            if [ -z "$action" ]
            then
                action=$1
            else
                echo "too many argument " $1
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

actions=("build" "clean" "rebuild" "test" "doc")
if [[ ! " ${actions[*]} " =~ " ${action} " ]]; then
    echo "invalid action: " $action
    usage
    exit 1
fi

configs=("debug" "release")
if [[ ! " ${configs[*]} " =~ " ${config} " ]]; then
    echo "invalid config: " $config
    usage
    exit 1
fi

case $action in
    "build")
        build $config
        ;;
    "rebuild")
        rebuild $config
        ;;
    "clean")
        clean $config
        ;;
    "test")
        run_test $config
        ;;
    "doc")
        doc
        ;;
esac


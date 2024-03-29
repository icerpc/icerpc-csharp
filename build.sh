#!/usr/bin/env bash

set -ue

# Everything in this script is relative to the directory containing this script.
# This works most of the time but is not perfect. It will fail if the script is sourced or if the script is
# executed from a symlink.
cd "$(dirname "${BASH_SOURCE[0]}")"

# Read version from icerpc.version.props
version=$(cat build/IceRpc.Version.props | grep "<Version" | sed -E "s/<Version .*>(.*)<\/Version>/\1/g" | sed -e 's/^[[:space:]]*//')

usage()
{
    echo "Usage: ./build.sh [actions] [arguments]"
    echo ""
    echo "Actions (defaults to --build):"
    echo "  --build                Build the IceRPC assemblies and the slicec-cs compiler."
    echo "  --publish              Creates and publishes the IceRPC NuGet packages to the local global-packages source."
    echo "  --clean                Clean all build artifacts."
    echo "  --coverage             Generate code coverage report from the tests runs."
    echo "                         Requires reportgenerator command from https://github.com/danielpalme/ReportGenerator"
    echo "  --doc                  Generate the C# API documentation"
    echo "                         Requires docfx from https://github.com/dotnet/docfx"
    echo ""
    echo "Arguments:"
    echo "  --config               Build configuration: debug or release, the default is debug."
    echo "  --version              The version override for the IceRPC NuGet packages. The default version is the version"
    echo "                         specified in the build/IceRpc.Version.props file."
    echo "  --help                 Print help and exit."
}

build()
{
    arguments=("build")
    if [ "$config" == "release" ]; then
        arguments+=("--release")
    fi
    pushd tools/slicec-cs
    run_command cargo "${arguments[@]}"
    popd

    pushd tools
    run_command dotnet "build" "-nr:false"$version_property "-c" "$dotnet_config"
    popd

    run_command dotnet "build" "-nr:false"$version_property "-c" "$dotnet_config"
}

clean()
{
    pushd tools/slicec-cs
    run_command cargo clean
    popd

    pushd tools
    run_command dotnet "clean" "-nr:false"$version_property "-c" "$dotnet_config"
    popd

    run_command dotnet "clean" "-nr:false"$version_property

    pushd src/IceRpc.Templates
    run_command dotnet "clean"$version_property "-nr:false"
    popd
}

doc()
{
    pushd docfx
    run_command docfx "metadata" "--property" "Configuration=$dotnet_config"
    run_command docfx "build"
    popd
}

publish()
{
    build

    pushd tools
    run_command dotnet "pack" "-nr:false"$version_property "-c" "$dotnet_config"
    popd

    run_command dotnet "pack" "-nr:false"$version_property "-c" "$dotnet_config"

    pushd src/IceRpc.Templates
    run_command dotnet "pack" "-nr:false"$version_property "-c" "$dotnet_config"
    popd

    global_packages=$(dotnet nuget locals -l global-packages)
    global_packages=${global_packages/global-packages: /""}
    run_command rm "-rf" "$global_packages/zeroc.slice/$version" "$global_packages/icerpc/$version" "$global_packages"/icerpc.*/"$version"
    run_command dotnet "nuget" "push" "tools/**/$dotnet_config/*.$version.nupkg" "--source" "$global_packages"
    run_command dotnet "nuget" "push" "src/**/$dotnet_config/*.$version.nupkg" "--source" "$global_packages"
}

code_coverage()
{
    runsettings=${PWD}/build/Coverlet.runsettings
    arguments=("test" "-c" "$dotnet_config" "-p:RunSettingsFilePath=$runsettings" "--collect:\"XPlat Code Coverage\"")
    run_command dotnet "${arguments[@]}"

    arguments=("-reports:tests/*/TestResults/*/coverage.cobertura.xml" "-targetdir:tests/CodeCoverageReport")
    if [ -n "${REPORTGENERATOR_LICENSE:-}" ]; then
        arguments+=("-license:${REPORTGENERATOR_LICENSE}")
    fi

    run_command reportgenerator "${arguments[@]}"
    # Remove code coverage results after the report has been generated.
    find "tests" -type d -name "TestResults" -prune -exec rm -rf {} \;
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

config=""
version_property=""
passedInActions=()
actions=("--build" "--clean" "--doc" "--coverage" "--publish")
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
        --version)
            version=$2
            version_property=" -p:Version=$version"
            shift
            shift
            ;;
        *)
            found=0
            for action in "${actions[@]}"; do
                if [ "$action" == "$1" ]; then
                    passedInActions+=("$action")
                    found=1
                    break
                fi
            done

            if [ $found -eq 0 ]; then
                echo "Unknown argument: " "$1"
                echo ""
                usage
                exit 1
            fi
            shift
            ;;
    esac
done

if [ -z "$config" ]; then
    config="debug"
fi

configs=("debug" "release")
if [[ ! " ${configs[*]} " == *" ${config} "* ]]; then
    echo "Invalid config: '" $config "', config must 'debug' or 'release'"
    echo ""
    usage
    exit 1
fi

if [ "$config" == "release" ]; then
    dotnet_config="Release"
else
    dotnet_config="Debug"
fi

if [ "${#passedInActions[@]}" -eq 0 ]; then
    passedInActions=("--build")
fi

for action in "${passedInActions[@]}"; do
    case $action in
        "--build")
            build
            ;;
        "--publish")
            publish
            ;;
        "--clean")
            clean
            ;;
        "--coverage")
            code_coverage
            ;;
        "--doc")
            doc
            ;;
    esac
done

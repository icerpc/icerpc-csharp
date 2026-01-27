#!/usr/bin/env bash

set -ue

# Everything in this script is relative to the directory containing this script.
# This works most of the time but is not perfect. It will fail if the script is sourced or if the script is
# executed from a symlink.
cd "$(dirname "${BASH_SOURCE[0]}")"

dotnet IceRpc.ProtocGen.BuildTelemetry.dll -- "$@"

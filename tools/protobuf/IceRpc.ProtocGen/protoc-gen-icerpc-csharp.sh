#!/usr/bin/env sh

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" >/dev/null 2>&1 && pwd )"
dotnet $DIR/IceRpc.ProtocGen.dll -- "$@"

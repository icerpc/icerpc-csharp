#!/usr/bin/env python3
# Copyright (c) ZeroC, Inc.

"""Compiles the Slice-Compiler definition files, and writes the generated code to the correct output directory.

This script relies on the downloaded `slicec` to function. The easiest way to make sure this is present is to run
`dotnet build` before running this script.

Usage:
    python build/compiler-slice.py
"""

import os
import platform
import subprocess


# If this is set to 'True' the script will print extra information to the terminal while it's running.
DEBUGGING = False

# Determine if we're running on a windows platform.
IS_WINDOWS = os.name == "nt"


def runCommand(args) -> tuple[str, str]:
    result = subprocess.run(args, check=False, shell=IS_WINDOWS, capture_output=True)
    return [result.stdout.decode("utf-8").strip(), result.stderr.decode("utf-8").strip()]


def get_slicec_name() -> tuple[str, str]:
    # Lookup the name of the system we're running on.
    system = platform.system().lower()
    if DEBUGGING:
        print("'get_slicec_name' found the following system name: '" + system + "'")
    # Convert it to the name we use in 'IceRpc.Slice.Tools.csproj'.
    match system:
        case "linux":
            system = "linux"
        case "windows":
            system = "windows"
        case "darwin":
            system = "macos"
        case _:
            raise RuntimeError("Unsupported platform detected: '" + system + "'")

    # Lookup the architecture of the system we're running on.
    architecture = platform.machine().lower()
    if DEBUGGING:
        print("'get_slicec_name' found the following system architecture: '" + architecture + "'")
    # Convert it to the name we use in 'IceRpc.Slice.Tools.csproj'.
    match architecture:
        case "x86" | "x86_64" | "amd64":
            architecture = "x64"
        case "arm64" | "aarch64":
            architecture = "arm64"
        case _:
            raise RuntimeError("Unsupported architecture detected: '" + architecture + "'")
    
    # Determine which file extension to use.
    extension = ".exe" if IS_WINDOWS else ""
    if DEBUGGING:
        print("'get_slicec_name' has decided to use this extension: '" + extension + "'")

    # Return the name of the executable, and what folder we expected it to be in.
    folder = system + "-" + architecture
    file = "slicec" + extension
    return [folder, file]


def find_slicec(repo_root: str) -> str:
    # Determine what is the exact name of the slicec we want to run is.
    [slicec_dir, slicec_file] = get_slicec_name()
    if DEBUGGING:
        print("'find_slicec' determined this is the directory where slicec should be: '" + slicec_dir + "'")
        print("'find_slicec' determined this is the filename slicec should be using: '" + slicec_file + "'")

    # Check for 'slicec' in the 'obj' directory for 'IceRpc.Slice.Tools'.
    candidate = os.path.join(repo_root, "src", "IceRpc.Slice.Tools", "obj", "tools", slicec_dir, slicec_file)
    if os.path.isfile(candidate):
        return candidate
    else:
        raise RuntimeError("Failed to locate valid slicec")


def find_code_generator(repo_root: str) -> list[str]:
    # Check for the code generator in the 'bin' directory for 'ZeroC.Slice.Generator'.
    bin_root = os.path.join(repo_root, "src", "ZeroC.Slice.Generator", "bin")
    if not os.path.isdir(bin_root):
        raise RuntimeError("'src/ZeroC.Slice.Generator/bin' is not a directory or does not exist")
    bin_subdir = os.path.join(bin_root, os.listdir(bin_root)[0])
    code_gen_dir = os.path.join(bin_subdir, os.listdir(bin_subdir)[0])

    # Determine which extension the code-generator script will have.
    code_gen_extension = ".bat" if IS_WINDOWS else ".sh"

    # Check for the actual code generator script file.
    code_gen_script = os.path.join(code_gen_dir, "slicec-csharp-generator" + code_gen_extension)
    if not os.path.isfile(code_gen_script):
        raise RuntimeError("Failed to locate code-generator at expected path: '" + code_gen_script + "'")
    else:
        return code_gen_script


def find_compiler_slice_files(repo_root: str) -> list[str]:
    # Get the directory where the compiler-definition Slice files should be.
    definition_dir = os.path.join(repo_root, "slice", "Compiler")
    if not os.path.isdir(definition_dir):
        raise RuntimeError("'slice/Compiler' is not a directory or does not exist")
    
    # Return any files in this directory.
    return [os.path.join(definition_dir, f) for f in os.listdir(definition_dir)]


def find_output_directory(repo_root: str) -> str:
    output_dir = os.path.join(repo_root, "src", "ZeroC.Slice.Symbols", "Compiler")
    if not os.path.isdir(output_dir):
        raise RuntimeError("'src/ZeroC.Slice.Symbols/Compiler' is not a directory or does not exist")
    
    # Return the output directory.
    return output_dir
    

def main() -> None:
    # Find the root of the repository we're in
    [repo_root, err] = runCommand(["git", "rev-parse", "--show-toplevel"])
    if err != "":
        raise RuntimeError("Error encountered while trying to find the repository root:\n" + err)
    if DEBUGGING:
        print("'find_slicec' determined this is the repository root: '" + repo_root + "'")

    # Find a valid 'slicec' executable.
    slicec_executable = find_slicec(repo_root)
    # Find the Slice code-generator.
    code_generator = find_code_generator(repo_root)
    # Find the output directory.
    output_dir = find_output_directory(repo_root)
    # Find the Slice compiler definition files.
    slice_files = find_compiler_slice_files(repo_root)

    # Inform the user what is being used for this compilation run.
    print("Found valid 'slicec' at: '" + slicec_executable + "'")
    print("Found valid code-generator at: '" + code_generator + "'")
    print("Found valid output directory at: '" + output_dir + "'")
    print("Compiling the following files: [\n    " + '\n    '.join(slice_files) + "\n]")

    # Actually run the compilation.
    command = [slicec_executable, "-G", code_generator, "--output-dir", output_dir]
    command.extend(slice_files)
    if DEBUGGING:
        print("Attempting to run this command: [" + ','.join(command) + "]")

    # Actually run the Slice compiler and remit any errors.
    [result, err] = runCommand(command)
    print(result)
    if err != "":
        print("\nErrors encountered while trying to compile Slice files:\n\n" + err)
        exit(79)
    print("Completed")


if __name__ == "__main__":
    main()

// Copyright (c) ZeroC, Inc.

use super::attribute_patcher::patch_attributes;
use super::generated_code::GeneratedCode;
use super::validators::cs_validator::validate_cs_attributes;
use super::visitors::{
    ClassVisitor, DispatchVisitor, EnumVisitor, ExceptionVisitor, ModuleVisitor, ProxyVisitor, StructVisitor,
};
use slice::code_block::CodeBlock;
use slice::command_line::SliceOptions;
use slice::compilation_result::{CompilationData, CompilationResult};
use slice::diagnostics::{Diagnostic, Error};
use slice::slice_file::SliceFile;
use std::io;

// Compile the Slice into a CompilationResult, then run the compiler chain.
pub fn compile(slice_options: &SliceOptions) -> CompilationResult {
    slice::compile_from_options(slice_options).and_then(compiler_chain)
}

// The compiler chain is a series of steps that are run on the compilation data.
pub fn compiler_chain(compilation_data: CompilationData) -> CompilationResult {
    unsafe { patch_attributes(compilation_data) }
        .and_then(validate_cs_attributes)
        .and_then(check_for_unique_names)
}

fn check_for_unique_names(mut compilation_data: CompilationData) -> CompilationResult {
    let mut file_map = std::collections::HashMap::new();

    for slice_file in compilation_data.files.values().filter(|file| file.is_source) {
        if let Some(old_path) = file_map.insert(&slice_file.filename, &slice_file.relative_path) {
            Diagnostic::new(Error::IO {
                action: "generate code for",
                path: slice_file.relative_path.clone(),
                error: io::ErrorKind::InvalidInput.into(),
            })
            .add_note("Multiple source files cannot have the same filename because the generated files are written to a common directory.", None)
            .add_note(
                format!("Other file is '{old_path}'."),
                None,
            )
            .report(&mut compilation_data.error_reporter)
        }
    }

    compilation_data.into()
}

// Generate the code for a single Slice file.
pub fn generate_code(slice_file: &SliceFile) -> String {
    let mut generated_code = GeneratedCode::new();

    generated_code.preamble.push(preamble(slice_file));

    let mut struct_visitor = StructVisitor {
        generated_code: &mut generated_code,
    };
    slice_file.visit_with(&mut struct_visitor);

    let mut proxy_visitor = ProxyVisitor {
        generated_code: &mut generated_code,
    };
    slice_file.visit_with(&mut proxy_visitor);

    let mut dispatch_visitor = DispatchVisitor {
        generated_code: &mut generated_code,
    };
    slice_file.visit_with(&mut dispatch_visitor);

    let mut exception_visitor = ExceptionVisitor {
        generated_code: &mut generated_code,
    };
    slice_file.visit_with(&mut exception_visitor);

    let mut enum_visitor = EnumVisitor {
        generated_code: &mut generated_code,
    };
    slice_file.visit_with(&mut enum_visitor);

    let mut class_visitor = ClassVisitor {
        generated_code: &mut generated_code,
    };
    slice_file.visit_with(&mut class_visitor);

    let mut module_visitor = ModuleVisitor {
        generated_code: &mut generated_code,
    };
    slice_file.visit_with(&mut module_visitor);

    // Move the generated code out of the generated_code struct and consolidate into a
    // single string.
    generated_code
        .preamble
        .into_iter()
        .chain(generated_code.code_blocks.into_iter())
        .collect::<CodeBlock>()
        .to_string()
        + "\n" // End the file with a trailing newline.
}

fn preamble(slice_file: &SliceFile) -> CodeBlock {
    format!(
        r#"// Copyright (c) ZeroC, Inc.

// <auto-generated/>
// slicec-cs version: '{version}'
// Generated from file: '{file}.slice'

#nullable enable

#pragma warning disable 1591 // Missing XML Comment
#pragma warning disable 1573 // Parameter has no matching param tag in the XML comment

using IceRpc.Slice;

[assembly:Slice("{file}.slice")]"#,
        version = env!("CARGO_PKG_VERSION"),
        file = slice_file.filename,
    )
    .into()
}

#[cfg(test)]
mod test {
    use super::{compile, generate_code};
    use slice::command_line::SliceOptions;
    use slice::compilation_result::CompilationData;
    use slice::diagnostics::{Diagnostic, DiagnosticReporter, Error};
    use slice::test_helpers::{check_diagnostics, diagnostics_from_compilation_data};
    use slice::utils::file_util::resolve_files_from;
    use std::io;
    use std::path::Path;

    // This test compiles all the Slice files in the tests directory (recursive).
    // Since this should exercise all the code generation, we can use this to
    // generate code coverage data.
    #[test]
    fn compile_all_test_slice() {
        let root_dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("..").join("..");
        let tests_dir = root_dir.join("tests").join("IceRpc.Tests").display().to_string();
        let slice_dir = root_dir.join("slice").display().to_string();

        // Use `resolve_files_from` to find all Slice files in the tests directory.
        let test_slice_files = {
            let mut options = SliceOptions::default();
            options.references.push(tests_dir.clone());
            let mut diagnostic_reporter = DiagnosticReporter::new(&options);
            resolve_files_from(&options, &mut diagnostic_reporter)
        };

        // Compile and generate code for each test Slice file.
        for slice_file in test_slice_files {
            let mut options = SliceOptions::default();
            options.sources.push(slice_file.relative_path);
            options.references.push(slice_dir.clone());
            options.references.push(tests_dir.clone());

            let compilation_data = match compile(&options) {
                Ok(compilation_data) => compilation_data,
                Err(compilation_data) => {
                    compilation_data.into_exit_code(); // This prints the diagnostics
                    panic!("Failed to compile IceRpc.Tests Slice files");
                }
            };

            generate_code(compilation_data.files.values().next().unwrap());
        }
    }

    #[test]
    fn unique_filenames() {
        // Arrange
        let root_dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("..").join("..");
        let greeter1 = root_dir
            .join("src")
            .join("IceRpc.ProjectTemplates")
            .join("Templates")
            .join("IceRpc-Client")
            .join("Greeter.slice");
        let greeter2 = root_dir
            .join("src")
            .join("IceRpc.ProjectTemplates")
            .join("Templates")
            .join("IceRpc-Server")
            .join("Greeter.slice");

        let mut options = SliceOptions::default();
        options.sources.push(greeter1.display().to_string());
        options.sources.push(greeter2.display().to_string());

        // Act
        let compilation_data: CompilationData = compile(&options).into();

        // Assert
        let expected = Diagnostic::new(Error::IO {
            action: "generate code for",
            path: compilation_data.files.values().last().unwrap().relative_path.clone(),
            error: io::ErrorKind::InvalidInput.into(),
        });
        let diagnostics = diagnostics_from_compilation_data(compilation_data);

        check_diagnostics(diagnostics, [expected]);
    }
}

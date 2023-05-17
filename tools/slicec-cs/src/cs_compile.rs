// Copyright (c) ZeroC, Inc.

use super::attribute_patcher::patch_attributes;
use super::validators::cs_validator::validate_cs_attributes;
use slice::compilation_state::CompilationState;
use slice::diagnostics::{Diagnostic, Error};
use std::io;

pub fn cs_patcher(compilation_state: &mut CompilationState) {
    unsafe { compilation_state.apply_unsafe(patch_attributes) };
}

pub fn cs_validator(compilation_state: &mut CompilationState) {
    compilation_state.apply(validate_cs_attributes);
    compilation_state.apply(check_for_unique_names);
}

fn check_for_unique_names(compilation_state: &mut CompilationState) {
    let mut file_map = std::collections::HashMap::new();

    for slice_file in compilation_state.files.values().filter(|file| file.is_source) {
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
            .report(&mut compilation_state.diagnostic_reporter)
        }
    }
}

#[cfg(test)]
mod test {
    use super::{cs_patcher, cs_validator};
    use crate::generators::generate_from_slice_file;
    use slice::diagnostics::{Diagnostic, DiagnosticReporter, Error};
    use slice::slice_options::SliceOptions;
    use slice::test_helpers::{check_diagnostics, diagnostics_from_compilation_state};
    use slice::utils::file_util::resolve_files_from;
    use std::io;
    use std::path::Path;

    // This test compiles all the Slice files in the tests directory (recursive).
    // Since this should exercise all the code generation, we can use this to
    // generate code coverage data.
    #[test]
    fn compile_all_test_slice() {
        let root_dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
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

            let compilation_state = slice::compile_from_options(&options, cs_patcher, cs_validator);
            if compilation_state.diagnostic_reporter.has_errors() {
                compilation_state.into_exit_code(); // This prints the diagnostics
                panic!("Failed to compile IceRpc.Tests Slice files");
            }

            generate_from_slice_file(compilation_state.files.values().next().unwrap());
        }
    }

    #[test]
    fn unique_filenames() {
        // Arrange
        let root_dir = Path::new(env!("CARGO_MANIFEST_DIR")).join("../..");
        let slice1 = root_dir.join("tests/IceRpc.Tests/Slice/Pingable.slice");
        let slice2 = root_dir.join("tests/IntegrationTests/Pingable.slice");

        let mut options = SliceOptions::default();
        options.sources.push(slice1.display().to_string());
        options.sources.push(slice2.display().to_string());

        // Act
        let compilation_state = slice::compile_from_options(&options, cs_patcher, cs_validator);

        // Assert
        let expected = Diagnostic::new(Error::IO {
            action: "generate code for",
            path: compilation_state.files.values().last().unwrap().relative_path.clone(),
            error: io::ErrorKind::InvalidInput.into(),
        });
        let diagnostics = diagnostics_from_compilation_state(compilation_state);

        check_diagnostics(diagnostics, [expected]);
    }
}

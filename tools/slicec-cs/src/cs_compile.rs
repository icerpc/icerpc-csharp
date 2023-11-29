// Copyright (c) ZeroC, Inc.

use crate::cs_attributes::*;
use slicec::ast::node::Node;
use slicec::compilation_state::CompilationState;
use slicec::diagnostics::{Diagnostic, Error};
use slicec::grammar::attributes::Unparsed;
use slicec::grammar::{AttributeFunctions, Encoding, NamedSymbol, Symbol, Type};
use std::io;

pub unsafe fn cs_patcher(compilation_state: &mut CompilationState) {
    let attribute_patcher = slicec::patch_attributes!(
        "cs::",
        CsAttribute,
        CsEncodedReturn,
        CsIdentifier,
        CsInternal,
        CsNamespace,
        CsReadonly,
        CsType,
    );
    compilation_state.apply_unsafe(attribute_patcher);
}

pub fn cs_validator(compilation_state: &mut CompilationState) {
    compilation_state.apply(check_for_unique_names);
    compilation_state.apply(ensure_custom_types_have_type_attribute);

    // TODO: remove this when we add proper support for enums with associated fields.
    compilation_state.apply(disallow_enums_with_associated_fields);
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
            .push_into(&mut compilation_state.diagnostics)
        }
    }
}

fn ensure_custom_types_have_type_attribute(compilation_state: &mut CompilationState) {
    for node in compilation_state.ast.as_slice() {
        if let Node::CustomType(custom_type_ptr) = node {
            let custom_type = custom_type_ptr.borrow();
            if !custom_type.has_attribute::<CsType>() {
                Diagnostic::new(Error::MissingRequiredAttribute {
                    attribute: CsType::directive().to_owned(),
                })
                .set_span(custom_type.span())
                .push_into(&mut compilation_state.diagnostics);
            }
        }
    }
}

fn disallow_enums_with_associated_fields(compilation_state: &mut CompilationState) {
    for node in compilation_state.ast.as_slice() {
        if let Node::Enum(enum_ptr) = node {
            let enum_def = enum_ptr.borrow();
            if enum_def.underlying.is_none() && !enum_def.supported_encodings().supports(Encoding::Slice1) {
                let identifier = enum_def.identifier();
                Diagnostic::new(Error::Syntax {
                    message: "enums with associated fields are not supported by slicec-cs".to_owned(),
                })
                .add_note(
                    format!("Try specifying an underlying type on your enum: `enum {identifier}: varint32`"),
                    None,
                )
                .set_span(enum_def.span())
                .push_into(&mut compilation_state.diagnostics);
            }
        }
    }
}

#[cfg(test)]
mod test {
    use super::{check_for_unique_names, cs_patcher, cs_validator};
    use crate::cs_options::CsOptions;
    use crate::generators::generate_from_slice_file;
    use slicec::compilation_state::CompilationState;
    use slicec::diagnostics::{Diagnostic, Diagnostics, Error};
    use slicec::slice_file::SliceFile;
    use slicec::test_helpers::{check_diagnostics, diagnostics_from_compilation_state};
    use slicec::utils::file_util::resolve_files_from;
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

        let mut cs_options = CsOptions::default();
        let slice_options = &mut cs_options.slice_options;
        slice_options.references.push(tests_dir);

        // Use `resolve_files_from` to find all Slice files in the tests directory.
        let test_slice_files = resolve_files_from(slice_options, &mut Diagnostics::new());

        // Compile and generate code for each test Slice file.
        for slice_file in test_slice_files {
            let slice_options = &mut cs_options.slice_options;
            slice_options.sources.push(slice_file.relative_path);
            slice_options.references.push(slice_dir.clone());

            let compilation_state = slicec::compile_from_options(slice_options, cs_patcher, cs_validator);
            if compilation_state.diagnostics.has_errors() {
                compilation_state.emit_diagnostics(slice_options);
                panic!("Failed to compile IceRpc.Tests Slice files");
            }

            generate_from_slice_file(compilation_state.files.values().next().unwrap(), &cs_options);
        }
    }

    #[test]
    fn unique_filenames() {
        // Arrange
        let cs_options = CsOptions::default();
        let mut compilation_state = CompilationState::create();
        compilation_state.files.insert(
            "foo/Pingable.slice".to_owned(),
            SliceFile::new("foo/Pingable.slice".to_owned(), "".to_owned(), true),
        );
        compilation_state.files.insert(
            "bar/Pingable.slice".to_owned(),
            SliceFile::new("bar/Pingable.slice".to_owned(), "".to_owned(), true),
        );

        // Act
        check_for_unique_names(&mut compilation_state);

        // Assert
        let expected = Diagnostic::new(Error::IO {
            action: "generate code for",
            path: compilation_state.files.values().last().unwrap().relative_path.clone(),
            error: io::ErrorKind::InvalidInput.into(),
        });
        let diagnostics = diagnostics_from_compilation_state(compilation_state, &cs_options.slice_options);

        check_diagnostics(diagnostics, [expected]);
    }
}

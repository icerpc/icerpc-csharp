// Copyright (c) ZeroC, Inc.

use crate::cs_attributes;
use crate::cs_compile::{cs_patcher, cs_validator};
use slicec::diagnostics::{Diagnostic, Error};
use slicec::test_helpers::{check_diagnostics, diagnostics_from_compilation_state};
use test_case::test_case;

/// This function parses the provided Slice file and returns any Diagnostics that were emitted during parsing.
#[must_use]
pub fn parse_for_diagnostics(slice: impl Into<String>) -> Vec<Diagnostic> {
    let state = slicec::compile_from_strings(&[&slice.into()], None, cs_patcher, cs_validator);
    diagnostics_from_compilation_state(state)
}

/// Asserts that the provided slice parses okay, producing no errors.
pub fn assert_parses(slice: impl Into<String>) {
    let diagnostics = parse_for_diagnostics(slice);
    let expected: [Diagnostic; 0] = []; // Compiler needs the type hint.
    check_diagnostics(diagnostics, expected);
}

#[test]
fn identifier_attribute_no_args() {
    // Arrange
    let slice = "
        module Test

        [cs::identifier()]
        struct S {}
    ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let argument = cs_attributes::IDENTIFIER.to_owned() + r#"("<argument>")"#;
    let expected = Diagnostic::new(Error::MissingRequiredArgument { argument });

    check_diagnostics(diagnostics, [expected]);
}

#[test]
fn identifier_attribute_multiple_args() {
    // Arrange
    let slice = "
        module Test

        [cs::identifier(\"Foo\", \"Bar\")]
        struct S {}
    ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let expected = Diagnostic::new(Error::TooManyArguments {
        expected: cs_attributes::IDENTIFIER.to_owned() + r#"("<argument>")"#,
    });

    check_diagnostics(diagnostics, [expected]);
}

#[test]
fn identifier_attribute_single_arg() {
    // Arrange
    let slice = "
        module Test

        [cs::identifier(\"Foo\")]
        struct S {}
    ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    assert!(diagnostics.is_empty());
}

#[test]
fn identifier_attribute_invalid_on_modules() {
    // Arrange
    let slice = "
        [cs::identifier(\"Foo\")]
        module Test
    ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let attribute = cs_attributes::IDENTIFIER.to_owned();
    let expected = Diagnostic::new(Error::UnexpectedAttribute { attribute });

    check_diagnostics(diagnostics, [expected]);
}

#[test]
fn identifier_attribute_on_parameter() {
    // Arrange
    let slice = "
        module Test

        interface I {
            op([cs::identifier(\"newParam\")] myParam: int32)
        }
    ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    assert!(diagnostics.is_empty());
}

#[test]
fn identifier_attribute_on_type_alias_fails() {
    // Arrange
    let slice = "
        module Test

        [cs::identifier(\"Foo\")]
        typealias S = int32
    ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let expected = Diagnostic::new(Error::UnexpectedAttribute {
        attribute: cs_attributes::IDENTIFIER.to_owned(),
    });

    check_diagnostics(diagnostics, [expected]);
}

#[test]
fn bad_attribute_on_type_ref_fails() {
    // Arrange
    let slice = "
        module Test
        typealias S = [cs::identifier(\"int23\")] int32
    ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let expected = Diagnostic::new(Error::UnexpectedAttribute {
        attribute: cs_attributes::IDENTIFIER.to_owned(),
    });

    check_diagnostics(diagnostics, [expected]);
}

#[test]
fn cs_type_on_custom_type() {
    // Arrange
    let slice = format!(
        "
        module Test
        [cs::type(\"MyType\")]
        custom S
        "
    );

    // Act / Assert
    assert_parses(slice);
}

#[test_case("sequence<int32>"; "sequence")]
#[test_case("dictionary<int32, int32>"; "dictionary")]
fn cs_type_on_valid_type_ref(slice_type: &str) {
    // Arrange
    let slice = format!(
        "
        module Test
        typealias S = [cs::type(\"MyType\")] {slice_type}
        "
    );

    // Act / Assert
    assert_parses(slice);
}

#[test]
fn cs_type_on_invalid_type_ref_fail() {
    // Arrange
    let slice = "
        module Test
        typealias S = [cs::type(\"SomeGeneric\")] string
    ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let expected = Diagnostic::new(Error::UnexpectedAttribute {
        attribute: cs_attributes::TYPE.to_owned(),
    });

    check_diagnostics(diagnostics, [expected]);
}

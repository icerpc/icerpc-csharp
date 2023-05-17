// Copyright (c) ZeroC, Inc.

use crate::cs_attributes;
use crate::cs_compile::{cs_patcher, cs_validator};
use slice::diagnostics::{Diagnostic, Error};
use slice::slice_options::SliceOptions;
use slice::test_helpers::diagnostics_from_compilation_state;

fn parse_for_diagnostics(slice: &str) -> Vec<Diagnostic> {
    let slice_options = SliceOptions::default();
    let state = slice::compile_from_strings(&[slice], Some(slice_options), cs_patcher, cs_validator);
    diagnostics_from_compilation_state(state)
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
    let expected = [Diagnostic::new(Error::MissingRequiredArgument { argument })];

    std::iter::zip(expected, diagnostics)
        .for_each(|(expected, actual)| assert_eq!(expected.message(), actual.message()));
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
    let expected = [Diagnostic::new(Error::TooManyArguments {
        expected: cs_attributes::IDENTIFIER.to_owned() + r#"("<argument>")"#,
    })];
    std::iter::zip(expected, diagnostics)
        .for_each(|(expected, actual)| assert_eq!(expected.message(), actual.message()));
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
    let expected = [Diagnostic::new(Error::UnexpectedAttribute { attribute })];
    std::iter::zip(expected, diagnostics)
        .for_each(|(expected, actual)| assert_eq!(expected.message(), actual.message()));
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
    let expected = [Diagnostic::new(Error::UnexpectedAttribute {
        attribute: cs_attributes::IDENTIFIER.to_owned(),
    })];
    std::iter::zip(expected, diagnostics)
        .for_each(|(expected, actual)| assert_eq!(expected.message(), actual.message()));
}

// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::diagnostics::{Warning, WarningKind};

use super::super::*;

#[test]
fn identifier_attribute_no_args() {
    // Arrange
    let slice = "
            module Test;

            [cs::identifier()]
            struct S {}
        ";

    // Act
    let diagnostic_reporter = slice::compile_from_strings(&[slice], None)
        .and_then(patch_attributes)
        .and_then(patch_comments)
        .and_then(validate_cs_attributes)
        .unwrap_err()
        .diagnostic_reporter;

    // Assert
    let argument = cs_attributes::IDENTIFIER.to_owned() + r#"("<argument>")"#;
    let expected = [Error::new(ErrorKind::MissingRequiredArgument { argument })];
    std::iter::zip(expected, diagnostic_reporter.into_diagnostics())
        .for_each(|(expected, actual)| assert_eq!(expected.to_string(), actual.message()));
}

#[test]
fn identifier_attribute_multiple_args() {
    // Arrange
    let slice = "
            module Test;

            [cs::identifier(\"Foo\", \"Bar\")]
            struct S {}
        ";

    // Act
    let diagnostic_reporter = slice::compile_from_strings(&[slice], None)
        .and_then(patch_attributes)
        .and_then(patch_comments)
        .and_then(validate_cs_attributes)
        .unwrap_err()
        .diagnostic_reporter;

    // Assert
    let expected = [Error::new(ErrorKind::TooManyArguments {
        expected: cs_attributes::IDENTIFIER.to_owned() + r#"("<argument>")"#,
    })];
    std::iter::zip(expected, diagnostic_reporter.into_diagnostics())
        .for_each(|(expected, actual)| assert_eq!(expected.to_string(), actual.message()));
}

#[test]
fn identifier_attribute_single_arg() {
    // Arrange
    let slice = "
            module Test;

            [cs::identifier(\"Foo\")]
            struct S {}
        ";

    // Act
    let diagnostic_reporter = slice::compile_from_strings(&[slice], None)
        .and_then(patch_attributes)
        .and_then(patch_comments)
        .and_then(validate_cs_attributes)
        .unwrap()
        .diagnostic_reporter;

    // Assert
    assert!(diagnostic_reporter.into_diagnostics().is_empty());
}

#[test]
fn identifier_attribute_invalid_on_modules() {
    // Arrange
    let slice = "
            [cs::identifier(\"Foo\")]
            module Test;
        ";

    // Act
    let diagnostic_reporter = slice::compile_from_strings(&[slice], None)
        .and_then(patch_attributes)
        .and_then(patch_comments)
        .and_then(validate_cs_attributes)
        .unwrap_err()
        .diagnostic_reporter;
    // Assert
    let attribute = cs_attributes::IDENTIFIER.to_owned();
    let expected = [Error::new(ErrorKind::UnexpectedAttribute { attribute })];
    std::iter::zip(expected, diagnostic_reporter.into_diagnostics())
        .for_each(|(expected, actual)| assert_eq!(expected.to_string(), actual.message()));
}

#[test]
fn identifier_attribute_on_parameter() {
    // Arrange
    let slice = "
            module Test;

            interface I {
                op([cs::identifier(\"newParam\")] myParam: int32);
            }
        ";

    // Act
    let diagnostic_reporter = slice::compile_from_strings(&[slice], None)
        .and_then(patch_attributes)
        .and_then(patch_comments)
        .and_then(validate_cs_attributes)
        .unwrap()
        .diagnostic_reporter;

    // Assert
    assert_eq!(diagnostic_reporter.into_diagnostics().len(), 0);
}

#[test]
fn identifier_attribute_on_type_alias_fails() {
    // Arrange
    let slice = "
            module Test;

            [cs::identifier(\"Foo\")]
            typealias S = int32;
        ";

    // Act
    let diagnostic_reporter = slice::compile_from_strings(&[slice], None)
        .and_then(patch_attributes)
        .and_then(patch_comments)
        .and_then(validate_cs_attributes)
        .unwrap()
        .diagnostic_reporter;

    // Assert
    let expected = [Warning::new(WarningKind::InconsequentialUseOfAttribute {
        attribute: cs_attributes::IDENTIFIER.to_owned(),
        kind: "typealias".to_owned(),
    })];
    std::iter::zip(expected, diagnostic_reporter.into_diagnostics())
        .for_each(|(expected, actual)| assert_eq!(expected.to_string(), actual.message()));
}

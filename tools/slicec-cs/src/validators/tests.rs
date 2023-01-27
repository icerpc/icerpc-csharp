// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::diagnostics::{Diagnostic, Warning, WarningKind};

use super::super::*;

fn parse_for_diagnostics(slice: &str) -> Vec<Diagnostic> {
    let data = match slice::compile_from_strings(&[slice], None)
        .and_then(patch_attributes)
        .and_then(patch_comments)
        .and_then(validate_cs_attributes)
    {
        Ok(data) => data,
        Err(data) => data,
    };

    data.diagnostic_reporter
        .into_diagnostics(&data.ast, &data.files)
        .collect()
}

#[test]
fn identifier_attribute_no_args() {
    // Arrange
    let slice = "
            module Test;

            [cs::identifier()]
            struct S {}
        ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let argument = cs_attributes::IDENTIFIER.to_owned() + r#"("<argument>")"#;
    let expected = [Error::new(ErrorKind::MissingRequiredArgument { argument })];

    std::iter::zip(expected, diagnostics)
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
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let expected = [Error::new(ErrorKind::TooManyArguments {
        expected: cs_attributes::IDENTIFIER.to_owned() + r#"("<argument>")"#,
    })];
    std::iter::zip(expected, diagnostics)
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
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    assert!(diagnostics.is_empty());
}

#[test]
fn identifier_attribute_invalid_on_modules() {
    // Arrange
    let slice = "
            [cs::identifier(\"Foo\")]
            module Test;
        ";

    // Act
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let attribute = cs_attributes::IDENTIFIER.to_owned();
    let expected = [Error::new(ErrorKind::UnexpectedAttribute { attribute })];
    std::iter::zip(expected, diagnostics)
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
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    assert_eq!(diagnostics.len(), 0);
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
    let diagnostics = parse_for_diagnostics(slice);

    // Assert
    let expected = [Warning::new(WarningKind::InconsequentialUseOfAttribute {
        attribute: cs_attributes::IDENTIFIER.to_owned(),
        kind: "typealias".to_owned(),
    })];
    std::iter::zip(expected, diagnostics)
        .for_each(|(expected, actual)| assert_eq!(expected.to_string(), actual.message()));
}

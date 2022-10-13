// Copyright (c) ZeroC, Inc. All rights reserved.

pub mod cs_validator;

#[cfg(test)]
mod test {
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
        let diagnostic_reporter = slice::parse_from_strings(&[slice], None)
            .and_then(patch_comments)
            .and_then(validate_cs_attributes)
            .unwrap_err()
            .diagnostic_reporter;
        // Assert
        let expected = [Error::new(
            ErrorKind::MissingRequiredArgument(cs_attributes::IDENTIFIER.to_owned() + r#"("<identifier>")"#),
            None,
        )];
        std::iter::zip(expected, diagnostic_reporter.into_diagnostics())
            .for_each(|(expected, actual)| assert_eq!(expected.to_string(), actual.to_string()));
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
        let diagnostic_reporter = slice::parse_from_strings(&[slice], None)
            .and_then(patch_comments)
            .and_then(validate_cs_attributes)
            .unwrap_err()
            .diagnostic_reporter;

        // Assert
        let expected = [Error::new(
            ErrorKind::TooManyArguments(cs_attributes::IDENTIFIER.to_owned() + r#"("<identifier>")"#),
            None,
        )];
        std::iter::zip(expected, diagnostic_reporter.into_diagnostics())
            .for_each(|(expected, actual)| assert_eq!(expected.to_string(), actual.to_string()));
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
        let diagnostic_reporter = slice::parse_from_strings(&[slice], None)
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
        let diagnostic_reporter = slice::parse_from_strings(&[slice], None)
            .and_then(patch_comments)
            .and_then(validate_cs_attributes)
            .unwrap_err()
            .diagnostic_reporter;
        // Assert
        let expected = [Error::new(
            ErrorKind::InvalidAttribute(cs_attributes::IDENTIFIER.to_owned(), "module".to_owned()),
            None,
        )];
        std::iter::zip(expected, diagnostic_reporter.into_diagnostics())
            .for_each(|(expected, actual)| assert_eq!(expected.to_string(), actual.to_string()));
    }

    #[test]
    fn identifier_attribute_on_parameter() {
        // Arrange
        let slice = "
            module Test;

            interface I {
                oP([cs::identifier(\"newParam\")] myParam: int32);
            }
        ";

        // Act
        let diagnostic_reporter = slice::parse_from_strings(&[slice], None)
            .and_then(patch_comments)
            .and_then(validate_cs_attributes)
            .unwrap()
            .diagnostic_reporter;

        // Assert
        assert_eq!(diagnostic_reporter.into_diagnostics().len(), 0);
    }
}

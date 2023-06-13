// Copyright (c) ZeroC, Inc.

use super::*;

#[derive(Debug)]
pub struct CsIdentifier {
    pub identifier: String,
}

impl CsIdentifier {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, reporter: &mut DiagnosticReporter) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_exactly_one_argument_was_provided(args, Self::directive(), span, reporter);

        let identifier = args.first().cloned().unwrap_or_default();
        CsIdentifier { identifier }
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, reporter: &mut DiagnosticReporter) {
        match applied_on {
            Attributables::Module(_) => {
                let attribute = Self::directive().to_owned();
                Diagnostic::new(Error::UnexpectedAttribute { attribute })
                    .set_span(span)
                    .add_note(
                        format!("To rename a module use {} instead", CsNamespace::directive()),
                        None,
                    )
                    .report(reporter);
            }
            Attributables::SliceFile(_) | Attributables::TypeAlias(_) | Attributables::TypeRef(_) => {
                report_unexpected_attribute(self, span, None, reporter)
            }
            _ => {}
        }
    }
}

implement_attribute_kind_for!(CsIdentifier, "cs::identifier", false);

// Copyright (c) ZeroC, Inc.

use super::*;

#[derive(Debug)]
pub struct CsEncodedResult {}

impl CsEncodedResult {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, reporter: &mut DiagnosticReporter) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_no_arguments_were_provided(args, Self::directive(), span, reporter);

        CsEncodedResult {}
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, reporter: &mut DiagnosticReporter) {
        if let Attributables::Operation(operation) = applied_on {
            if operation.non_streamed_return_members().is_empty() {
                Diagnostic::new(Error::UnexpectedAttribute {
                    attribute: Self::directive().to_owned(),
                })
                .set_span(span)
                .add_note(
                    if operation.streamed_return_member().is_some() {
                        format!(
                            "The '{}' attribute is not applicable to an operation that only returns a stream.",
                            Self::directive(),
                        )
                    } else {
                        format!(
                            "The '{}' attribute is not applicable to an operation that does not return anything.",
                            Self::directive(),
                        )
                    },
                    None,
                )
                .report(reporter);
            }
        } else {
            // TODO Add a note explaining what this can be applied to.
            report_unexpected_attribute(self, span, None, reporter);
        }
    }
}

implement_attribute_kind_for!(CsEncodedResult, "cs::encodedResult", false);

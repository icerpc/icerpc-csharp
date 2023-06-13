// Copyright (c) ZeroC, Inc.

use super::*;

#[derive(Debug)]
pub struct CsInternal {}

impl CsInternal {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, reporter: &mut DiagnosticReporter) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_no_arguments_were_provided(args, Self::directive(), span, reporter);

        CsInternal {}
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, reporter: &mut DiagnosticReporter) {
        if !matches!(
            applied_on,
            Attributables::Struct(_)
                | Attributables::Class(_)
                | Attributables::Exception(_)
                | Attributables::Interface(_)
                | Attributables::Enum(_)
        ) {
            report_unexpected_attribute(self, span, None, reporter);
        }
    }
}

implement_attribute_kind_for!(CsInternal, "cs::internal", false);

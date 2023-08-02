// Copyright (c) ZeroC, Inc.

use super::*;

#[derive(Debug)]
pub struct CsInternal {}

impl CsInternal {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, diagnostics: &mut Diagnostics) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_no_arguments_were_provided(args, Self::directive(), span, diagnostics);

        CsInternal {}
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, diagnostics: &mut Diagnostics) {
        if !matches!(
            applied_on,
            Attributables::Struct(_)
                | Attributables::Class(_)
                | Attributables::Exception(_)
                | Attributables::Interface(_)
                | Attributables::Enum(_)
        ) {
            report_unexpected_attribute(self, span, None, diagnostics);
        }
    }
}

implement_attribute_kind_for!(CsInternal, "cs::internal", false);

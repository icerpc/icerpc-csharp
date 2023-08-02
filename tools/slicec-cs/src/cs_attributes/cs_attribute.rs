// Copyright (c) ZeroC, Inc.

use super::*;

#[derive(Debug)]
pub struct CsAttribute {
    pub attribute: String,
}

impl CsAttribute {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, diagnostics: &mut Diagnostics) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_exactly_one_argument_was_provided(args, Self::directive(), span, diagnostics);

        let attribute = args.first().cloned().unwrap_or_default();
        CsAttribute { attribute }
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, diagnostics: &mut Diagnostics) {
        if !matches!(
            applied_on,
            Attributables::Enum(_) | Attributables::Enumerator(_) | Attributables::Field(_),
        ) {
            // TODO Add a note explaining what this can be applied to, and how to put attributes on other things.
            report_unexpected_attribute(self, span, None, diagnostics);
        }
    }
}

implement_attribute_kind_for!(CsAttribute, "cs::attribute", true);

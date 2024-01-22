// Copyright (c) ZeroC, Inc.

use super::*;

#[derive(Debug)]
pub struct CsIdentifier {
    pub identifier: String,
}

impl CsIdentifier {
    pub fn parse_from(Unparsed { directive, args }: &Unparsed, span: &Span, diagnostics: &mut Diagnostics) -> Self {
        debug_assert_eq!(directive, Self::directive());

        check_that_exactly_one_argument_was_provided(args, Self::directive(), span, diagnostics);

        let identifier = args.first().cloned().unwrap_or_default();
        CsIdentifier { identifier }
    }

    pub fn validate_on(&self, applied_on: Attributables, span: &Span, diagnostics: &mut Diagnostics) {
        match applied_on {
            Attributables::Module(_) => {
                let note = format!(
                    "To map a module to a different C# namespace, use '{}' instead",
                    CsNamespace::directive(),
                );
                report_unexpected_attribute(self, span, Some(&note), diagnostics);
            }

            Attributables::SliceFile(_) | Attributables::TypeAlias(_) | Attributables::TypeRef(_) => {
                report_unexpected_attribute(self, span, None, diagnostics)
            }

            _ => {}
        }
    }
}

implement_attribute_kind_for!(CsIdentifier, "cs::identifier", false);

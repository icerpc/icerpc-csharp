// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::NamedSymbol;

pub trait AttributeExt {
    fn custom_attributes(&self) -> Vec<String>;
    fn obsolete_attribute(&self, check_parent: bool) -> Option<String>;

    /// The C# Type Id attribute. Returns None if no type id attribute is required.
    fn type_id_attribute(&self) -> String;
}

impl<T: NamedSymbol + ?Sized> AttributeExt for T {
    fn custom_attributes(&self) -> Vec<String> {
        if let Some(attributes) = self.find_attribute("cs:attribute") {
            attributes.to_vec()
        } else {
            vec![]
        }
    }

    fn obsolete_attribute(&self, _check_parent: bool) -> Option<String> {
        // TODO: check parent once we no longer need the ast)
        let deprecate_reason = if let Some(deprecate) = self.find_attribute("deprecate") {
            match deprecate.as_slice() {
                [] => Some(format!("This {} has been deprecated", self.kind())),
                _ => Some(deprecate.to_vec().join("\n")),
            }
        // } else if check_parent {
        // get_deprecate_reason(named_symbol.parent(), false)
        } else {
            None
        };

        deprecate_reason.map(|r| format!(r#"global::System.Obsolete("{}")"#, r))
    }

    fn type_id_attribute(&self) -> String {
        format!(
            r#"IceRpc.Slice.TypeId("{}::{}")"#,
            self.scope(),
            self.identifier()
        )
    }
}

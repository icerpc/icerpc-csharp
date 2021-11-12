// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::Attributable;

pub trait AttributeExt {
    fn custom_attributes(&self) -> Vec<String>;
    fn obsolete_attribute(&self, check_parent: bool) -> Option<String>;
}

impl<T: Attributable + ?Sized> AttributeExt for T {
    fn custom_attributes(&self) -> Vec<String> {
        if let Some(attributes) = self.get_attribute("cs:attribute", false) {
            attributes.to_vec()
        } else {
            vec![]
        }
    }

    fn obsolete_attribute(&self, check_parent: bool) -> Option<String> {
        self.get_attribute("deprecate", check_parent)
            .map(|arguments| {
                let reason = match arguments.as_slice() {
                    [] => format!("This {} has been deprecated", self.kind()),
                    _ => arguments.join("\n"),
                };
                format!(r#"global::System.Obsolete("{}")"#, reason)
            })
    }
}

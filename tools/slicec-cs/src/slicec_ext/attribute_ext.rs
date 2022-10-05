// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::Attributable;

pub trait AttributeExt {
    fn custom_attributes(&self) -> Vec<String>;
}

impl<T: Attributable + ?Sized> AttributeExt for T {
    fn custom_attributes(&self) -> Vec<String> {
        if let Some(attributes) = self.get_attribute(cs_attributes::ATTRIBUTE, false) {
            attributes.to_vec()
        } else {
            vec![]
        }
    }
}

pub mod cs_attributes {
    pub const ATTRIBUTE: &str = "cs::attribute";
    pub const ENCODED_RESULT: &str = "cs::encodedResult";
    pub const GENERIC: &str = "cs::generic";
    pub const INTERNAL: &str = "cs::internal";
    pub const NAMESPACE: &str = "cs::namespace";
    pub const READONLY: &str = "cs::readonly";
    pub const TYPE: &str = "cs::type";
}

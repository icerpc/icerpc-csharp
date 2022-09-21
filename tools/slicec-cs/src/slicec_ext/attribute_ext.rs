// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::Attributable;

pub trait AttributeExt {
    fn custom_attributes(&self) -> Vec<String>;
}

impl<T: Attributable + ?Sized> AttributeExt for T {
    fn custom_attributes(&self) -> Vec<String> {
        if let Some(attributes) = self.get_attribute("cs::attribute", false) {
            attributes.to_vec()
        } else {
            vec![]
        }
    }
}

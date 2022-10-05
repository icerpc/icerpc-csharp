// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::slicec_ext::cs_attributes;
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

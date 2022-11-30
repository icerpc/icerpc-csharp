// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::{Attribute, AttributeKind, LanguageKind};

#[derive(Clone, Debug)]
pub enum CsAttributeKind {
    Attribute { attributes: Vec<String> },
    EncodedResult,
    Generic { generic_type: String },
    Identifier { identifier: String },
    Internal,
    Namespace { namespace: String },
    Readonly,
    Type { name: String },
}

impl LanguageKind for CsAttributeKind {
    fn as_any(&self) -> &dyn std::any::Any {
        self
    }

    fn clone_kind(&self) -> Box<dyn LanguageKind> {
        Box::new(self.clone())
    }

    fn debug_kind(&self) -> &str {
        ""
    }
}

fn as_cs_attribute(attribute: &Attribute) -> Option<&CsAttributeKind> {
    match &attribute.kind {
        AttributeKind::LanguageKind { directive: _, kind } => kind.as_any().downcast_ref::<CsAttributeKind>(),
        _ => None,
    }
}

pub fn match_cs_attribute(attribute: &Attribute) -> Option<Vec<String>> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Attribute { attributes } => Some(attributes.clone()),
        _ => None,
    })
}

pub fn match_cs_encoded_result(attribute: &Attribute) -> Option<()> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::EncodedResult => Some(()),
        _ => None,
    })
}

pub fn match_cs_generic(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Generic { generic_type } => Some(generic_type.clone()),
        _ => None,
    })
}

pub fn match_cs_identifier(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Identifier { identifier } => Some(identifier.to_owned()),
        _ => None,
    })
}

pub fn match_cs_internal(attribute: &Attribute) -> Option<()> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Internal => Some(()),
        _ => None,
    })
}

pub fn match_cs_namespace(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Namespace { namespace } => Some(namespace.to_owned()),
        _ => None,
    })
}

pub fn match_cs_readonly(attribute: &Attribute) -> Option<()> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Readonly => Some(()),
        _ => None,
    })
}

pub fn match_cs_type(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Type { name } => Some(name.to_owned()),
        _ => None,
    })
}

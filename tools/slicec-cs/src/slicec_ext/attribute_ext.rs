// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::grammar::{Attribute, AttributeKind, LanguageKind};

use crate::cs_attributes;

#[derive(Clone, Debug)]
pub enum CsAttributeKind {
    Attribute { attribute: String },
    EncodedResult,
    Generic { generic_type: String },
    Identifier { identifier: String },
    Internal,
    Namespace { namespace: String },
    Readonly,
    Type { name: String },
}

impl LanguageKind for CsAttributeKind {
    fn directive(&self) -> &str {
        match &self {
            CsAttributeKind::Attribute { .. } => cs_attributes::ATTRIBUTE,
            CsAttributeKind::EncodedResult => cs_attributes::ENCODED_RESULT,
            CsAttributeKind::Generic { .. } => cs_attributes::GENERIC,
            CsAttributeKind::Identifier { .. } => cs_attributes::IDENTIFIER,
            CsAttributeKind::Internal => cs_attributes::INTERNAL,
            CsAttributeKind::Namespace { .. } => cs_attributes::NAMESPACE,
            CsAttributeKind::Readonly => cs_attributes::READONLY,
            CsAttributeKind::Type { .. } => cs_attributes::TYPE,
        }
    }

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

impl From<CsAttributeKind> for AttributeKind {
    fn from(kind: CsAttributeKind) -> Self {
        AttributeKind::LanguageKind { kind: Box::new(kind) }
    }
}

fn as_cs_attribute(attribute: &Attribute) -> Option<&CsAttributeKind> {
    match &attribute.kind {
        AttributeKind::LanguageKind { kind } => kind.as_any().downcast_ref::<CsAttributeKind>(),
        _ => None,
    }
}

pub fn match_cs_attribute(attribute: &Attribute) -> Option<String> {
    as_cs_attribute(attribute).and_then(|a| match a {
        CsAttributeKind::Attribute { attribute } => Some(attribute.clone()),
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

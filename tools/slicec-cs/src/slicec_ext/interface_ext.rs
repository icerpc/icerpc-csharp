// Copyright (c) ZeroC, Inc.

use super::entity_ext::EntityExt;

use slice::grammar::Interface;

pub trait InterfaceExt: EntityExt {
    /// Name of the generated proxy struct, e.g. if the Slice interface is `Foo`, the C# proxy is struct `FooProxy`.
    fn proxy_name(&self) -> String {
        self.interface_name().chars().skip(1).collect::<String>() + "Proxy"
    }

    fn scoped_service_name(&self, current_namespace: &str) -> String {
        let namespace = self.namespace();
        if namespace == current_namespace {
            self.service_name()
        } else {
            format!("global::{}.{}", namespace, self.service_name())
        }
    }

    fn scoped_proxy_name(&self, current_namespace: &str) -> String {
        let namespace = self.namespace();
        if namespace == current_namespace {
            self.proxy_name()
        } else {
            format!("global::{}.{}", namespace, self.proxy_name())
        }
    }

    /// The name of the generated C# service interface for this Slice interface.
    /// eg. If the Slice interface is `Foo`, the C# service interface is `IFooService`.
    fn service_name(&self) -> String {
        self.interface_name() + "Service"
    }
}

impl InterfaceExt for Interface {}

// Copyright (c) ZeroC, Inc.

use super::entity_ext::EntityExt;

use slice::grammar::Interface;

pub trait InterfaceExt: EntityExt {
    /// Name of the generated implementation struct for this Slice interface's proxy.
    /// eg. If the slice interface is `Foo`, the C# proxy implementation is `FooProxy`.
    fn proxy_implementation_name(&self) -> String {
        self.proxy_name().chars().skip(1).collect()
    }

    /// The name of the generated C# proxy struct for this Slice interface.
    /// eg. If the slice interface is `Foo`, the C# proxy is `IFooProxy`.
    fn proxy_name(&self) -> String {
        self.interface_name() + "Proxy"
    }

    fn scoped_proxy_name(&self, current_namespace: &str) -> String {
        let namespace = self.namespace();
        if namespace == current_namespace {
            self.proxy_name()
        } else {
            format!("global::{}.{}", namespace, self.proxy_name())
        }
    }

    fn scoped_proxy_implementation_name(&self, current_namespace: &str) -> String {
        let namespace = self.namespace();
        if namespace == current_namespace {
            self.proxy_implementation_name()
        } else {
            format!("global::{}.{}", namespace, self.proxy_implementation_name())
        }
    }
}

impl InterfaceExt for Interface {}

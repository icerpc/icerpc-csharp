// Copyright (c) ZeroC, Inc. All rights reserved.

use slice::code_gen_util::TypeContext;
use slice::grammar::{Attributable, Class, NamedSymbol, Operation};

use crate::code_block::CodeBlock;
use crate::comments::{operation_parameter_doc_comment, CommentTag};
use crate::cs_util::*;
use crate::member_util::escape_parameter_name;
use crate::slicec_ext::*;

trait Builder {
    fn build(&self) -> String;
}

pub trait AttributeBuilder {
    fn add_attribute(&mut self, attributes: &str) -> &mut Self;

    fn add_type_id_attribute(&mut self, named_symbol: &dyn NamedSymbol) -> &mut Self {
        self.add_attribute(&named_symbol.type_id_attribute());
        self
    }

    fn add_compact_type_id_attribute(&mut self, class_def: &Class) -> &mut Self {
        if let Some(compact_id) = class_def.compact_id {
            self.add_attribute(&format!("IceRpc.Slice.CompactTypeId({})", compact_id));
        }
        self
    }

    /// Adds multiple "container" attributes.
    /// - The obsolete attribute
    /// - Any custom attributes
    fn add_container_attributes(&mut self, container: &dyn Attributable) -> &mut Self {
        if let Some(attribute) = container.obsolete_attribute(false) {
            self.add_attribute(&attribute);
        }

        for attribute in container.custom_attributes() {
            // The custom attribute value is always quoted in Slice and we unquote it here
            // [cs:attribute("System.Flags")] would become [System.Flags]
            debug_assert!(attribute.starts_with('\"') && attribute.ends_with('\"'));
            self.add_attribute(
                attribute
                    .strip_prefix('\"')
                    .unwrap()
                    .strip_suffix('\"')
                    .unwrap(),
            );
        }

        self
    }
}

pub trait CommentBuilder {
    fn add_comment(&mut self, tag: &str, content: &str) -> &mut Self;
}

#[derive(Clone, Debug)]
pub struct ContainerBuilder {
    container_type: String,
    name: String,
    bases: Vec<String>,
    contents: Vec<CodeBlock>,
    attributes: Vec<String>,
    comments: Vec<CommentTag>,
}

impl ContainerBuilder {
    pub fn new(container_type: &str, name: &str) -> Self {
        Self {
            container_type: container_type.to_owned(),
            name: name.to_owned(),
            bases: vec![],
            contents: vec![],
            attributes: vec![],
            comments: vec![],
        }
    }

    pub fn add_base(&mut self, base: String) -> &mut Self {
        self.bases.push(base);
        self
    }

    pub fn add_bases(&mut self, bases: &[String]) -> &mut Self {
        self.bases.extend_from_slice(bases);
        self
    }

    pub fn add_block(&mut self, content: CodeBlock) -> &mut Self {
        self.contents.push(content);
        self
    }

    pub fn build(&self) -> String {
        let mut code = CodeBlock::new();

        for comment in &self.comments {
            code.writeln(&comment.to_string());
        }

        for attribute in &self.attributes {
            code.writeln(&format!("[{}]", attribute));
        }

        writeln!(
            code,
            "{container_type} {name}{bases}",
            container_type = self.container_type,
            name = self.name,
            bases = if self.bases.is_empty() {
                "".to_string()
            } else {
                format!(" : {bases}", bases = self.bases.join(", "))
            },
        );

        let mut body_content: CodeBlock = self.contents.iter().cloned().collect();

        if body_content.is_empty() {
            code.writeln("{\n}");
        } else {
            writeln!(code, "{{\n    {body}\n}}", body = body_content.indent());
        }

        code.to_string()
    }
}

impl AttributeBuilder for ContainerBuilder {
    fn add_attribute(&mut self, attribute: &str) -> &mut Self {
        self.attributes.push(attribute.to_owned());
        self
    }
}

impl CommentBuilder for ContainerBuilder {
    fn add_comment(&mut self, tag: &str, content: &str) -> &mut Self {
        self.comments.push(CommentTag::new(tag, content));
        self
    }
}

#[derive(Clone, Debug)]
pub enum FunctionType {
    Declaration,
    BlockBody,
    ExpressionBody,
}

#[derive(Clone, Debug)]
pub struct FunctionBuilder {
    access: String,
    name: String,
    return_type: String,
    parameters: Vec<String>,
    body: CodeBlock,
    base_constructor: String,
    base_arguments: Vec<String>,
    comments: Vec<CommentTag>,
    attributes: Vec<String>,
    function_type: FunctionType,
    inherit_doc: bool,
}

impl FunctionBuilder {
    pub fn new(
        access: &str,
        return_type: &str,
        name: &str,
        function_type: FunctionType,
    ) -> FunctionBuilder {
        FunctionBuilder {
            parameters: Vec::new(),
            access: String::from(access),
            name: String::from(name),
            return_type: String::from(return_type),
            body: CodeBlock::new(),
            comments: Vec::new(),
            attributes: Vec::new(),
            base_constructor: String::from("base"),
            base_arguments: Vec::new(),
            function_type,
            inherit_doc: false,
        }
    }

    pub fn add_comment(&mut self, tag: &str, content: &str) -> &mut Self {
        self.comments.push(CommentTag::new(tag, content));
        self
    }

    fn add_comment_with_attribute(
        &mut self,
        tag: &str,
        attribute_name: &str,
        attribute_value: &str,
        content: &str,
    ) -> &mut Self {
        self.comments.push(CommentTag::with_tag_attribute(
            tag,
            attribute_name,
            attribute_value,
            content,
        ));
        self
    }

    pub fn add_parameter(
        &mut self,
        param_type: &str,
        param_name: &str,
        default_value: Option<&str>,
        doc_comment: Option<&str>,
    ) -> &mut Self {
        self.parameters.push(format!(
            "{param_type} {param_name}{default_value}",
            param_type = param_type,
            param_name = escape_keyword(param_name),
            default_value = match default_value {
                Some(value) => format!(" = {}", value),
                None => "".to_string(),
            }
        ));

        if let Some(comment) = doc_comment {
            self.add_comment_with_attribute("param", "name", param_name, comment);
        }

        self
    }

    pub fn add_parameters(&mut self, parameters: &[String]) -> &mut Self {
        for p in parameters {
            self.parameters.push(p.clone());
        }
        self
    }

    /// Set the base constructor (used when there are base parameters). The default is base
    pub fn set_base_constructor(&mut self, base_constructor: &str) -> &mut Self {
        self.base_constructor = base_constructor.to_owned();
        self
    }

    pub fn add_base_parameter(&mut self, argument: &str) -> &mut Self {
        self.base_arguments.push(argument.to_owned());
        self
    }

    pub fn add_base_parameters(&mut self, arguments: &[String]) -> &mut Self {
        for arg in arguments {
            self.base_arguments.push(arg.to_owned());
        }
        self
    }

    pub fn set_body(&mut self, body: CodeBlock) -> &mut Self {
        self.body = body;
        self
    }

    pub fn set_inherit_doc(&mut self, inherit_doc: bool) -> &mut Self {
        self.inherit_doc = inherit_doc;
        self
    }

    pub fn add_never_editor_browsable_attribute(&mut self) -> &mut Self {
        self.add_attribute(
            "global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)");
        self
    }

    pub fn add_operation_parameters(
        &mut self,
        operation: &Operation,
        context: TypeContext,
    ) -> &mut Self {
        let parameters = operation.parameters();

        for parameter in &parameters {
            // The attributes are a space separated list of attributes.
            // eg. [attribute1] [attribute2]
            let parameter_attributes = parameter.get_attribute("cs:attribute", false).map_or_else(
                || "".to_owned(),
                |vec| {
                    vec.iter()
                        .map(|a| format!("[{}]", a))
                        .collect::<Vec<_>>()
                        .join("\n")
                },
            );

            let parameter_type = parameter.to_type_string(&operation.namespace(), context);
            let parameter_name = parameter.parameter_name();

            // TODO: it would be better if we could use parameter.comment() to get the parameter
            // comment instead
            let parameter_comment =
                operation_parameter_doc_comment(operation, parameter.identifier());

            self.add_parameter(
                &format!("{}{}", parameter_attributes, &parameter_type),
                &parameter_name,
                None,
                parameter_comment,
            );
        }

        match context {
            TypeContext::Incoming => {
                self.add_parameter(
                    "IceRpc.Dispatch",
                    &escape_parameter_name(&parameters, "dispatch"),
                    None,
                    Some("The dispatch properties"),
                );
            }
            TypeContext::Outgoing => {
                self.add_parameter(
                    "IceRpc.Invocation?",
                    &escape_parameter_name(&parameters, "invocation"),
                    Some("null"),
                    Some("The invocation properties."),
                );
            }
            _ => panic!("Unexpected context value"),
        }

        self.add_parameter(
            "global::System.Threading.CancellationToken",
            &escape_parameter_name(&parameters, "cancel"),
            Some("default"),
            Some("A cancellation token that receives the cancellation requests."),
        );

        self
    }

    pub fn build(&mut self) -> CodeBlock {
        let mut code = CodeBlock::new();

        if self.inherit_doc {
            code.writeln("/// <inheritdoc/>")
        } else {
            for comment in &self.comments {
                code.writeln(&comment.to_string());
            }
        }

        for attribute in &self.attributes {
            code.writeln(&format!("[{}]", attribute));
        }

        // CodeBlock ignores whitespace only writes so there's no issue of over-padding here.
        write!(code, "{} ", self.access);
        write!(code, "{} ", self.return_type);
        write!(code, "{}({})", self.name, self.parameters.join(", "));

        match self.base_arguments.as_slice() {
            [] => {}
            _ => write!(
                code,
                "\n    : {}({})",
                self.base_constructor,
                self.base_arguments.join(", ")
            ),
        }

        match self.function_type {
            FunctionType::Declaration => {
                code.writeln(";");
            }
            FunctionType::ExpressionBody => {
                if self.body.is_empty() {
                    code.writeln(" => {{}};");
                } else {
                    writeln!(code, " =>\n    {};", self.body.indent());
                }
            }
            FunctionType::BlockBody => {
                if self.body.is_empty() {
                    code.writeln("\n{\n}");
                } else {
                    writeln!(code, "\n{{\n    {}\n}}", self.body.indent());
                }
            }
        }

        code
    }
}

impl AttributeBuilder for FunctionBuilder {
    fn add_attribute(&mut self, attribute: &str) -> &mut Self {
        self.attributes.push(attribute.to_owned());
        self
    }
}

impl CommentBuilder for FunctionBuilder {
    fn add_comment(&mut self, tag: &str, content: &str) -> &mut Self {
        self.comments.push(CommentTag::new(tag, content));
        self
    }
}

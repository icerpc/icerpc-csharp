// Copyright (c) ZeroC, Inc.

use crate::code_block::CodeBlock;
use crate::code_gen_util::TypeContext;
use crate::comments::CommentTag;
use crate::cs_attributes::CsType;
use crate::cs_util::*;
use crate::member_util::escape_parameter_name;
use crate::slicec_ext::*;
use slicec::grammar::*;

pub trait Builder {
    fn build(&self) -> CodeBlock;
}

pub trait AttributeBuilder {
    fn add_attribute(&mut self, attributes: impl Into<String>) -> &mut Self;

    fn add_default_service_path_attribute(&mut self, interface: &Interface) -> &mut Self {
        self.add_attribute(format!(
            "IceRpc.DefaultServicePath(\"{}\")",
            interface.default_service_path()
        ))
    }

    fn add_type_id_attribute(&mut self, entity: &dyn Entity) -> &mut Self {
        self.add_attribute(entity.type_id_attribute());
        self
    }

    fn add_compact_type_id_attribute(&mut self, class_def: &Class) -> &mut Self {
        if let Some(compact_id) = &class_def.compact_id {
            self.add_attribute(format!("CompactSliceTypeId({})", compact_id.value));
        }
        self
    }

    /// Adds the C# Obsolete attribute if the entity has the Slice deprecated attribute.
    fn add_obsolete_attribute(&mut self, entity: &dyn Entity) -> &mut Self {
        if let Some(attribute) = entity.obsolete_attribute() {
            self.add_attribute(attribute);
        }
        self
    }
}

pub trait CommentBuilder {
    fn add_comment(&mut self, tag: &str, content: impl Into<String>) -> &mut Self;

    fn add_generated_remark(&mut self, generated_type: &str, slice_type: &impl Entity) -> &mut Self {
        self.add_comment(
            "remarks",
            format!(
                "The Slice compiler generated this {} from the Slice {} <c>{}</c>.",
                generated_type,
                slice_type.kind(),
                slice_type.module_scoped_identifier(),
            ),
        );
        self
    }

    fn add_generated_remark_with_note(
        &mut self,
        generated_type: &str,
        note: impl Into<String>,
        slice_type: &impl Entity,
    ) -> &mut Self {
        self.add_comment(
            "remarks",
            format!(
                "The Slice compiler generated this {} from Slice {} <c>{}</c>.\n{}",
                generated_type,
                slice_type.kind(),
                slice_type.module_scoped_identifier(),
                note.into(),
            ),
        );
        self
    }

    fn add_comment_with_attribute(
        &mut self,
        tag: &str,
        attribute_name: &str,
        attribute_value: &str,
        content: impl Into<String>,
    ) -> &mut Self;

    fn add_comments(&mut self, comments: Vec<CommentTag>) -> &mut Self;
}

#[derive(Clone, Debug)]
pub struct ContainerBuilder {
    container_type: String,
    name: String,
    bases: Vec<String>,
    fields: Vec<String>,
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
            fields: vec![],
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

    pub fn add_field(&mut self, field_name: &str, field_type: &str, doc_comment: Option<&str>) -> &mut Self {
        if let Some(comment) = doc_comment {
            self.add_comment_with_attribute("param", "name", field_name, comment);
        }
        self.fields.push(format!("{field_type} {field_name}"));
        self
    }

    pub fn add_fields(&mut self, fields: &[&Field]) -> &mut Self {
        for field in fields {
            let type_string = field.data_type().field_type_string(&field.namespace());

            self.add_field(
                &field.field_name(),
                &type_string,
                field.formatted_param_doc_comment().as_deref(),
            );
        }

        self
    }
}

impl Builder for ContainerBuilder {
    fn build(&self) -> CodeBlock {
        let mut code = CodeBlock::default();

        for comment in &self.comments {
            code.writeln(&comment.to_string());
        }

        for attribute in &self.attributes {
            code.writeln(&format!("[{attribute}]"));
        }

        writeln!(
            code,
            "{container_type} {name}{fields}{bases}",
            container_type = self.container_type,
            name = self.name,
            bases = if self.bases.is_empty() {
                "".to_string()
            } else {
                format!(" : {bases}", bases = self.bases.join(", "))
            },
            fields = if self.fields.is_empty() {
                "".to_string()
            } else {
                format!("({})", self.fields.join(", "))
            },
        );

        let body_content: CodeBlock = self.contents.iter().cloned().collect();

        if body_content.is_empty() {
            code.writeln("{\n}");
        } else {
            writeln!(code, "{{\n    {body}\n}}", body = body_content.indent());
        }

        code
    }
}

impl AttributeBuilder for ContainerBuilder {
    fn add_attribute(&mut self, attribute: impl Into<String>) -> &mut Self {
        self.attributes.push(attribute.into());
        self
    }
}

impl CommentBuilder for ContainerBuilder {
    fn add_comment(&mut self, tag: &str, content: impl Into<String>) -> &mut Self {
        self.comments.push(CommentTag::new(tag, content.into()));
        self
    }

    fn add_comment_with_attribute(
        &mut self,
        tag: &str,
        attribute_name: &str,
        attribute_value: &str,
        content: impl Into<String>,
    ) -> &mut Self {
        self.comments.push(CommentTag::with_tag_attribute(
            tag,
            attribute_name,
            attribute_value,
            content.into(),
        ));
        self
    }

    fn add_comments(&mut self, comments: Vec<CommentTag>) -> &mut Self {
        self.comments.extend(comments);
        self
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
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
    base_arguments: Vec<String>,
    comments: Vec<CommentTag>,
    attributes: Vec<String>,
    function_type: FunctionType,
    inherit_doc: bool,
}

impl FunctionBuilder {
    pub fn new(access: &str, return_type: &str, name: &str, function_type: FunctionType) -> FunctionBuilder {
        FunctionBuilder {
            parameters: Vec::new(),
            access: access.to_owned(),
            name: name.to_owned(),
            return_type: return_type.to_owned(),
            body: CodeBlock::default(),
            comments: Vec::new(),
            attributes: Vec::new(),
            base_arguments: Vec::new(),
            function_type,
            inherit_doc: false,
        }
    }

    pub fn add_parameter(
        &mut self,
        param_type: &str,
        param_name: &str,
        default_value: Option<&str>,
        doc_comment: Option<String>,
    ) -> &mut Self {
        self.parameters.push(format!(
            "{param_type} {param_name}{default_value}",
            default_value = match default_value {
                Some(value) => format!(" = {value}"),
                None => "".to_string(),
            },
        ));

        if let Some(comment) = doc_comment {
            self.add_comment_with_attribute("param", "name", param_name, comment);
        }

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
            "global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)",
        );
        self
    }

    pub fn add_operation_parameters(&mut self, operation: &Operation, context: TypeContext) -> &mut Self {
        let parameters = operation.parameters();

        // Find an index such that all parameters after it are optional (but not streamed)
        // We compute this by finding the last parameter where all other parameters after it are
        // optional, and adding 1 to its index.
        // If we can't find one, that means all parameters were optional,
        // so we return 0 for this value.
        let trailing_optional_parameters_index = match parameters
            .iter()
            .rposition(|p| !p.data_type.is_optional || p.is_streamed)
        {
            Some(last_index) => last_index + 1,
            None => 0,
        };

        for (index, parameter) in parameters.iter().enumerate() {
            let parameter_type = parameter.cs_type_string(&operation.namespace(), context);
            let parameter_name = parameter.parameter_name();

            let default_value = if context == TypeContext::OutgoingParam && (index >= trailing_optional_parameters_index) {
                match parameter.data_type.concrete_typeref() {
                    // Sequences of fixed-size numeric types are mapped to `ReadOnlyMemory<T>` and have to use
                    // 'default' as their default value. Other optional types are mapped to nullable types and
                    // can use 'null' as the default value, which makes it clear what the default is.
                    TypeRefs::Sequence(sequence_ref)
                        if sequence_ref.has_fixed_size_primitive_elements()
                            && !sequence_ref.has_attribute::<CsType>() =>
                    {
                        Some("default")
                    }
                    _ => Some("null"),
                }
            } else {
                None
            };

            self.add_parameter(
                &parameter_type,
                &parameter_name,
                default_value,
                parameter.formatted_param_doc_comment(),
            );
        }

        match context {
            TypeContext::IncomingParam => {
                self.add_parameter(
                    "IceRpc.Features.IFeatureCollection",
                    &escape_parameter_name(&parameters, "features"),
                    None,
                    Some("The dispatch features.".to_owned()),
                );
            }
            TypeContext::OutgoingParam => {
                self.add_parameter(
                    "IceRpc.Features.IFeatureCollection?",
                    &escape_parameter_name(&parameters, "features"),
                    Some("null"),
                    Some("The invocation features.".to_owned()),
                );
            }
            _ => panic!("Unexpected context value"),
        }

        self.add_parameter(
            "global::System.Threading.CancellationToken",
            &escape_parameter_name(&parameters, "cancellationToken"),
            if context == TypeContext::OutgoingParam {
                Some("default")
            } else {
                None
            },
            Some("A cancellation token that receives the cancellation requests.".to_owned()),
        );

        if let Some(comment) = operation.comment() {
            // Generate documentation for any '@returns' tags on the operation.
            match comment.returns.as_slice() {
                // Do nothing if there's no return tags.
                [] => {}

                // If there's a single return tag, generate a normal `returns` message.
                [single] => {
                    let message = format_comment_message(&single.message, &operation.namespace());
                    self.add_comment("returns", message);
                }

                // If there's multiple return tags, this function returns a tuple.
                // We generate a returns message that lists the elements of the tuple as a bulleted list.
                multiple => {
                    let mut content = "A tuple containing:\n<list type=\"bullet\">\n".to_owned();
                    for return_tag in multiple {
                        // TODO add references to the types/identifiers here later!
                        let message = format_comment_message(&return_tag.message, &operation.namespace());
                        content = content + "<item><description>" + message.trim_end() + "</description></item>\n";
                    }
                    content += "</list>\n";

                    self.add_comment("returns", content);
                }
            }

            // Generate documentation for any '@throws' tags on the operation.
            for throws_tag in &comment.throws {
                let message = format_comment_message(&throws_tag.message, &operation.namespace());
                match throws_tag.thrown_type() {
                    Ok(exception) => {
                        let exception_name = exception.escape_scoped_identifier(&operation.namespace());
                        self.add_comment_with_attribute("exception", "cref", &exception_name, message);
                    }
                    Err(identifier) => {
                        // If there was an error resolving the link, print the identifier without any formatting.
                        self.add_comment_with_attribute("exception", "cref", &identifier.value, message);
                    }
                }
            }
        }

        // Generate documentation for "void" returns. This is only done for void operations
        // since they can't have '@returns' tags in their doc comments.
        if operation.return_type.is_empty() {
            let comment = match context {
                TypeContext::IncomingParam => "A value task that completes when this implementation completes.",
                TypeContext::OutgoingParam => "A task that completes when the response is received.",
                _ => unreachable!("Unexpected context value"),
            };
            self.add_comment("returns", comment);
        }

        self
    }
}

impl Builder for FunctionBuilder {
    fn build(&self) -> CodeBlock {
        let mut code = CodeBlock::default();

        if self.inherit_doc {
            code.writeln("/// <inheritdoc/>")
        } else {
            for comment in &self.comments {
                code.writeln(&comment.to_string());
            }
        }

        for attribute in &self.attributes {
            code.writeln(&format!("[{attribute}]"));
        }

        // CodeBlock ignores whitespace only writes so there's no issue of over-padding here.
        write!(code, "{} ", self.access);
        write!(code, "{} ", self.return_type);
        if self.parameters.len() > 1 {
            write!(
                code,
                "\
                {}(
    {})",
                self.name,
                CodeBlock::from(self.parameters.join(",\n")).indent(),
            );
        } else {
            write!(code, "{}({})", self.name, self.parameters.join(", "))
        }

        match self.base_arguments.as_slice() {
            [] => {}
            _ => write!(code, "\n    : base({})", self.base_arguments.join(", ")),
        }

        match self.function_type {
            FunctionType::Declaration => {
                code.writeln(";");
            }
            FunctionType::ExpressionBody => {
                if self.body.is_empty() {
                    code.writeln(" => {{}};");
                } else {
                    writeln!(code, " =>\n    {};", self.body.clone().indent());
                }
            }
            FunctionType::BlockBody => {
                if self.body.is_empty() {
                    code.writeln("\n{\n}");
                } else {
                    writeln!(code, "\n{{\n    {}\n}}", self.body.clone().indent());
                }
            }
        }

        code
    }
}

#[derive(Debug, Clone)]
pub struct FunctionCallBuilder {
    callable: String,
    type_argument: Option<String>,
    arguments: Vec<String>,
    arguments_on_newline: bool,
    use_semicolon: bool,
}

impl FunctionCallBuilder {
    pub fn new(callable: impl Into<String>) -> FunctionCallBuilder {
        FunctionCallBuilder {
            callable: callable.into(),
            type_argument: None,
            arguments: vec![],
            arguments_on_newline: false,
            use_semicolon: true,
        }
    }

    pub fn arguments_on_newline(&mut self, arguments_on_newline: bool) -> &mut Self {
        self.arguments_on_newline = arguments_on_newline;
        self
    }

    pub fn use_semicolon(&mut self, use_semicolon: bool) -> &mut Self {
        self.use_semicolon = use_semicolon;
        self
    }

    pub fn set_type_argument<T: ToString>(&mut self, type_argument: T) -> &mut Self {
        self.type_argument = Some(type_argument.to_string());
        self
    }

    pub fn add_argument<T: ToString>(&mut self, argument: T) -> &mut Self {
        self.arguments.push(argument.to_string());
        self
    }

    pub fn add_argument_if<T: ToString>(&mut self, condition: bool, argument: T) -> &mut Self {
        if condition {
            self.add_argument(argument);
        }
        self
    }

    pub fn add_argument_if_present<T: ToString>(&mut self, argument: Option<T>) -> &mut Self {
        if let Some(arg) = argument {
            self.add_argument(arg);
        }
        self
    }
}

impl Builder for FunctionCallBuilder {
    fn build(&self) -> CodeBlock {
        let type_arg = match &self.type_argument {
            Some(arg) => format!("<{arg}>"),
            None => "".to_owned(),
        };

        let mut function_call = if self.arguments_on_newline && !self.arguments.is_empty() {
            format!("{}{type_arg}(\n    {})", self.callable, self.arguments.join(",\n    "))
        } else {
            format!("{}{type_arg}({})", self.callable, self.arguments.join(", "))
        };

        if self.use_semicolon {
            function_call.push(';');
        }

        function_call.into()
    }
}

impl AttributeBuilder for FunctionBuilder {
    fn add_attribute(&mut self, attribute: impl Into<String>) -> &mut Self {
        self.attributes.push(attribute.into());
        self
    }
}

impl CommentBuilder for FunctionBuilder {
    fn add_comment(&mut self, tag: &str, content: impl Into<String>) -> &mut Self {
        self.comments.push(CommentTag::new(tag, content.into()));
        self
    }

    fn add_comment_with_attribute(
        &mut self,
        tag: &str,
        attribute_name: &str,
        attribute_value: &str,
        content: impl Into<String>,
    ) -> &mut Self {
        self.comments.push(CommentTag::with_tag_attribute(
            tag,
            attribute_name,
            attribute_value,
            content.into(),
        ));
        self
    }

    fn add_comments(&mut self, comments: Vec<CommentTag>) -> &mut Self {
        self.comments.extend(comments);
        self
    }
}

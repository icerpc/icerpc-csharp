use crate::builders::{
    AttributeBuilder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType,
};
use crate::code_block::CodeBlock;
use crate::comments::doc_comment_message;
use crate::comments::operation_parameter_doc_comment;
use crate::cs_util::*;
use crate::decoding::*;
use crate::encoded_result::encoded_result_struct;
use crate::encoding::*;
use crate::generated_code::GeneratedCode;
use crate::member_util::escape_parameter_name;
use crate::slicec_ext::*;
use slice::code_gen_util::*;
use slice::grammar::*;
use slice::visitor::Visitor;

pub struct DispatchVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for DispatchVisitor<'_> {
    fn visit_interface_start(&mut self, interface_def: &Interface) {
        let bases = interface_def.base_interfaces();
        let interface_name = interface_def.interface_name();
        let access = interface_def.access_modifier();
        let mut interface_builder =
            ContainerBuilder::new(&format!("{} partial interface", access), &interface_name);

        let summary_comment = format!(
            r#"Interface used to implement services for Slice interface {}. <seealso cref="{}"/>.
{}"#,
            interface_def.identifier(),
            interface_def.proxy_name(),
            doc_comment_message(interface_def)
        );

        interface_builder
            .add_comment("summary", &summary_comment)
            .add_type_id_attribute(interface_def)
            .add_container_attributes(interface_def);

        interface_builder.add_bases(&bases.iter().map(|b| b.interface_name()).collect::<Vec<_>>());

        interface_builder
            .add_block(request_class(interface_def))
            .add_block(response_class(interface_def));

        interface_builder.add_block(
            format!(
                "\
private static readonly IActivator _defaultActivator =
    SliceDecoder.GetActivator(typeof({}).Assembly);",
                interface_name
            )
            .into(),
        );

        for operation in interface_def.operations() {
            if operation.has_encoded_result() {
                interface_builder.add_block(encoded_result_struct(operation));
            }
            interface_builder.add_block(operation_declaration(operation));
        }

        for operation in interface_def.operations() {
            interface_builder.add_block(operation_dispatch(operation));
        }

        self.generated_code
            .insert_scoped(interface_def, interface_builder.build().into());
    }
}

fn request_class(interface_def: &Interface) -> CodeBlock {
    let bases = interface_def.base_interfaces();
    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| !o.parameters.is_empty())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let access = interface_def.access_modifier();
    let mut class_builder = ContainerBuilder::new(
        &if bases.is_empty() {
            format!("{} static class", access)
        } else {
            format!("{} static new class", access)
        },
        "Request",
    );

    class_builder.add_comment(
        "summary",
        "Provides static methods that read the arguments of requests.",
    );

    for operation in operations {
        let parameters = operation.parameters();

        let namespace = &operation.namespace();

        let function_type = if operation.streamed_parameter().is_some() {
            FunctionType::BlockBody
        } else {
            FunctionType::ExpressionBody
        };

        // We need the async/await for proper type inference when returning tuples with nullable elements like string?.
        let mut builder = FunctionBuilder::new(
            &format!("{} static async", access),
            &format!(
                "global::System.Threading.Tasks.ValueTask<{}>",
                &parameters.to_tuple_type(namespace, TypeContext::Decode, false)
            ),
            &operation.escape_identifier_with_suffix("Async"),
            function_type,
        );

        builder.add_comment(
            "summary",
            &format!(
                "Decodes the argument{s} of operation {operation_identifier}.",
                s = if parameters.len() == 1 { "" } else { "s" },
                operation_identifier = operation.escape_identifier()
            ),
        );

        builder.add_parameter("IceRpc.IncomingRequest", "request", None, None);
        builder.add_parameter(
            "global::System.Threading.CancellationToken",
            "cancel",
            None,
            None,
        );
        builder.set_body(request_decode_body(operation));

        class_builder.add_block(builder.build());
    }

    class_builder.build().into()
}

fn response_class(interface_def: &Interface) -> CodeBlock {
    let bases = interface_def.base_interfaces();

    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| o.has_nonstreamed_return_members())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let access = interface_def.access_modifier();
    let mut class_builder = ContainerBuilder::new(
        &if bases.is_empty() {
            format!("{} static class", access)
        } else {
            format!("{} static new class", access)
        },
        "Response",
    );

    class_builder.add_comment(
        "summary",
        "Provides static methods that write the return values of responses.",
    );

    for operation in operations {
        let non_streamed_returns = operation.nonstreamed_return_members();

        let namespace = &operation.namespace();
        let operation_name = &operation.escape_identifier();
        let returns_classes = operation.returns_classes();
        let mut builder = FunctionBuilder::new(
            &format!("{} static", access),
            "global::System.IO.Pipelines.PipeReader",
            operation_name,
            FunctionType::BlockBody,
        );

        builder
            .add_comment(
                "summary",
                &format!(
                    "Creates a response payload for operation {}.",
                    &operation_name
                ),
            )
            .add_comment("returns", "A new response payload.");

        let encoding = escape_parameter_name(&non_streamed_returns, "encoding");
        if !returns_classes {
            builder.add_parameter(
                "SliceEncoding",
                &encoding,
                None,
                Some("The encoding of the payload"),
            );
        }

        match non_streamed_returns.as_slice() {
            [param] => {
                builder.add_parameter(
                    &param.to_type_string(namespace, TypeContext::Encode, false),
                    "returnValue",
                    None,
                    Some("The operation return value"),
                );
            }
            _ => {
                for param in &non_streamed_returns {
                    builder.add_parameter(
                        &param.to_type_string(namespace, TypeContext::Encode, false),
                        &param.parameter_name(),
                        None,
                        operation_parameter_doc_comment(operation, param.identifier()),
                    );
                }
            }
        }

        builder.set_body(
            format!(
                "\
var pipe_ = new global::System.IO.Pipelines.Pipe(); // TODO: pipe options

var encoder_ = new SliceEncoder(pipe_.Writer, {encoding}, {class_format});
Span<byte> sizePlaceholder_ = encoder_.GetPlaceholderSpan(4);
int startPos_ = encoder_.EncodedByteCount;
{encode_returns}
SliceEncoder.EncodeVarULong((ulong)(encoder_.EncodedByteCount - startPos_), sizePlaceholder_);

pipe_.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
return pipe_.Reader;",
                encoding = if returns_classes {
                    "IceRpc.Encoding.Slice11"
                } else {
                    &encoding
                },
                class_format = operation.format_type(),
                encode_returns = encode_operation(operation, true, "encoder_")
            )
            .into(),
        );

        class_builder.add_block(builder.build());
    }

    class_builder.build().into()
}

fn request_decode_body(operation: &Operation) -> CodeBlock {
    let mut code = CodeBlock::new();

    let namespace = &operation.namespace();

    if let Some(stream_member) = operation.streamed_parameter() {
        let non_streamed_parameters = operation.nonstreamed_parameters();

        if non_streamed_parameters.is_empty() {
            writeln!(
                code,
                "\
await request.CheckEmptyArgsAsync(hasStream: true, cancel).ConfigureAwait(false);

return {}",
                decode_operation_stream(stream_member, namespace, true, false)
            );
        } else {
            writeln!(
                code,
                "\
var {args} = await request.ToArgsAsync(
    _defaultActivator,
    {decode_func},
    hasStream: true,
    cancel).ConfigureAwait(false);

{decode_request_stream}

return {args_and_stream};",
                args = non_streamed_parameters.to_argument_tuple("sliceP_"),
                decode_func = request_decode_func(operation).indent(),
                decode_request_stream =
                    decode_operation_stream(stream_member, namespace, true, true,),
                args_and_stream = operation.parameters().to_argument_tuple("sliceP_")
            );
        }
    } else {
        writeln!(
            code,
            "\
await request.ToArgsAsync(
    _defaultActivator,
    {decode_func},
    hasStream: false,
    cancel).ConfigureAwait(false)
",
            decode_func = request_decode_func(operation).indent()
        );
    }

    code
}

fn request_decode_func(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();

    let parameters = operation.nonstreamed_parameters();
    assert!(!parameters.is_empty());

    let use_default_decode_func = parameters.len() == 1
        && get_bit_sequence_size(&parameters) == 0
        && parameters.first().unwrap().tag.is_none();

    // TODO: simplify code for single stream param

    if use_default_decode_func {
        let param = parameters.first().unwrap();
        decode_func(param.data_type(), namespace)
    } else {
        format!(
            "(ref SliceDecoder decoder) =>
{{
    {}
}}",
            decode_operation(operation, true).indent()
        )
        .into()
    }
}

fn operation_declaration(operation: &Operation) -> CodeBlock {
    FunctionBuilder::new(
        &operation.parent().unwrap().access_modifier(),
        &operation.return_task(true),
        &(operation.escape_identifier_with_suffix("Async")),
        FunctionType::Declaration,
    )
    .add_comment("summary", &doc_comment_message(operation))
    .add_operation_parameters(operation, TypeContext::Decode)
    .add_container_attributes(operation)
    .build()
}

fn operation_dispatch(operation: &Operation) -> CodeBlock {
    let operation_name = &operation.escape_identifier();
    let internal_name = format!("SliceD{}Async", &operation_name);

    format!(
        r#"
[IceRpc.Slice.Operation("{name}")]
protected static async global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> {internal_name}(
    {interface_name} target,
    IceRpc.IncomingRequest request,
    global::System.Threading.CancellationToken cancel)
{{
    {dispatch_body}
}}
"#,
        name = operation.identifier(),
        internal_name = internal_name,
        interface_name = operation.parent().unwrap().interface_name(),
        dispatch_body = operation_dispatch_body(operation).indent()
    )
    .into()
}

fn operation_dispatch_body(operation: &Operation) -> CodeBlock {
    let async_operation_name = &operation.escape_identifier_with_suffix("Async");
    let parameters = operation.parameters();
    let return_parameters = operation.return_members();

    let mut code = CodeBlock::new();

    if !operation.is_idempotent {
        code.writeln("request.CheckNonIdempotent();");
    }

    if operation.compress_return() {
        code.writeln(
            "request.Features = request.Features.With(IceRpc.Features.CompressPayload.Yes);",
        );
    }

    // temporary way to "compute" the Slice encoding
    let encoding = "request.GetSliceEncoding()";

    match parameters.as_slice() {
        [] => {
            // Verify the payload is indeed empty (it can contain tagged params that we have to skip).
            code.writeln(
                "\
await request.CheckEmptyArgsAsync(hasStream: false, cancel).ConfigureAwait(false);",
            );
        }
        [parameter] => {
            writeln!(
                code,
                "var {var_name} = await Request.{async_operation_name}(request, cancel).ConfigureAwait(false);",
                var_name = parameter.parameter_name_with_prefix("sliceP_"),
                async_operation_name = async_operation_name,
            )
        }
        _ => {
            // > 1 parameter
            writeln!(
                code,
                "var args = await Request.{async_operation_name}(request, cancel).ConfigureAwait(false);",
                async_operation_name = async_operation_name,
            )
        }
    };

    if operation.has_encoded_result() {
        // TODO: support for stream param with encoded result?
        let mut args = vec![];

        match parameters.as_slice() {
            [p] => {
                args.push(p.parameter_name_with_prefix("sliceP_"));
            }
            _ => {
                for p in parameters {
                    args.push("args.".to_owned() + &p.field_name(FieldType::NonMangled));
                }
            }
        }

        args.push("new IceRpc.Slice.Dispatch(request)".to_owned());
        args.push("cancel".to_owned());

        writeln!(
            code,
            "var returnValue = await target.{name}({args}).ConfigureAwait(false);",
            name = async_operation_name,
            args = args.join(", ")
        );

        writeln!(
            code,
            "return new IceRpc.OutgoingResponse(request) {{ PayloadSource = returnValue.Payload }};"
        );
    } else {
        let mut args = match parameters.as_slice() {
            [parameter] => vec![parameter.parameter_name_with_prefix("sliceP_")],
            _ => parameters
                .iter()
                .map(|parameter| format!("args.{}", &parameter.field_name(FieldType::NonMangled)))
                .collect(),
        };
        args.push("new IceRpc.Slice.Dispatch(request)".to_owned());
        args.push("cancel".to_owned());

        writeln!(
            code,
            "{return_value}await target.{async_operation_name}({args}).ConfigureAwait(false);",
            return_value = if !return_parameters.is_empty() {
                "var returnValue = "
            } else {
                ""
            },
            async_operation_name = async_operation_name,
            args = args.join(", ")
        );

        writeln!(
            code,
            "return new IceRpc.OutgoingResponse(request) {{ PayloadSource = {payload_source}, PayloadSourceStream = {payload_source_stream} }};",
            payload_source = dispatch_return_payload(operation, encoding),
            payload_source_stream = payload_source_stream(operation, encoding)
        );
    }

    code
}

fn dispatch_return_payload(operation: &Operation, encoding: &str) -> CodeBlock {
    let non_streamed_return_values = operation.nonstreamed_return_members();

    let mut returns = vec![];

    if !operation.returns_classes() {
        returns.push(encoding.to_owned());
    }

    returns.push(match operation.return_members().len() {
        1 => "returnValue".to_owned(),
        _ => non_streamed_return_values
            .iter()
            .map(|r| format!("returnValue.{}", &r.field_name(FieldType::NonMangled)))
            .collect::<Vec<_>>()
            .join(", "),
    });

    match non_streamed_return_values.len() {
        0 => format!(
            "{encoding}.CreateEmptyPayload(hasStream: {has_stream})",
            encoding = encoding,
            has_stream = if operation.return_type.is_empty() {
                "false"
            } else {
                "true"
            }
        ),
        _ => format!(
            "Response.{operation_name}({args})",
            operation_name = operation.escape_identifier(),
            args = returns.join(", ")
        ),
    }
    .into()
}

fn payload_source_stream(operation: &Operation, encoding: &str) -> CodeBlock {
    let namespace = &operation.namespace();
    let return_values = operation.return_members();

    match operation.streamed_return_member() {
        None => "null".into(),
        Some(stream_return) => {
            let stream_type = stream_return.data_type();

            let stream_arg = if return_values.len() == 1 {
                "returnValue".to_owned()
            } else {
                format!(
                    "returnValue.{}",
                    &stream_return.field_name(FieldType::NonMangled)
                )
            };

            match stream_type.concrete_type() {
                Types::Primitive(primitive) if matches!(primitive, Primitive::Byte) => {
                    stream_arg.into()
                }
                _ => format!(
                    "\
{encoding}.CreatePayloadSourceStream<{stream_type}>(
    {stream_arg},
    {encode_action})",
                    stream_type = stream_type.to_type_string(namespace, TypeContext::Encode, false),
                    stream_arg = stream_arg,
                    encoding = encoding,
                    encode_action =
                        encode_action(stream_type, TypeContext::Encode, namespace).indent(),
                )
                .into(),
            }
        }
    }
}

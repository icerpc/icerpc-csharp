// Copyright (c) ZeroC, Inc.

use crate::builders::{AttributeBuilder, Builder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType};
use crate::code_block::CodeBlock;
use crate::code_gen_util::TypeContext;
use crate::cs_attributes::CsEncodedReturn;
use crate::decoding::*;
use crate::encoding::*;
use crate::slicec_ext::*;
use slicec::grammar::*;

pub fn generate_dispatch(interface_def: &Interface) -> CodeBlock {
    let namespace = interface_def.namespace();
    let bases = interface_def.base_interfaces();
    let service_name = interface_def.service_name();
    let access = interface_def.access_modifier();
    let mut interface_builder = ContainerBuilder::new(&format!("{access} partial interface"), &service_name);

    if let Some(summary) = interface_def.formatted_doc_comment_summary() {
        interface_builder.add_comment("summary", summary);
    }

    interface_builder
        .add_generated_remark_with_note(
            "server-side interface",
            r#"Your service implementation must implement this interface."#,
            interface_def,
        )
        .add_comments(interface_def.formatted_doc_comment_seealso())
        .add_type_id_attribute(interface_def)
        .add_default_service_path_attribute(interface_def);

    interface_builder.add_bases(
        &bases
            .iter()
            .map(|b| b.scoped_service_name(&namespace))
            .collect::<Vec<_>>(),
    );

    interface_builder
        .add_block(request_class(interface_def))
        .add_block(response_class(interface_def));

    if interface_def.supported_encodings().supports(Encoding::Slice1) {
        interface_builder.add_block(
            format!(
                "\
private static readonly IActivator _defaultActivator =
    IActivator.FromAssembly(typeof({service_name}).Assembly);"
            )
            .into(),
        );
    }

    // TODO: add a Slice cs attribute to conditionally suppress the generation of these methods.
    if interface_def.module_scoped_identifier() != "Ice::Object" {
        for operation in interface_def.operations() {
            interface_builder.add_block(operation_declaration(operation));
        }
    }

    for operation in interface_def.operations() {
        interface_builder.add_block(operation_dispatch(operation));
    }

    interface_builder.build()
}

fn request_class(interface_def: &Interface) -> CodeBlock {
    let bases = interface_def.base_interfaces();

    let mut operations = interface_def.operations();
    operations.retain(|o| !o.parameters.is_empty());

    if operations.is_empty() {
        return "".into();
    }

    let mut class_builder = ContainerBuilder::new(
        if bases.is_empty() {
            "public static class"
        } else {
            "public static new class"
        },
        "Request",
    );

    class_builder
        .add_comment("summary", "Provides static methods that decode request payloads.")
        .add_generated_remark("static class", interface_def);

    for operation in operations {
        let parameters = operation.parameters();

        let namespace = &operation.namespace();

        let function_type = if operation.streamed_parameter().is_some() {
            FunctionType::BlockBody
        } else {
            FunctionType::ExpressionBody
        };

        let mut builder = FunctionBuilder::new(
            if function_type == FunctionType::BlockBody {
                "public static async"
            } else {
                "public static"
            },
            &format!(
                "global::System.Threading.Tasks.ValueTask<{}>",
                &parameters.to_tuple_type(namespace, TypeContext::IncomingParam),
            ),
            &operation.escape_identifier_with_prefix_and_suffix("Decode", "Async"),
            function_type,
        );

        builder.add_comment(
            "summary",
            format!(
                r#"Decodes the request payload of operation <c>{operation_identifier}</c>."#,
                operation_identifier = operation.identifier(),
            ),
        );

        builder.add_parameter(
            "IceRpc.IncomingRequest",
            "request",
            None,
            Some("The incoming request.".to_owned()),
        );
        builder.add_parameter(
            "global::System.Threading.CancellationToken",
            "cancellationToken",
            None,
            Some("A cancellation token that receives the cancellation requests.".to_owned()),
        );
        builder.set_body(request_decode_body(operation));

        class_builder.add_block(builder.build());
    }

    class_builder.build()
}

fn response_class(interface_def: &Interface) -> CodeBlock {
    let bases = interface_def.base_interfaces();

    let mut operations = interface_def.operations();
    operations.retain(|o| o.has_non_streamed_return_members());

    if operations.is_empty() {
        return "".into();
    }

    let mut class_builder = ContainerBuilder::new(
        if bases.is_empty() {
            "public static class"
        } else {
            "public static new class"
        },
        "Response",
    );

    class_builder
        .add_comment(
            "summary",
            "Provides static methods that encode return values into response payloads.",
        )
        .add_generated_remark("static class", interface_def);

    for operation in operations {
        let non_streamed_returns = operation.non_streamed_return_members();

        let namespace = &operation.namespace();
        let operation_name = &operation.escape_identifier();

        let mut builder = FunctionBuilder::new(
            "public static",
            "global::System.IO.Pipelines.PipeReader",
            format!("Encode{operation_name}").as_str(),
            FunctionType::BlockBody,
        );

        builder.add_comment(
            "summary",
            format!(
                "Encodes the return value of operation <c>{}</c> into a response payload.",
                operation.identifier(),
            ),
        );

        match non_streamed_returns.as_slice() {
            [param] => {
                builder.add_parameter(
                    &param.data_type().outgoing_parameter_type_string(namespace),
                    "returnValue",
                    None,
                    Some("The operation return value.".to_owned()),
                );
            }
            _ => {
                for param in &non_streamed_returns {
                    builder.add_parameter(
                        &param.data_type().outgoing_parameter_type_string(namespace),
                        &param.parameter_name(),
                        None,
                        param.formatted_param_doc_comment(),
                    );
                }
            }
        }

        builder.add_parameter(
            "SliceEncodeOptions?",
            "encodeOptions",
            Some("null"),
            Some("The Slice encode options.".to_owned()),
        );

        builder.add_comment("returns", "A new response payload.");

        builder.set_body(encode_operation(operation, true));

        class_builder.add_block(builder.build());
    }

    class_builder.build()
}

fn request_decode_body(operation: &Operation) -> CodeBlock {
    let mut code = CodeBlock::default();

    let non_streamed_parameters = operation.non_streamed_parameters();
    let namespace = &operation.namespace();
    let encoding = operation.encoding;

    if let Some(stream_member) = operation.streamed_parameter() {
        if non_streamed_parameters.is_empty() {
            writeln!(
                code,
                "await request.DecodeEmptyArgsAsync({encoding}, cancellationToken).ConfigureAwait(false);",
                encoding = encoding.to_cs_encoding(),
            );

            let stream_type = stream_member.data_type();
            match stream_type.concrete_type() {
                Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => {
                    writeln!(code, "return IceRpc.IncomingFrameExtensions.DetachPayload(request);");
                }
                _ => {
                    writeln!(
                        code,
                        "\
var payloadContinuation = IceRpc.IncomingFrameExtensions.DetachPayload(request);
return {}",
                        decode_operation_stream(stream_member, namespace, encoding, true)
                    )
                }
            }
        } else {
            writeln!(
                code,
                "\
var {args} = await request.DecodeArgsAsync(
    {encoding},
    {decode_func},
    defaultActivator: null,
    cancellationToken).ConfigureAwait(false);",
                args = non_streamed_parameters.to_argument_tuple(),
                encoding = encoding.to_cs_encoding(),
                decode_func = decode_non_streamed_parameters_func(&non_streamed_parameters, encoding).indent(),
            );
            let stream_type = stream_member.data_type();
            match stream_type.concrete_type() {
                Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => {
                    writeln!(
                        code,
                        "var {} = IceRpc.IncomingFrameExtensions.DetachPayload(request);",
                        stream_member.parameter_name_with_prefix(),
                    )
                }
                _ => writeln!(
                    code,
                    "\
var payloadContinuation = IceRpc.IncomingFrameExtensions.DetachPayload(request);
var {stream_parameter_name} = {decode_operation_stream}
",
                    stream_parameter_name = stream_member.parameter_name_with_prefix(),
                    decode_operation_stream = decode_operation_stream(stream_member, namespace, encoding, true),
                ),
            }
            writeln!(code, "return {};", operation.parameters().to_argument_tuple());
        }
    } else {
        writeln!(
            code,
            "\
request.DecodeArgsAsync(
    {encoding},
    {decode_func},
    defaultActivator: {default_activator},
    cancellationToken)
",
            encoding = encoding.to_cs_encoding(),
            decode_func = decode_non_streamed_parameters_func(&non_streamed_parameters, encoding).indent(),
            default_activator = default_activator(encoding),
        );
    }
    code
}

fn operation_declaration(operation: &Operation) -> CodeBlock {
    let mut builder = FunctionBuilder::new(
        "public",
        &operation.dispatch_return_task(),
        &operation.escape_identifier_with_suffix("Async"),
        FunctionType::Declaration,
    );
    if let Some(summary) = operation.formatted_doc_comment_summary() {
        builder.add_comment("summary", summary);
    }
    builder
        .add_operation_parameters(operation, TypeContext::IncomingParam)
        .add_comments(operation.formatted_doc_comment_seealso())
        .build()
}

fn operation_dispatch(operation: &Operation) -> CodeBlock {
    let operation_name = &operation.escape_identifier();
    let internal_name = format!("SliceD{}Async", &operation_name);

    format!(
        r#"
[SliceOperation("{name}")]
[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]
protected static async global::System.Threading.Tasks.ValueTask<IceRpc.OutgoingResponse> {internal_name}(
    {service_name} target,
    IceRpc.IncomingRequest request,
    global::System.Threading.CancellationToken cancellationToken)
{{
    {dispatch_body}
}}
"#,
        name = operation.identifier(),
        service_name = operation.parent().service_name(),
        dispatch_body = operation_dispatch_body(operation).indent(),
    )
    .into()
}

fn operation_dispatch_body(operation: &Operation) -> CodeBlock {
    let async_operation_name = &operation.escape_identifier_with_suffix("Async");
    let parameters = operation.parameters();
    let return_parameters = operation.return_members();

    let mut check_and_decode = CodeBlock::default();

    if !operation.is_idempotent {
        check_and_decode.writeln("request.CheckNonIdempotent();");
    }

    if operation.compress_return() {
        check_and_decode.writeln(
            "\
request.Features = IceRpc.Features.FeatureCollectionExtensions.With(
    request.Features,
    IceRpc.Features.CompressFeature.Compress);
            ",
        )
    }

    let encoding = operation.encoding.to_cs_encoding();

    match parameters.as_slice() {
        [] => {
            // Verify the payload is indeed empty (it can contain tagged params that we have to
            // skip).
            writeln!(
                check_and_decode,
                "\
await request.DecodeEmptyArgsAsync({encoding}, cancellationToken).ConfigureAwait(false);",
            );
        }
        [parameter] => {
            writeln!(
                check_and_decode,
                "var {var_name} = await Request.Decode{async_operation_name}(request, cancellationToken).ConfigureAwait(false);",
                var_name = parameter.parameter_name_with_prefix(),
            )
        }
        _ => {
            // > 1 parameter
            writeln!(
                check_and_decode,
                "var args = await Request.Decode{async_operation_name}(request, cancellationToken).ConfigureAwait(false);",
            )
        }
    };

    let mut dispatch_and_return = CodeBlock::default();

    let mut args = match parameters.as_slice() {
        [parameter] => vec![parameter.parameter_name_with_prefix()],
        _ => parameters
            .into_iter()
            .map(|parameter| "args.".to_owned() + &parameter.field_name())
            .collect(),
    };
    args.push("request.Features".to_owned());
    args.push("cancellationToken".to_owned());
    writeln!(
        dispatch_and_return,
        "{return_value}await target.{async_operation_name}({args}).ConfigureAwait(false);",
        return_value = if !return_parameters.is_empty() {
            "var returnValue = "
        } else {
            ""
        },
        args = args.join(", "),
    );

    #[allow(clippy::collapsible_else_if)] // We preserve the 'if' and 'else' blocks because they're symmetric.
    if operation.has_attribute::<CsEncodedReturn>() {
        if operation.streamed_return_member().is_none() {
            writeln!(
                dispatch_and_return,
                "return new IceRpc.OutgoingResponse(request) {{ Payload = returnValue }};",
            );
        } else {
            writeln!(
                dispatch_and_return,
                "\
return new IceRpc.OutgoingResponse(request)
{{
    Payload = returnValue.Payload,
    PayloadContinuation = {payload_continuation}
}};",
                payload_continuation = payload_continuation(operation, encoding).indent(),
            );
        }
    } else {
        if operation.return_type.is_empty() {
            writeln!(dispatch_and_return, "return new IceRpc.OutgoingResponse(request);")
        } else {
            writeln!(
                dispatch_and_return,
                "\
return new IceRpc.OutgoingResponse(request)
{{
    Payload = {payload},
    PayloadContinuation = {payload_continuation}
}};",
                payload = dispatch_return_payload(operation, encoding),
                payload_continuation = payload_continuation(operation, encoding).indent(),
            );
        }
    }

    let mut code = CodeBlock::default();
    writeln!(code, "{check_and_decode}");

    if operation.exception_specification.is_empty() {
        writeln!(code, "{dispatch_and_return}");
    } else {
        let catch_expression = match operation.exception_specification.as_slice() {
            [] => unreachable!(),
            [single_exception] => {
                let exception = single_exception.escape_scoped_identifier(&operation.namespace());
                format!("{exception} sliceException")
            }
            multiple_exceptions => {
                let exceptions = multiple_exceptions.iter();
                let cs_exceptions = exceptions.map(|ex| ex.escape_scoped_identifier(&operation.namespace()));
                let exception_list = cs_exceptions.collect::<Vec<_>>().join(" or ");
                format!("SliceException sliceException) when (sliceException is {exception_list}")
            }
        };
        write!(
            code,
            "
try
{{
    {dispatch_and_return}
}}
catch ({catch_expression})
{{
    return request.CreateSliceExceptionResponse(sliceException, {encoding});
}}",
            dispatch_and_return = dispatch_and_return.indent(),
        );
    }
    code
}

// only called for non-void operations
fn dispatch_return_payload(operation: &Operation, encoding: &str) -> CodeBlock {
    let non_streamed_return_values = operation.non_streamed_return_members();

    let mut returns = vec![];

    returns.push(match operation.return_members().len() {
        1 => "returnValue".to_owned(),
        _ => non_streamed_return_values
            .iter()
            .map(|r| format!("returnValue.{}", &r.field_name()))
            .collect::<Vec<_>>()
            .join(", "),
    });

    match non_streamed_return_values.len() {
        0 => format!("{encoding}.CreateEmptyStructPayload()"),
        _ => format!(
            "Response.Encode{operation_name}({args}, request.Features.Get<ISliceFeature>()?.EncodeOptions)",
            operation_name = operation.escape_identifier(),
            args = returns.join(", "),
        ),
    }
    .into()
}

fn payload_continuation(operation: &Operation, encoding: &str) -> CodeBlock {
    let namespace = &operation.namespace();
    let return_values = operation.return_members();
    match operation.streamed_return_member() {
        None => "null".into(),
        Some(stream_return) => {
            let stream_type = stream_return.data_type();

            let stream_arg = if return_values.len() == 1 {
                "returnValue".to_owned()
            } else {
                format!("returnValue.{}", &stream_return.field_name())
            };

            match stream_type.concrete_type() {
                Types::Primitive(Primitive::UInt8) if !stream_type.is_optional => stream_arg.into(),
                _ => format!(
                    "\
{stream_arg}.ToPipeReader(
    {encode_stream_parameter},
    {use_segments},
    {encoding},
    {encode_options})",
                    encode_stream_parameter =
                        encode_stream_parameter(stream_type, namespace, operation.encoding).indent(),
                    use_segments = stream_type.fixed_wire_size().is_none(),
                    encode_options = "request.Features.Get<ISliceFeature>()?.EncodeOptions",
                )
                .into(),
            }
        }
    }
}

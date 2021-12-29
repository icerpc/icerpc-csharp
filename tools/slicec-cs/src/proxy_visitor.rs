// Copyright (c) ZeroC, Inc. All rights reserved.

use crate::builders::{
    AttributeBuilder, CommentBuilder, ContainerBuilder, FunctionBuilder, FunctionType,
};
use crate::code_block::CodeBlock;
use crate::comments::*;
use crate::decoding::*;
use crate::encoding::*;
use crate::generated_code::GeneratedCode;
use crate::member_util::*;
use crate::slicec_ext::*;
use slice::code_gen_util::*;
use slice::grammar::*;
use slice::visitor::Visitor;

pub struct ProxyVisitor<'a> {
    pub generated_code: &'a mut GeneratedCode,
}

impl<'a> Visitor for ProxyVisitor<'_> {
    fn visit_interface_start(&mut self, interface_def: &Interface) {
        let namespace = interface_def.namespace();
        let prx_interface = interface_def.proxy_name(); // IFooPrx
        let prx_impl: String = interface_def.proxy_implementation_name(); // FooPrx
        let access = interface_def.access_modifier();
        let all_bases: Vec<&Interface> = interface_def.all_base_interfaces();
        let bases: Vec<&Interface> = interface_def.base_interfaces();

        let mut prx_impl_bases: Vec<String> = vec![prx_interface.clone(), "IPrx".to_owned()];

        let mut all_base_impl: Vec<String> = all_bases
            .iter()
            .map(|b| b.scoped_proxy_implementation_name(&namespace))
            .collect();

        let add_service_prx = !(all_bases
            .iter()
            .any(|b| b.module_scoped_identifier() == "IceRpc::Service")
            || interface_def.module_scoped_identifier() == "IceRpc::Service");

        if add_service_prx {
            prx_impl_bases.push("IceRpc.IServicePrx".to_owned());
            all_base_impl.push("IceRpc.ServicePrx".to_owned());
        }

        // prx bases
        let prx_bases: Vec<String> = bases
            .into_iter()
            .map(|b| b.scoped_proxy_name(&namespace))
            .collect();

        let summary_message = format!(
            r#"The client-side interface for Slice interface {}. <seealso cref="{}"/>.
{}"#,
            interface_def.identifier(),
            interface_def.interface_name(),
            doc_comment_message(interface_def)
        );

        let mut code = CodeBlock::new();
        code.add_block(
            &ContainerBuilder::new(&format!("{} partial interface", access), &prx_interface)
                .add_comment("summary", &summary_message)
                .add_type_id_attribute(interface_def)
                .add_container_attributes(interface_def)
                .add_bases(&prx_bases)
                .add_block(proxy_interface_operations(interface_def))
                .build(),
        );

        let mut proxy_impl_builder = ContainerBuilder::new(
            &format!("{} readonly partial record struct", access),
            &prx_impl,
        );

        proxy_impl_builder.add_bases(&prx_impl_bases)
            .add_comment("summary", &format!(r#"Typed proxy record struct. It implements <see cref="{}"/> by sending requests to a remote IceRPC service."#, prx_interface))
            .add_type_id_attribute(interface_def)
            .add_container_attributes(interface_def)
            .add_block(request_class(interface_def))
            .add_block(response_class(interface_def))
            .add_block(format!(r#"
/// <summary>The default path for services that implement Slice interface <c>{interface_name}</c>.</summary>
{access} static readonly string DefaultPath = typeof({prx_impl}).GetDefaultPath();

private static readonly IActivator _defaultActivator =
    IceDecoder.GetActivator(typeof({prx_impl}).Assembly);

/// <summary>The proxy to the remote service.</summary>
{access} IceRpc.Proxy Proxy {{ get; init; }}"#,
                               access = access,
                               interface_name = interface_def.identifier(),
                               prx_impl = prx_impl
            ).into());

        for base_impl in all_base_impl {
            proxy_impl_builder.add_block(
                format!(
                    r#"
/// <summary>Implicit conversion to <see cref="{base_impl}"/>.</summary>
{access} static implicit operator {base_impl}({prx_impl} prx) => new(prx.Proxy);"#,
                    access = access,
                    base_impl = base_impl,
                    prx_impl = prx_impl
                )
                .into(),
            );
        }

        proxy_impl_builder.add_block(proxy_impl_static_methods(interface_def));

        if add_service_prx {
            proxy_impl_builder.add_block(
                format!(
                    "\
/// <inheritdoc/>
{access} global::System.Threading.Tasks.Task<string[]> IceIdsAsync(
    IceRpc.Invocation? invocation = null,
    global::System.Threading.CancellationToken cancel = default) =>
    new IceRpc.ServicePrx(Proxy).IceIdsAsync(invocation, cancel);

/// <inheritdoc/>
{access} global::System.Threading.Tasks.Task<bool> IceIsAAsync(
    string id,
    IceRpc.Invocation? invocation = null,
    global::System.Threading.CancellationToken cancel = default) =>
    new IceRpc.ServicePrx(Proxy).IceIsAAsync(id, invocation, cancel);

/// <inheritdoc/>
{access} global::System.Threading.Tasks.Task IcePingAsync(
    IceRpc.Invocation? invocation = null,
    global::System.Threading.CancellationToken cancel = default) =>
    new IceRpc.ServicePrx(Proxy).IcePingAsync(invocation, cancel);",
                    access = access
                )
                .into(),
            );
        }

        for operation in interface_def.all_inherited_operations() {
            proxy_impl_builder.add_block(proxy_base_operation_impl(operation));
        }

        for operation in interface_def.operations() {
            proxy_impl_builder.add_block(proxy_operation_impl(operation));
        }

        code.add_block(&proxy_impl_builder.build());

        self.generated_code.insert_scoped(interface_def, code)
    }
}

fn proxy_impl_static_methods(interface_def: &Interface) -> CodeBlock {
    let access = interface_def.access_modifier();
    format!(
        r#"/// <summary>Creates a new <see cref="{prx_impl}"/> from the give connection and path.</summary>
/// <param name="connection">The connection. If it's an outgoing connection, the endpoint of the new proxy is
/// <see cref="Connection.RemoteEndpoint"/>; otherwise, the new proxy has no endpoint.</param>
/// <param name="path">The path of the proxy. If null, the path is set to <see cref="DefaultPath"/>.</param>
/// <param name="invoker">The invoker. If null and connection is an incoming connection, the invoker is set to
/// the server's invoker.</param>
/// <returns>The new proxy.</returns>
{access} static {prx_impl} FromConnection(
    IceRpc.Connection connection,
    string? path = null,
    IceRpc.IInvoker? invoker = null) =>
    new(IceRpc.Proxy.FromConnection(connection, path ?? DefaultPath, invoker));

/// <summary>Creates a new <see cref="{prx_impl}"/> with the given path and protocol.</summary>
/// <param name="path">The path for the proxy.</param>
/// <param name="protocol">The proxy protocol.</param>
/// <returns>The new proxy.</returns>
{access} static {prx_impl} FromPath(string path, IceRpc.Protocol? protocol = null) =>
    new(IceRpc.Proxy.FromPath(path, protocol ?? IceRpc.Protocol.Ice2));

/// <summary>Creates a new <see cref="{prx_impl}"/> from a string and invoker.</summary>
/// <param name="s">The string representation of the proxy.</param>
/// <param name="invoker">The invoker of the new proxy.</param>
/// <param name="parser">The proxy parser to use for this parsing. <c>null</c> selects the default proxy parser.</param>
/// <returns>The new proxy</returns>
/// <exception cref="global::System.FormatException"><c>s</c> does not contain a valid string representation
/// of a proxy.</exception>
{access} static {prx_impl} Parse(string s, IceRpc.IInvoker? invoker = null, IceRpc.IProxyParser? parser = null) =>
    new(IceRpc.Proxy.Parse(s, invoker));

/// <summary>Creates a new <see cref="{prx_impl}"/> from a string and invoker.</summary>
/// <param name="s">The proxy string representation.</param>
/// <param name="invoker">The invoker of the new proxy.</param>
/// <param name="parser">The proxy parser to use for this parsing. <c>null</c> selects the default proxy parser.</param>
/// <param name="prx">The new proxy.</param>
/// <returns><c>true</c> if the s parameter was parsed successfully; otherwise, <c>false</c>.</returns>
{access} static bool TryParse(string s, IceRpc.IInvoker? invoker, IceRpc.IProxyParser? parser, out {prx_impl} prx)
{{
    if (IceRpc.Proxy.TryParse(s, invoker, parser, out IceRpc.Proxy? proxy))
    {{
        prx = new(proxy);
        return true;
    }}
    else
    {{
        prx = default;
        return false;
    }}
}}

/// <summary>Constructs an instance of <see cref="{prx_impl}"/>.</summary>
/// <param name="proxy">The proxy to the remote service.</param>
{access} {prx_impl}(IceRpc.Proxy proxy) => Proxy = proxy;

/// <inheritdoc/>
{access} override string ToString() => Proxy.ToString();"#,
        prx_impl = interface_def.proxy_implementation_name(),
        access = access
    ).into()
}

/// The actual implementation of the proxy operation.
fn proxy_operation_impl(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();
    let operation_name = operation.escape_identifier();
    let async_operation_name = operation.escape_identifier_with_suffix("Async");
    let return_task = operation.return_task(false);
    let is_oneway = operation.has_attribute("oneway", false);

    let parameters = operation.nonstreamed_parameters();
    let stream_return = operation.streamed_return_member();

    let invocation_parameter = escape_parameter_name(&operation.parameters(), "invocation");
    let cancel_parameter = escape_parameter_name(&operation.parameters(), "cancel");

    let sends_classes = operation.sends_classes();
    let void_return = operation.return_type.len() == 0;
    let access = operation.parent().unwrap().access_modifier();

    let mut builder = FunctionBuilder::new(
        &access,
        &return_task,
        &async_operation_name,
        FunctionType::BlockBody,
    );
    builder.set_inherit_doc(true);
    builder.add_operation_parameters(operation, TypeContext::Outgoing);

    let mut body = CodeBlock::new();

    if operation.compress_arguments() {
        body.writeln(&format!(
            "\
if ({invocation}?.RequestFeatures.Get<IceRpc.Features.CompressPayload>() == null)
{{
    {invocation} ??= new IceRpc.Invocation();
    {invocation}.RequestFeatures = IceRpc.FeatureCollectionExtensions.CompressPayload(
        {invocation}.RequestFeatures);
}}
",
            invocation = invocation_parameter
        ));
    }

    let payload_encoding = if sends_classes {
        "IceRpc.Encoding.Ice11".to_owned()
    } else {
        body.writeln("var payloadEncoding = Proxy.GetIceEncoding();");
        "payloadEncoding".to_owned()
    };

    let mut invoke_args = vec![
        format!(r#""{}""#, operation.identifier()),
        payload_encoding.clone(),
    ];

    // The payload argument
    if parameters.is_empty() {
        invoke_args.push(format!(
            "{payload_encoding}.CreateEmptyPayload(hasStream: {has_stream})",
            payload_encoding = payload_encoding,
            has_stream = if operation.parameters.is_empty() { "false" } else { "true" }
        ));
    } else {
        let mut request_helper_args = vec![parameters.to_argument_tuple("")];

        if !sends_classes {
            request_helper_args.insert(0, payload_encoding.clone());
        }

        invoke_args.push(format!(
            "Request.{}({})",
            operation_name,
            request_helper_args.join(", ")
        ));
    }

    // Stream parameter (if any)
    if let Some(stream_parameter) = operation.streamed_parameter() {
        let stream_parameter_name = stream_parameter.parameter_name();
        let stream_type = stream_parameter.data_type();
        match stream_type.concrete_type() {
            Types::Primitive(b) if matches!(b, Primitive::Byte) =>
            invoke_args.push(stream_parameter_name),
            _ => invoke_args.push(format!(
                "\
{payload_encoding}.CreatePayloadSourceStream<{stream_type}>(
    {stream_parameter},
    {encode_action})",
                stream_type = stream_type.to_type_string(namespace, TypeContext::Outgoing, false),
                stream_parameter = stream_parameter_name,
                payload_encoding = payload_encoding,
                encode_action = encode_action(stream_type, namespace, true, true).indent()
            )),
        }
    } else {
        invoke_args.push("payloadSourceStream: null".to_owned());
    }

    if void_return && stream_return.is_none() {
        invoke_args.push("_defaultActivator".to_owned());
    }

    if !void_return {
        invoke_args.push("Response.".to_owned() + &async_operation_name);
    }

    invoke_args.push("invocation".to_owned());

    if operation.is_idempotent {
        invoke_args.push("idempotent: true".to_owned());
    }

    if void_return && is_oneway {
        invoke_args.push("oneway: true".to_owned());
    }

    invoke_args.push(format!("cancel: {}", cancel_parameter));

    writeln!(
        body,
        "\
return Proxy.InvokeAsync(
    {});",
        CodeBlock::from(invoke_args.join(",\n"))
            .indent()
            .to_string()
    );

    builder.set_body(body);

    builder.build()
}

fn proxy_base_operation_impl(operation: &Operation) -> CodeBlock {
    let async_name = operation.escape_identifier_with_suffix("Async");
    let return_task = operation.return_task(false);
    let mut operation_params = operation
        .parameters()
        .iter()
        .map(|p| p.parameter_name())
        .collect::<Vec<_>>();

    operation_params.push(escape_parameter_name(&operation.parameters(), "invocation"));
    operation_params.push(escape_parameter_name(&operation.parameters(), "cancel"));

    let mut builder = FunctionBuilder::new(
        &operation.parent().unwrap().access_modifier(),
        &return_task,
        &async_name,
        FunctionType::ExpressionBody,
    );
    builder.set_inherit_doc(true);
    builder.add_operation_parameters(operation, TypeContext::Outgoing);

    builder.set_body(
        format!(
            "new {base_prx_impl}(Proxy).{async_name}({operation_params})",
            base_prx_impl = operation.parent().unwrap().proxy_implementation_name(),
            async_name = async_name,
            operation_params = operation_params.join(", ")
        )
        .into(),
    );

    builder.build()
}

fn proxy_interface_operations(interface_def: &Interface) -> CodeBlock {
    let mut code = CodeBlock::new();
    let operations = interface_def.operations();

    for operation in operations {
        // TODO: add returns doc comments (see C++)
        code.add_block(
            &FunctionBuilder::new(
                "",
                &operation.return_task(false),
                &operation.escape_identifier_with_suffix("Async"),
                FunctionType::Declaration,
            )
            .add_container_attributes(operation)
            .add_comment("summary", &doc_comment_message(operation))
            .add_operation_parameters(operation, TypeContext::Outgoing)
            .build(),
        );
    }

    code
}

fn request_class(interface_def: &Interface) -> CodeBlock {
    let namespace = &interface_def.namespace();
    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| o.has_nonstreamed_parameters())
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let access = interface_def.access_modifier();
    let mut class_builder = ContainerBuilder::new(&format!("{} static class", access), "Request");

    class_builder.add_comment(
        "summary",
        "Converts the arguments of each operation that takes arguments into a request payload.",
    );

    for operation in operations {
        let params: Vec<&Parameter> = operation.nonstreamed_parameters();

        assert!(!params.is_empty());

        let sends_classes = operation.sends_classes();

        let mut builder = FunctionBuilder::new(
            &format!("{} static", access),
            "global::System.IO.Pipelines.PipeReader",
            &operation.escape_identifier(),
            FunctionType::ExpressionBody,
        );

        builder.add_comment(
            "summary",
            &format!(
                "Creates the request payload for operation {}.",
                operation.identifier()
            ),
        );

        if !sends_classes {
            builder.add_parameter(
                "IceEncoding",
                "encoding",
                None,
                Some("The encoding of the payload."),
            );
        }

        if params.len() == 1 {
            builder.add_parameter(
                &params.to_tuple_type(namespace, TypeContext::Outgoing, false),
                "arg",
                None,
                Some("The request argument."),
            );
        } else {
            builder.add_parameter(
                &format!(
                    "in {}",
                    params.to_tuple_type(namespace, TypeContext::Outgoing, false)
                ),
                "args",
                None,
                Some("The request arguments."),
            );
        }

        if sends_classes {
            builder.add_comment("returns", "The payload encoded with encoding 1.1.");
        } else {
            builder.add_comment(
                "returns",
                r#"The payload encoded with <paramref name="encoding"/>."#,
            );
        }

        let body = if sends_classes {
            format!(
                "\
IceRpc.Encoding.Ice11.{name}(
    {args},
    {encode_action},
    {format})",
                name = match params.len() {
                    1 => "CreatePayloadFromSingleArg",
                    _ => "CreatePayloadFromArgs",
                },
                args = if params.len() == 1 { "arg" } else { "in args" },
                encode_action = request_encode_action(operation).indent(),
                format = operation.format_type()
            )
        } else {
            format!(
                "\
encoding.{name}(
    {args},
    {encode_action})",
                name = match params.len() {
                    1 => "CreatePayloadFromSingleArg",
                    _ => "CreatePayloadFromArgs",
                },
                args = if params.len() == 1 { "arg" } else { "in args" },
                encode_action = request_encode_action(operation).indent(),
            )
        };

        builder.set_body(body.into());

        class_builder.add_block(builder.build());
    }

    class_builder.build().into()
}

fn response_class(interface_def: &Interface) -> CodeBlock {
    let namespace = &interface_def.namespace();
    let operations = interface_def
        .operations()
        .iter()
        .filter(|o| o.return_type.len() > 0)
        .cloned()
        .collect::<Vec<_>>();

    if operations.is_empty() {
        return "".into();
    }

    let access = interface_def.access_modifier();

    let mut class_builder = ContainerBuilder::new(&format!("{} static class", access), "Response");

    class_builder.add_comment(
        "summary",
        &format!(
            r#"Holds a <see cref="IceRpc.Slice.ResponseDecodeFunc{{T}}"/> for each non-void remote operation defined in <see cref="{}Prx"/>."#,
            interface_def.interface_name()));

    for operation in operations {
        let members = operation.return_members();
        assert!(!members.is_empty());

        class_builder.add_block(format!(
            r#"
/// <summary>The <see cref="ResponseDecodeFunc{{T}}"/> for the return value type of operation {name}.</summary>
{access} static async global::System.Threading.Tasks.ValueTask<{return_type}> {escaped_name}(
    IceRpc.IncomingResponse response,
    IceRpc.IInvoker? invoker,
    global::System.Threading.CancellationToken cancel) =>
    await response.ToReturnValueAsync(
        invoker,
        _defaultActivator,
        {response_decode_func},
        {has_stream},
        cancel).ConfigureAwait(false);"#,
            name = operation.identifier(),
            access = access,
            escaped_name = operation.escape_identifier_with_suffix("Async"),
            return_type = members.to_tuple_type(namespace, TypeContext::Incoming, false),
            response_decode_func = response_decode_func(operation).indent().indent().indent(),
            has_stream = members.len() > 0 && members.last().unwrap().is_streamed,
        ).into());
    }
    class_builder.build().into()
}

fn request_encode_action(operation: &Operation) -> CodeBlock {
    let namespace = operation.namespace();

    // We only want the non-streamed params
    let params: Vec<&Parameter> = operation.nonstreamed_parameters();

    // When the operation's parameter is a T? where T is an interface or a class, there is a
    // built-in encoder, so defaultEncodeAction is true.
    if params.len() == 1
        && get_bit_sequence_size(&params) == 0
        && params.first().unwrap().tag.is_none()
    {
        encode_action(params.first().unwrap().data_type(), &namespace, true, true)
    } else {
        format!(
            "\
(ref IceEncoder encoder,
 {_in}{param_type} value) =>
{{
    {encode}
}}",
            _in = if params.len() == 1 { "" } else { "in " },
            param_type = params.to_tuple_type(&namespace, TypeContext::Outgoing, false),
            encode = encode_operation(operation, false).indent()
        )
        .into()
    }
}

fn response_decode_func(operation: &Operation) -> CodeBlock {
    let namespace = &operation.namespace();
    // vec of members
    let members = operation.return_members();

    assert!(!members.is_empty());

    // TODO: simplify single stream response generated code

    if members.len() == 1
        && get_bit_sequence_size(&members) == 0
        && members.first().unwrap().tag.is_none()
        && !members.first().unwrap().is_streamed
    {
        decode_func(members.first().unwrap().data_type(), namespace)
    } else {
        format!(
            "\
(ref IceDecoder decoder) =>
{{
    {decode}
}}",
            decode = decode_operation(operation, false).indent()
        )
        .into()
    }
}

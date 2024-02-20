// Copyright (c) ZeroC, Inc.

use crate::builders::{Builder, FunctionCallBuilder};
use crate::code_block::CodeBlock;
use crate::code_gen_util::get_bit_sequence_size;
use crate::cs_attributes::CsType;
use crate::cs_util::*;
use crate::member_util::get_sorted_members;
use crate::slicec_ext::*;
use convert_case::Case;
use slicec::grammar::*;

/// Compute how many bits are needed to decode the provided members, and if more than 0 bits are needed,
/// this generates code that creates a new `BitSequenceReader` with the necessary capacity.
fn initialize_bit_sequence_reader_for<T: Member>(members: &[&T], code: &mut CodeBlock, encoding: Encoding) {
    let bit_sequence_size = get_bit_sequence_size(encoding, members);
    if bit_sequence_size > 0 {
        writeln!(
            code,
            "var bitSequenceReader = decoder.GetBitSequenceReader({bit_sequence_size});",
        );
    }
}

pub fn decode_fields(fields: &[&Field], encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    initialize_bit_sequence_reader_for(fields, &mut code, encoding);

    let action = |field_name, field_value| writeln!(code, "this.{field_name} = {field_value};");

    decode_fields_core(fields, encoding, action);
    code
}

pub fn decode_enum_fields(fields: &[&Field], enum_class: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    initialize_bit_sequence_reader_for(fields, &mut code, encoding);

    let mut new_instance_builder = FunctionCallBuilder::new(format!("new {enum_class}"));
    new_instance_builder.arguments_on_newline(fields.len() > 1);
    let action = |field_name, field_value| {
        new_instance_builder.add_argument(&format!("{field_name}: {field_value}"));
    };

    decode_fields_core(fields, encoding, action);
    writeln!(code, "var result = {}", new_instance_builder.build());
    code
}

/// Generates code for each of the provided fields by calling the provided `action` for each field.
fn decode_fields_core(fields: &[&Field], encoding: Encoding, mut action: impl FnMut(String, CodeBlock)) {
    for field in get_sorted_members(fields) {
        let namespace = field.namespace();

        let field_name = field.field_name();
        let field_value = match field.is_tagged() {
            true => decode_tagged(field, &namespace, true, encoding),
            false => decode_member(field, &namespace, encoding),
        };

        action(field_name, field_value);
    }
}

pub fn default_activator(encoding: Encoding) -> &'static str {
    if encoding == Encoding::Slice1 {
        "_defaultActivator"
    } else {
        "null"
    }
}

fn decode_member(member: &impl Member, namespace: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    let data_type = member.data_type();
    let type_string = remove_optional_modifier_from(data_type.field_type_string(namespace));

    if data_type.is_optional {
        match data_type.concrete_type() {
            Types::CustomType(custom_type_ref) if encoding == Encoding::Slice1 => {
                write!(
                    code,
                    "{decoder_extensions_class}.DecodeNullable{name}(ref decoder);",
                    decoder_extensions_class =
                        custom_type_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                    name = custom_type_ref.cs_identifier(Case::Pascal),
                );
                return code;
            }
            _ if data_type.is_class_type() => {
                // does not use bit sequence
                writeln!(code, "decoder.DecodeNullableClass<{type_string}>();");
                return code;
            }
            _ => {
                write!(code, "bitSequenceReader.Read() ? ");
                // keep going
            }
        }
    }

    match &data_type.concrete_typeref() {
        TypeRefs::Class(_) => {
            assert!(!data_type.is_optional);
            write!(code, "decoder.DecodeClass<{type_string}>()");
        }
        TypeRefs::Primitive(primitive_ref) => {
            if primitive_ref.is_class_type() {
                write!(code, "decoder.DecodeClass<SliceClass>()");
            } else {
                write!(code, "decoder.Decode{}()", primitive_ref.type_suffix());
            }
        }
        TypeRefs::Struct(_) => write!(code, "new {type_string}(ref decoder)"),
        TypeRefs::Dictionary(dictionary_ref) => code.write(&decode_dictionary(dictionary_ref, namespace, encoding)),
        TypeRefs::Sequence(sequence_ref) => code.write(&decode_sequence(sequence_ref, namespace, encoding)),
        TypeRefs::Enum(enum_ref) => {
            write!(
                code,
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    enum_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = enum_ref.cs_identifier(Case::Pascal),
            );
        }
        TypeRefs::ResultType(result_type_ref) => code.write(&decode_result(result_type_ref, namespace, encoding)),
        TypeRefs::CustomType(custom_type_ref) => {
            write!(
                code,
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    custom_type_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = custom_type_ref.cs_identifier(Case::Pascal),
            );
        }
    }

    if data_type.is_optional {
        code.write(" : null");
    }

    code
}

fn decode_tagged(member: &impl Member, namespace: &str, constructed_type: bool, encoding: Encoding) -> CodeBlock {
    let data_type = member.data_type();

    assert!(data_type.is_optional);
    assert!(member.is_tagged());

    let decode = FunctionCallBuilder::new("decoder.DecodeTagged")
        .add_argument(member.tag().unwrap())
        .add_argument_if_present(
            (encoding == Encoding::Slice1).then(|| format!("TagFormat.{}", data_type.tag_format().unwrap())),
        )
        .add_argument(decode_func(data_type, namespace, encoding))
        .add_argument_if_present((encoding == Encoding::Slice1).then(|| format!("useTagEndMarker: {constructed_type}")))
        .use_semicolon(false)
        .build();

    decode
}

fn decode_dictionary(dictionary_ref: &TypeRef<Dictionary>, namespace: &str, encoding: Encoding) -> CodeBlock {
    let key_type = &dictionary_ref.key_type;
    let value_type = &dictionary_ref.value_type;

    // decode key
    let decode_key = decode_func(key_type, namespace, encoding);
    let decode_value = decode_func_with_cast(value_type, namespace, encoding, false);
    let dictionary_type = remove_optional_modifier_from(dictionary_ref.incoming_parameter_type_string(namespace));
    let decode_key = decode_key.indent();
    let decode_value = decode_value.indent();

    // Use WithOptionalValueType method if encoding is not Slice1 and the value type is optional
    if encoding != Encoding::Slice1 && value_type.is_optional {
        format!(
            "\
decoder.DecodeDictionaryWithOptionalValueType(
    size => new {dictionary_type}(size),
    {decode_key},
    {decode_value})",
        )
    } else {
        format!(
            "\
decoder.DecodeDictionary(
    size => new {dictionary_type}(size),
    {decode_key},
    {decode_value})",
        )
    }
    .into()
}

fn decode_result(result_type_ref: &TypeRef<ResultType>, namespace: &str, encoding: Encoding) -> CodeBlock {
    assert!(encoding != Encoding::Slice1);
    let success_type = &result_type_ref.success_type;
    let failure_type = &result_type_ref.failure_type;

    let decode_success = decode_func_with_cast(success_type, namespace, encoding, success_type.is_optional).indent();
    let decode_failure = decode_func_with_cast(failure_type, namespace, encoding, failure_type.is_optional).indent();

    format!(
        "\
decoder.DecodeResult(
    {decode_success},
    {decode_failure})"
    )
    .into()
}

fn decode_sequence(sequence_ref: &TypeRef<Sequence>, namespace: &str, encoding: Encoding) -> CodeBlock {
    let element_type = &sequence_ref.element_type;
    let element_type_string = element_type.field_type_string(namespace);
    let sequence_type = remove_optional_modifier_from(sequence_ref.incoming_parameter_type_string(namespace));
    let has_cs_type_attribute = sequence_ref.has_attribute::<CsType>();

    let uses_bit_sequence = element_type.is_optional && encoding != Encoding::Slice1;
    let mut uses_sequence_factory = false;

    let function_name = match uses_bit_sequence {
        true => "decoder.DecodeSequenceOfOptionals",
        false => "decoder.DecodeSequence",
    };
    let mut builder = FunctionCallBuilder::new(function_name);
    builder.arguments_on_newline(true);
    builder.use_semicolon(false);

    match element_type.concrete_type() {
        Types::Primitive(primitive) if primitive.fixed_wire_size().is_some() && !uses_bit_sequence => {
            builder.set_type_argument(remove_optional_modifier_from(element_type_string));
            builder.add_argument_if(*primitive == Primitive::Bool, "checkElement: SliceDecoder.CheckBoolValue");
        }
        Types::Enum(enum_def) if enum_def.fixed_wire_size().is_some() && !uses_bit_sequence => {
            if enum_def.is_unchecked {
                builder.set_type_argument(remove_optional_modifier_from(element_type_string));
            } else {
                let underlying_type = enum_def.get_underlying_cs_type();
                let enum_name = enum_def.cs_identifier(Case::Pascal);
                let underlying_extensions_class = enum_def.escape_scoped_identifier_with_suffix(
                    &(underlying_type.to_cs_case(Case::Pascal) + "Extensions"),
                    namespace,
                );
                let decode_func = format!("({element_type_string} e) => _ = {underlying_extensions_class}.As{enum_name}(({underlying_type})e)");
                builder.add_argument(decode_func);
            }
        }
        _ => {
            if has_cs_type_attribute {
                uses_sequence_factory = true;
                builder.add_argument(format!("sequenceFactory: (size) => new {sequence_type}(size)"));
            }
            builder.add_argument(decode_func(element_type, namespace, encoding).indent());
        }
    }

    if has_cs_type_attribute && !uses_sequence_factory {
        // If 'cs::type' is used on a sequence of fixed-size primitives (or enums with such an underlying type),
        // instead of using a 'sequenceFactory', we pass the decoded sequence directly to the mapped type's constructor.
        return FunctionCallBuilder::new(format!("new {sequence_type}"))
            .arguments_on_newline(true)
            .use_semicolon(false)
            .add_argument(builder.build().indent())
            .build();
    }

    if !has_cs_type_attribute && matches!(element_type.concrete_type(), Types::Sequence(_)) {
        // For nested sequences we want to cast Foo[][] returned by DecodeSequence to IList<Foo>[]
        // used in the request and response decode methods.
        let code = builder.build();
        let element_type_string = element_type.field_type_string(namespace);
        return CodeBlock::from(format!("({element_type_string}[]){code}"));
    }

    builder.build()
}

fn decode_stream_parameter(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    let decode_func_body = decode_func_body(type_ref, namespace, encoding);
    if type_ref.is_optional {
        CodeBlock::from(format!(
            "(ref SliceDecoder decoder) => decoder.DecodeBool() ? {decode_func_body} : null"
        ))
    } else {
        decode_func(type_ref, namespace, encoding)
    }
}

/// Like [`decode_func`], this returns a lambda function that takes a `SliceDecoder` and returns a decoded type.
/// The difference between this function and `decode_func` is this function writes a cast on the lambda, if the
/// type being decoded is mapped differently as a field/parameter (ie. sequence and dictionary types).
///
/// This function must be called instead of `decode_func` when we're decoding in the context of a generic type
/// (ie. `decode_result` or `decode_dictionary`). The C# compiler cannot implicitly convert nested generic types,
/// so we need these casts to satisfy the type system.
fn decode_func_with_cast(type_ref: &TypeRef, namespace: &str, encoding: Encoding, is_optional: bool) -> CodeBlock {
    let decode_func = decode_func_body(type_ref, namespace, encoding);
    let cast = match type_ref.concrete_type() {
        Types::Sequence(_) | Types::Dictionary(_) => format!("({})", type_ref.field_type_string(namespace)),
        _ => "".to_owned(),
    };

    if is_optional {
        format!(
            "\
(ref SliceDecoder decoder) => decoder.DecodeBool() ?
    {cast}{decode_func}
    : null",
        )
    } else {
        format!("(ref SliceDecoder decoder) => {cast}{decode_func}")
    }
    .into()
}

fn decode_func(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    let decode_func_body = decode_func_body(type_ref, namespace, encoding);
    CodeBlock::from(format!("(ref SliceDecoder decoder) => {decode_func_body}"))
}

fn decode_func_body(type_ref: &TypeRef, namespace: &str, encoding: Encoding) -> CodeBlock {
    let mut code = CodeBlock::default();
    let type_name = remove_optional_modifier_from(type_ref.incoming_parameter_type_string(namespace));

    // When we decode the type, we decode it as a non-optional. If the type is supposed to be optional, we cast it after
    // decoding. Except for sequences and dictionaries, because we always cast them anyways, and for optional custom
    // types, since the 'DecodeNullableX' we use to decode already returns a nullable type (so no cast needed).
    if type_ref.is_optional
        && !matches!(type_ref.concrete_type(), Types::Sequence(_) | Types::Dictionary(_))
        && !matches!(type_ref.concrete_type(), Types::CustomType(_) if encoding == Encoding::Slice1)
    {
        write!(code, "({type_name}?)");
    }

    match &type_ref.concrete_typeref() {
        _ if type_ref.is_class_type() => {
            // is_class_type is either Typeref::Class or Primitive::AnyClass
            assert!(encoding == Encoding::Slice1);
            if type_ref.is_optional {
                write!(code, "decoder.DecodeNullableClass<{type_name}>()")
            } else {
                write!(code, "decoder.DecodeClass<{type_name}>()")
            }
        }
        // Primitive::AnyClass is handled above by is_class_type branch
        TypeRefs::Primitive(primitive_ref) => write!(code, "decoder.Decode{}()", primitive_ref.type_suffix()),
        TypeRefs::Sequence(sequence_ref) => write!(code, "{}", decode_sequence(sequence_ref, namespace, encoding)),
        TypeRefs::Dictionary(dictionary_ref) => {
            write!(code, "{}", decode_dictionary(dictionary_ref, namespace, encoding))
        }
        TypeRefs::Enum(enum_ref) => {
            write!(
                code,
                "{decoder_extensions_class}.Decode{name}(ref decoder)",
                decoder_extensions_class =
                    enum_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace),
                name = enum_ref.cs_identifier(Case::Pascal),
            )
        }
        TypeRefs::ResultType(result_type_ref) => {
            write!(code, "{}", decode_result(result_type_ref, namespace, encoding))
        }
        TypeRefs::Struct(_) => write!(code, "new {type_name}(ref decoder)"),
        TypeRefs::CustomType(custom_type_ref) => {
            let extensions_class =
                custom_type_ref.escape_scoped_identifier_with_suffix("SliceDecoderExtensions", namespace);
            let identifier = custom_type_ref.cs_identifier(Case::Pascal);

            // We use the 'Nullable' decoding function here, even for tags, to ensure interop with Ice.
            // Because an Ice client could send a tagged proxy that is 'set' to 'null'.
            if type_ref.is_optional && encoding == Encoding::Slice1 {
                write!(code, "{extensions_class}.DecodeNullable{identifier}(ref decoder)");
            } else {
                write!(code, "{extensions_class}.Decode{identifier}(ref decoder)");
            }
        }
        TypeRefs::Class(_) => panic!("unexpected, see is_class_type above"),
    }
    code
}

/// Returns a lambda function that takes a `SliceDecoder` and decodes the provided list of parameters from it.
/// This function assumes the parameters are non-streamed, and that at least one such parameter was provided.
pub fn decode_non_streamed_parameters_func(non_streamed_parameters: &[&Parameter], encoding: Encoding) -> CodeBlock {
    // Ensure that the parameters are all non-streamed.
    assert!(non_streamed_parameters.iter().all(|p| !p.is_streamed));

    match non_streamed_parameters {
        [] => panic!("passed empty parameter list to `decode_non_streamed_parameters_func"),

        // If there's only one parameter, it isn't tagged, and doesn't require a bit-sequence to decode,
        // We return a simplified lambda function.
        [param] if !param.is_tagged() && (encoding == Encoding::Slice1 || !param.data_type().is_optional) => {
            decode_func(param.data_type(), &param.namespace(), encoding)
        }

        // Otherwise we return a full multi-line lambda function for decoding the parameters.
        _ => {
            let mut code = CodeBlock::default();

            initialize_bit_sequence_reader_for(non_streamed_parameters, &mut code, encoding);

            for parameter in get_sorted_members(non_streamed_parameters) {
                let param_type = parameter.data_type();
                let namespace = parameter.namespace();

                // For optional value types we have to use the full type as the compiler cannot
                // disambiguate between null and the actual value type.
                let param_type_string = match param_type.is_optional && param_type.is_value_type() {
                    true => param_type.incoming_parameter_type_string(&namespace),
                    false => "var".to_owned(),
                };

                let param_name = &parameter.parameter_name_with_prefix();

                let decode = match parameter.is_tagged() {
                    true => decode_tagged(parameter, &namespace, false, encoding),
                    false => decode_member(parameter, &namespace, encoding),
                };

                writeln!(code, "{param_type_string} {param_name} = {decode};")
            }
            writeln!(code, "return {};", non_streamed_parameters.to_argument_tuple());

            let body_content = code.indent();
            format!(
                "\
(ref SliceDecoder decoder) =>
{{
    {body_content}
}}",
            )
            .into()
        }
    }
}

pub fn decode_operation_stream(
    stream_member: &Parameter,
    namespace: &str,
    encoding: Encoding,
    dispatch: bool,
) -> CodeBlock {
    let cs_encoding = encoding.to_cs_encoding();
    let param_type = stream_member.data_type();
    let param_type_str = param_type.incoming_parameter_type_string(namespace);
    let fixed_wire_size = param_type.fixed_wire_size();

    match param_type.concrete_type() {
        Types::Primitive(Primitive::UInt8) if !param_type.is_optional => {
            panic!("Must not be called for non-optional UInt8 parameters as there is no decoding");
        }
        _ => FunctionCallBuilder::new(format!("payloadContinuation.ToAsyncEnumerable<{param_type_str}>"))
            .arguments_on_newline(true)
            .add_argument(cs_encoding)
            .add_argument(decode_stream_parameter(param_type, namespace, encoding).indent())
            .add_argument_if_present(fixed_wire_size)
            .add_argument_if(!dispatch && fixed_wire_size.is_none(), "sender")
            .add_argument_if(
                fixed_wire_size.is_none(),
                "sliceFeature: request.Features.Get<ISliceFeature>()",
            )
            .build(),
    }
}

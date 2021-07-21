//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#include <CsUtil.h>
#include <Slice/Util.h>
#include <IceUtil/StringUtil.h>

#include <sys/types.h>
#include <sys/stat.h>

#ifdef _WIN32
#  include <direct.h>
#else
#  include <unistd.h>
#endif

using namespace std;
using namespace Slice;
using namespace IceUtil;
using namespace IceUtilInternal;

bool
Slice::normalizeCase(const ContainedPtr& c)
{
    auto fileMetadata = c->unit()->findDefinitionContext(c->file())->getAllMetadata();
    if(find(begin(fileMetadata), end(fileMetadata), "preserve-case") != end(fileMetadata) ||
       find(begin(fileMetadata), end(fileMetadata), "cs:preserve-case") != end(fileMetadata))
    {
        return false;
    }
    return true;
}
std::string
Slice::operationName(const OperationPtr& op)
{
    return normalizeCase(op) ? pascalCase(op->name()) : op->name();
}

std::string
Slice::paramName(const MemberPtr& param, const string& prefix)
{
    string name = param->name();
    return normalizeCase(param) ? fixId(prefix + camelCase(name)) : fixId(prefix + name);
}

std::string
Slice::paramTypeStr(const MemberPtr& param, const string& ns, bool readOnly)
{
    return CsGenerator::typeToString(param->type(),
                                     ns,
                                     readOnly,
                                     true, // isParam
                                     param->stream());
}

std::string
Slice::fieldName(const MemberPtr& member)
{
    string name = member->name();
    return normalizeCase(member) ? fixId(pascalCase(name)) : fixId(name);
}

std::string
Slice::interfaceName(const InterfaceDeclPtr& decl)
{
    string name = normalizeCase(decl) ? pascalCase(decl->name()) : decl->name();

    // Check if the interface already follows the 'I' prefix convention.
    if (name.size() >= 2 && name.at(0) == 'I' && isupper(name.at(1)))
    {
        return name;
    }
    return string("I") + name;
}

std::string
Slice::interfaceName(const InterfaceDefPtr& def)
{
    return interfaceName(def->declaration());
}

std::string
Slice::helperName(const TypePtr& type, const string& scope)
{
    ContainedPtr contained = ContainedPtr::dynamicCast(type);
    assert(contained);
    return getUnqualified(contained, scope, "", "Helper");
}

namespace
{

const std::array<std::string, 17> builtinSuffixTable =
{
    "Bool",
    "Byte",
    "Short",
    "UShort",
    "Int",
    "UInt",
    "VarInt",
    "VarUInt",
    "Long",
    "ULong",
    "VarLong",
    "VarULong",
    "Float",
    "Double",
    "String",
    "Proxy",
    "Class"
};

string
mangleName(const string& name, unsigned int baseTypes)
{
    static const char* ObjectNames[] = { "Equals", "Finalize", "GetHashCode", "GetType", "MemberwiseClone",
                                         "ReferenceEquals", "ToString", 0 };

    static const char* ExceptionNames[] = { "Data", "GetBaseException", "GetObjectData", "HelpLink", "HResult",
                                            "InnerException", "Message", "Source", "StackTrace", "TargetSite", 0 };
    string mangled = name;

    if((baseTypes & ExceptionType) == ExceptionType)
    {
        for(int i = 0; ExceptionNames[i] != 0; ++i)
        {
            if(ciequals(name, ExceptionNames[i]))
            {
                return "Ice" + name;
            }
        }
        baseTypes |= ObjectType; // Exception is an Object
    }

    if((baseTypes & ObjectType) == ObjectType)
    {
        for(int i = 0; ObjectNames[i] != 0; ++i)
        {
            if(ciequals(name, ObjectNames[i]))
            {
                return "Ice" + name;
            }
        }
    }

    return mangled;
}

string
lookupKwd(const string& name, unsigned int baseTypes)
{
    //
    // Keyword list. *Must* be kept in alphabetical order.
    //
    static const string keywordList[] =
    {
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator",
        "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try",
        "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
    };
    bool found = binary_search(&keywordList[0],
                               &keywordList[sizeof(keywordList) / sizeof(*keywordList)],
                               name,
                               Slice::CICompare());
    if(found)
    {
        return "@" + name;
    }
    return mangleName(name, baseTypes);
}

}

std::string
Slice::builtinSuffix(const BuiltinPtr& builtin)
{
    return builtinSuffixTable[builtin->kind()];
}

string
Slice::getNamespaceMetadata(const ContainedPtr& cont)
{
    //
    // Traverse to the top-level module.
    //
    ModulePtr m;
    ContainedPtr p = cont;
    while(true)
    {
        if(ModulePtr::dynamicCast(p))
        {
            m = ModulePtr::dynamicCast(p);
        }

        ContainerPtr c = p->container();
        p = ContainedPtr::dynamicCast(c); // This cast fails for Unit.
        if(!p)
        {
            break;
        }
    }

    assert(m);

    static const string prefix = "cs:namespace:";

    string q;
    if(m->findMetadata(prefix, q))
    {
        q = q.substr(prefix.size());
    }
    return q;
}

string
Slice::getNamespace(const ContainedPtr& cont)
{
    string scope = fixId(cont->scope());

    // Remove trailing .
    if(scope.rfind(".") == scope.size() - 1)
    {
        scope = scope.substr(0, scope.size() - 1);
    }

    string ns = getNamespaceMetadata(cont);

    if(ns.empty())
    {
        return scope;
    }

    // If the namespace is not empty we strip the top level module from the scope
    size_t pos = scope.find(".");
    if (pos != string::npos)
    {
        ns += "." + scope.substr(pos + 1);
    }

    return ns;
}

string
Slice::getUnqualified(const string& type, const string& scope, bool builtin)
{
    if (type.find(".") != string::npos && type.find(scope) == 0 && type.find(".", scope.size() + 1) == string::npos)
    {
        return type.substr(scope.size() + 1);
    }
    else if (builtin || type.rfind("IceRpc", 0) == 0)
    {
        return type;
    }
    else
    {
        return "global::" + type;
    }
}

string
Slice::getUnqualified(const ContainedPtr& p, const string& package, const string& prefix, const string& suffix)
{
    string name = fixId(prefix + p->name() + suffix);
    string contPkg = getNamespace(p);
    if (contPkg == package || contPkg.empty())
    {
        return name;
    }
    else if (contPkg.rfind("IceRpc", 0) == 0)
    {
        return contPkg + "." + name;
    }
    else
    {
        return "global::" + contPkg + "." + name;
    }
}

//
// If the passed name is a scoped name, return the identical scoped name,
// but with all components that are C# keywords replaced by
// their "@"-prefixed version; otherwise, if the passed name is
// not scoped, but a C# keyword, return the "@"-prefixed name;
// otherwise, check if the name is one of the method names of baseTypes;
// if so, prefix it with ice_; otherwise, return the name unchanged.
//
string
Slice::fixId(const string& name, unsigned int baseTypes)
{
    if(name.empty())
    {
        return name;
    }
    if(name[0] != ':')
    {
        return lookupKwd(name, baseTypes);
    }
    vector<string> ids = splitScopedName(name);
    transform(begin(ids), end(ids), begin(ids), [baseTypes](const std::string& i)
                                                {
                                                    return lookupKwd(i, baseTypes);
                                                });
    ostringstream os;
    for(vector<string>::const_iterator i = ids.begin(); i != ids.end();)
    {
        os << *i;
        if(++i != ids.end())
        {
            os << ".";
        }
    }
    return os.str();
}

string
Slice::CsGenerator::typeToString(const TypePtr& type, const string& package, bool readOnly, bool isParam, bool streamParam)
{
    if (streamParam)
    {
        if (auto builtin = BuiltinPtr::dynamicCast(type); builtin && builtin->kind() == Builtin::KindByte)
        {
            return "global::System.IO.Stream";
        }
        else
        {
            // TODO
            assert(false);
            return "";
        }
    }

    if (!type)
    {
        return "void";
    }

    SequencePtr seq;

    auto optional = OptionalPtr::dynamicCast(type);
    if (optional)
    {
        seq = SequencePtr::dynamicCast(optional->underlying());
        if (!isParam || !seq || !readOnly)
        {
            return typeToString(optional->underlying(), package, readOnly, isParam) + "?";
        }
        // else process seq in the code below.
    }
    else
    {
        seq = SequencePtr::dynamicCast(type);
    }

    static const std::array<std::string, 17> builtinTable =
    {
        "bool",
        "byte",
        "short",
        "ushort",
        "int",
        "uint",
        "int",
        "uint",
        "long",
        "ulong",
        "long",
        "ulong",
        "float",
        "double",
        "string",
        "IceRpc.ServicePrx",
        "IceRpc.AnyClass"
    };

    BuiltinPtr builtin = BuiltinPtr::dynamicCast(type);
    if(builtin)
    {
        return getUnqualified(builtinTable[builtin->kind()], package, true);
    }

    ClassDeclPtr cl = ClassDeclPtr::dynamicCast(type);
    if(cl)
    {
        return getUnqualified(cl, package);
    }

    InterfaceDeclPtr interface = InterfaceDeclPtr::dynamicCast(type);
    if(interface)
    {
        return getUnqualified(getNamespace(interface) + "." + interfaceName(interface).substr(1) + "Prx", package);
    }

    if(seq)
    {
        string customType = seq->findMetadataWithPrefix("cs:generic:");

        if (!isParam)
        {
            return "global::System.Collections.Generic.IList<" + typeToString(seq->type(), package) + ">";
        }
        else if (readOnly)
        {
            auto elementType = seq->type();
            string elementTypeStr = "<" + typeToString(elementType, package, readOnly, false) + ">";
            if (isFixedSizeNumericSequence(seq) && customType.empty())
            {
                return "global::System.ReadOnlyMemory" + elementTypeStr; // same for optional!
            }
            else
            {
                return "global::System.Collections.Generic.IEnumerable" + elementTypeStr + (optional ? "?" : "");
            }
        }
        else if (customType.empty())
        {
            return typeToString(seq->type(), package) + "[]";
        }
        else
        {
            ostringstream out;
            if (customType == "List" || customType == "LinkedList" || customType == "Queue" || customType == "Stack")
            {
                out << "global::System.Collections.Generic.";
            }
            out << customType << "<" << typeToString(seq->type(), package) << ">";
            return out.str();
        }
    }

    DictionaryPtr d = DictionaryPtr::dynamicCast(type);
    if (d)
    {
        if (!isParam)
        {
            return "global::System.Collections.Generic.IDictionary<" +
                typeToString(d->keyType(), package) + ", " +
                typeToString(d->valueType(), package) + ">";
        }
        else if (readOnly)
        {
            return "global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<" +
                typeToString(d->keyType(), package) + ", " +
                typeToString(d->valueType(), package) + ">>";
        }
        else
        {
            string prefix = "cs:generic:";
            string meta;
            string typeName;

            if (d->findMetadata(prefix, meta))
            {
                typeName = meta.substr(prefix.size());
            }
            else
            {
                typeName = "Dictionary";
            }

            return "global::System.Collections.Generic." + typeName + "<" +
                typeToString(d->keyType(), package) + ", " +
                typeToString(d->valueType(), package) + ">";
        }
    }

    ContainedPtr contained = ContainedPtr::dynamicCast(type);
    if(contained)
    {
        return getUnqualified(contained, package);
    }

    return "???";
}

string
Slice::returnTypeStr(const OperationPtr& op, const string& ns, bool dispatch)
{
    InterfaceDefPtr interface = op->interface();
    auto returnValues = op->returnType();

    if (returnValues.size() == 0)
    {
        return "void";
    }
    else if (dispatch && op->hasMarshaledResult())
    {
        string name = getNamespace(interface) + "." + interfaceName(interface);
        return getUnqualified(name, ns) + "." + pascalCase(op->name()) + "MarshaledReturnValue";
    }
    else if (returnValues.size() > 1)
    {
        // when dispatch is true, the result-type is read-only
        return toTupleType(returnValues, ns, dispatch);
    }
    else
    {
        return paramTypeStr(returnValues.front(), ns, dispatch);
    }
}

string
Slice::returnTaskStr(const OperationPtr& op, const string& ns, bool dispatch)
{
    string t = returnTypeStr(op, ns, dispatch);
    if(t == "void")
    {
        if (dispatch)
        {
            return "global::System.Threading.Tasks.ValueTask";
        }
        else
        {
            return "global::System.Threading.Tasks.Task";
        }
    }
    else if (dispatch)
    {
        return "global::System.Threading.Tasks.ValueTask<" + t + '>';
    }
    else
    {
        return "global::System.Threading.Tasks.Task<" + t + '>';
    }
}

bool
Slice::isCollectionType(const TypePtr& type)
{
    return SequencePtr::dynamicCast(type) || DictionaryPtr::dynamicCast(type);
}

bool
Slice::isValueType(const TypePtr& type)
{
    assert(!OptionalPtr::dynamicCast(type));

    BuiltinPtr builtin = BuiltinPtr::dynamicCast(type);
    if (builtin)
    {
        switch (builtin->kind())
        {
            case Builtin::KindString:
            case Builtin::KindAnyClass:
            {
                return false;
            }
            default:
            {
                return true;
            }
        }
    }

    if (EnumPtr::dynamicCast(type) || StructPtr::dynamicCast(type) || type->isInterfaceType())
    {
        return true;
    }
    return false;
}

bool
Slice::isReferenceType(const TypePtr& type)
{
    return !isValueType(type);
}

bool
Slice::isFixedSizeNumericSequence(const SequencePtr& seq)
{
    TypePtr type = seq->type();
    if (auto en = EnumPtr::dynamicCast(type); en && en->underlying())
    {
        type = en->underlying();
    }

    BuiltinPtr builtin = BuiltinPtr::dynamicCast(type);
    return builtin && builtin->isNumericTypeOrBool() && !builtin->isVariableLength();
}

vector<string>
Slice::getNames(const MemberList& params, const string& prefix)
{
    return getNames(params, [&](const auto& item) { return paramName(item, prefix); });
}

vector<string>
Slice::getNames(const MemberList& params, function<string (const MemberPtr&)> fn)
{
    return mapfn<MemberPtr>(params, move(fn));
}

std::string
Slice::toTuple(const MemberList& params, const string& prefix)
{
    if(params.size() == 1)
    {
        return paramName(params.front(), prefix);
    }
    else
    {
        ostringstream os;
        os << "(";
        bool firstParam = true;
        for (const auto& param : params)
        {
            if (firstParam)
            {
                firstParam = false;
            }
            else
            {
                os << ", ";
            }
            os << paramName(param, prefix);
        }
        os << ")";
        return os.str();
    }
}

std::string
Slice::toTupleType(const MemberList& params, const string& ns, bool readOnly)
{
    if(params.size() == 1)
    {
        return paramTypeStr(params.front(), ns, readOnly);
    }
    else
    {
        ostringstream os;
        os << "(";
        bool firstParam = true;
        for (const auto& param : params)
        {
            if (firstParam)
            {
                firstParam = false;
            }
            else
            {
                os << ", ";
            }

            os << paramTypeStr(param, ns, readOnly) << " " << fieldName(param);
        }
        os << ")";
        return os.str();
    }
}

string
Slice::CsGenerator::encodeAction(const TypePtr& type, const string& scope, bool readOnly, bool param)
{
    ostringstream out;
    if (auto optional = OptionalPtr::dynamicCast(type))
    {
        // Expected for proxy and class types.
        TypePtr underlying = optional->underlying();
        out << typeToString(underlying, scope, readOnly, param) << ".NullableEncodeAction";
    }
    else if (type->isClassType() || type->isInterfaceType())
    {
        out << typeToString(type, scope, readOnly, param) << ".EncodeAction";
    }
    else if (auto builtin = BuiltinPtr::dynamicCast(type))
    {
        out << "IceRpc.BasicEncodeActions." << builtinSuffixTable[builtin->kind()] << "EncodeAction";
    }
    else if (EnumPtr::dynamicCast(type))
    {
        out << helperName(type, scope) << ".EncodeAction";
    }
    else if (auto dict = DictionaryPtr::dynamicCast(type))
    {
        out << "(encoder, dictionary) => " << dictionaryMarshalCode(dict, scope, "dictionary");
    }
    else if (auto seq = SequencePtr::dynamicCast(type))
    {
        // We generate the sequence encoder inline, so this function must not be called when the top-level object is
        // not cached.
        out << "(encoder, sequence) => " << sequenceMarshalCode(seq, scope, "sequence", readOnly, param);
    }
    else
    {
        out << typeToString(type, scope, readOnly, param) << ".EncodeAction";
    }
    return out.str();
}

void
Slice::CsGenerator::writeMarshalCode(
    Output& out,
    const TypePtr& type,
    int& bitSequenceIndex,
    bool forNestedType,
    const string& scope,
    const string& param)
{
    if (auto optional = OptionalPtr::dynamicCast(type))
    {
        TypePtr underlying = optional->underlying();

        if (underlying->isInterfaceType())
        {
            // does not use bit sequence
            out << nl << "encoder.EncodeNullableProxy(" << param << "?.Proxy);";
        }
        else if (underlying->isClassType())
        {
            // does not use bit sequence
            out << nl << "encoder.EncodeNullableClass(" << param;
            if (BuiltinPtr::dynamicCast(underlying))
            {
                out << ", null);"; // no formal type optimization
            }
            else
            {
                out << ", " << typeToString(underlying, scope, false, !forNestedType) << ".IceTypeId);";
            }
        }
        else
        {
            assert(bitSequenceIndex >= 0);
            SequencePtr seq = SequencePtr::dynamicCast(underlying);
            bool readOnlyMemory = false;
            if (seq)
            {
                auto elementType = seq->type();
                string customType = seq->findMetadataWithPrefix("cs:generic:");
                if (isFixedSizeNumericSequence(seq) && customType.empty() && !forNestedType)
                {
                    readOnlyMemory = true;
                }
            }
            out << nl << "if (" << param;
            if (readOnlyMemory)
            {
                // A null T[]? or List<T>? is implicitly converted into a default aka null ReadOnlyMemory<T> or
                // ReadOnlySpan<T>. Furthermore, the span of a default ReadOnlyMemory<T> is a default ReadOnlySpan<T>,
                // which is distinct from the span of an empty sequence. This is why the "value.Span != null" below
                // works correctly.
                out << ".Span";
            }
            out << " != null)";
            out << sb;
            string nonNullParam = param + (isReferenceType(underlying) ? "" : ".Value");
            writeMarshalCode(out, underlying, bitSequenceIndex, forNestedType, scope, nonNullParam);
            out << eb;
            out << nl << "else";
            out << sb;
            out << nl << "bitSequence[" << bitSequenceIndex++ << "] = false;";
            out << eb;
        }
    }
    else
    {
        if (type->isInterfaceType())
        {
            out << nl << "encoder.EncodeProxy(" << param << ".Proxy);";
        }
        else if (type->isClassType())
        {
            out << nl << "encoder.EncodeClass(" << param;
            if (BuiltinPtr::dynamicCast(type))
            {
                out << ", null);"; // no formal type optimization
            }
            else
            {
                out << ", " << typeToString(type, scope) << ".IceTypeId);";
            }
        }
        else if (auto builtin = BuiltinPtr::dynamicCast(type))
        {
            out << nl << "encoder.Encode" << builtinSuffixTable[builtin->kind()] << "(" << param << ");";
        }
        else if (StructPtr::dynamicCast(type))
        {
            out << nl << param << ".Encode(encoder);";
        }
        else if (auto seq = SequencePtr::dynamicCast(type))
        {
            out << nl << sequenceMarshalCode(seq, scope, param, !forNestedType, !forNestedType) << ";";
        }
        else if (auto dict = DictionaryPtr::dynamicCast(type))
        {
            out << nl << dictionaryMarshalCode(dict, scope, param) << ";";
        }
        else
        {
            out << nl << helperName(type, scope) << ".Encode(encoder, " << param << ");";
        }
    }
}

string
Slice::CsGenerator::decodeFunc(const TypePtr& type, const string& scope)
{
    ostringstream out;
    if (auto optional = OptionalPtr::dynamicCast(type))
    {
        TypePtr underlying = optional->underlying();
        // Expected for classes and proxies
        assert(underlying->isClassType() || underlying->isInterfaceType());
        out << typeToString(underlying, scope) << ".NullableDecodeFunc";
    }
    else if (auto builtin = BuiltinPtr::dynamicCast(type); builtin && !builtin->usesClasses() &&
                builtin->kind() != Builtin::KindObject)
    {
        out << "IceRpc.BasicDecodeFuncs." << builtinSuffixTable[builtin->kind()] << "DecodeFunc";
    }
    else if (auto seq = SequencePtr::dynamicCast(type))
    {
        out << "decoder => " << sequenceUnmarshalCode(seq, scope);
    }
    else if (auto dict = DictionaryPtr::dynamicCast(type))
    {
        out << "decoder => " << dictionaryUnmarshalCode(dict, scope);
    }
    else if (EnumPtr::dynamicCast(type))
    {
        out << helperName(type, scope) << ".DecodeFunc";
    }
    else
    {
        out << typeToString(type, scope) << ".DecodeFunc";
    }
    return out.str();
}

string
Slice::CsGenerator::streamDataReader(const TypePtr& type)
{
    ostringstream out;
    if (auto builtin = BuiltinPtr::dynamicCast(type); builtin && builtin->kind() == Builtin::KindByte)
    {
        out << "ReceiveDataIntoIOStream";
    }
    else
    {
        // TODO
        assert(false);
    }
    return out.str();
}

string
Slice::CsGenerator::streamDataWriter(const TypePtr &type)
{
    ostringstream out;
    if (auto builtin = BuiltinPtr::dynamicCast(type); builtin && builtin->kind() == Builtin::KindByte)
    {
        out << "SendDataFromIOStream";
    }
    else
    {
        // TODO
        assert(false);
    }
    return out.str();
}

void
Slice::CsGenerator::writeUnmarshalCode(
    Output& out,
    const TypePtr& type,
    int& bitSequenceIndex,
    const string& scope,
    const string& param)
{
    out << param << " = ";
    auto optional = OptionalPtr::dynamicCast(type);
    TypePtr underlying = optional ? optional->underlying() : type;

    if (optional)
    {
        if (underlying->isInterfaceType())
        {
            // does not use bit sequence
            out << "IceRpc.IceDecoderPrxExtensions.DecodeNullablePrx<" << typeToString(underlying, scope)
                << ">(decoder);";
            return;
        }
        else if (underlying->isClassType())
        {
            // does not use bit sequence
            out << "decoder.DecodeNullableClass<" << typeToString(underlying, scope) << ">(";
            if (BuiltinPtr::dynamicCast(underlying))
            {
                out << "formalTypeId: null";
            }
            else
            {
                out << typeToString(underlying, scope) << ".IceTypeId";
            }
            out << ");";
            return;
        }
        else
        {
            assert(bitSequenceIndex >= 0);
            out << "bitSequence[" << bitSequenceIndex++ << "] ? ";
            // and keep going
        }
    }

    if (underlying->isInterfaceType())
    {
        assert(!optional);
        out << "new " << typeToString(underlying, scope) << "(decoder.DecodeProxy());";
    }
    else if (underlying->isClassType())
    {
        assert(!optional);
        out << "decoder.DecodeClass<" << typeToString(underlying, scope) << ">(";
        if (BuiltinPtr::dynamicCast(underlying))
        {
            out << "formalTypeId: null";
        }
        else
        {
            out << typeToString(underlying, scope) << ".IceTypeId";
        }
        out << ")";
    }
    else if (auto builtin = BuiltinPtr::dynamicCast(underlying))
    {
        out << "decoder.Decode" << builtinSuffixTable[builtin->kind()] << "()";
    }
    else if (auto st = StructPtr::dynamicCast(underlying))
    {
        out << "new " << getUnqualified(st, scope) << "(decoder)";
    }
    else if (auto dict = DictionaryPtr::dynamicCast(underlying))
    {
        out << dictionaryUnmarshalCode(dict, scope);
    }
    else if (auto seq = SequencePtr::dynamicCast(underlying))
    {
        out << sequenceUnmarshalCode(seq, scope);
    }
    else
    {
        auto contained = ContainedPtr::dynamicCast(underlying);
        assert(contained);
        out << helperName(underlying, scope) << ".Decode" << contained->name() << "(decoder)";
    }

    if (optional)
    {
        out << " : null";
    }
    out << ";";
}

void
Slice::CsGenerator::writeTaggedMarshalCode(
    Output& out,
    const OptionalPtr& optionalType,
    bool isDataMember,
    const string& scope,
    const string& param,
    int tag)
{
    assert(optionalType);
    TypePtr type = optionalType->underlying();

    BuiltinPtr builtin = BuiltinPtr::dynamicCast(type);
    StructPtr st = StructPtr::dynamicCast(type);
    SequencePtr seq = SequencePtr::dynamicCast(type);

    if (type->isInterfaceType())
    {
        out << nl << "encoder.EncodeTaggedProxy(" << tag << ", " << param << "?.Proxy);";
    }
    else if (builtin || type->isClassType())
    {
        auto kind = builtin ? builtin->kind() : Builtin::KindAnyClass;
        out << nl << "encoder.EncodeTagged" << builtinSuffixTable[kind] << "(" << tag << ", " << param << ");";
    }
    else if(st)
    {
        out << nl << "encoder.EncodeTaggedStruct(" << tag << ", " << param;
        if(!st->isVariableLength())
        {
            out << ", fixedSize: " << st->minWireSize();
        }
        out << ", " << encodeAction(st, scope, !isDataMember) << ");";
    }
    else if (auto en = EnumPtr::dynamicCast(type))
    {
        string suffix = en->underlying() ? builtinSuffix(en->underlying()) : "Size";
        string underlyingType = en->underlying() ? typeToString(en->underlying(), "") : "int";
        out << nl << "encoder.EncodeTagged" << suffix << "(" << tag << ", (" << underlyingType << "?)"
            << param << ");";
    }
    else if(seq)
    {
        const TypePtr elementType = seq->type();
        builtin = BuiltinPtr::dynamicCast(elementType);

        bool hasCustomType = seq->hasMetadataWithPrefix("cs:generic");
        bool readOnly = !isDataMember;

        if (isFixedSizeNumericSequence(seq) && (readOnly || !hasCustomType))
        {
            if (readOnly && !hasCustomType)
            {
                out << nl << "encoder.EncodeTaggedSequence(" << tag << ", " << param << ".Span" << ");";
            }
            else
            {
                // param is an IEnumerable<T>
                out << nl << "encoder.EncodeTaggedSequence(" << tag << ", " << param << ");";
            }
        }
        else if (auto optional = OptionalPtr::dynamicCast(elementType); optional && optional->encodedUsingBitSequence())
        {
            TypePtr underlying = optional->underlying();
            out << nl << "encoder.EncodeTaggedSequence(" << tag << ", " << param;
            if (isReferenceType(underlying))
            {
                out << ", withBitSequence: true";
            }
            out << ", " << encodeAction(underlying, scope, !isDataMember) << ");";
        }
        else if (elementType->isVariableLength())
        {
            out << nl << "encoder.EncodeTaggedSequence(" << tag << ", " << param
                << ", " << encodeAction(elementType, scope, !isDataMember) << ");";
        }
        else
        {
            // Fixed size = min-size
            out << nl << "encoder.EncodeTaggedSequence(" << tag << ", " << param << ", "
                << "elementSize: " << elementType->minWireSize()
                << ", " << encodeAction(elementType, scope, !isDataMember) << ");";
        }
    }
    else
    {
        DictionaryPtr d = DictionaryPtr::dynamicCast(type);
        assert(d);
        TypePtr keyType = d->keyType();
        TypePtr valueType = d->valueType();

        bool withBitSequence = false;

        if (auto optional = OptionalPtr::dynamicCast(valueType); optional && optional->encodedUsingBitSequence())
        {
            withBitSequence = true;
            valueType = optional->underlying();
        }

        out << nl << "encoder.EncodeTaggedDictionary(" << tag << ", " << param;

        if (!withBitSequence && !keyType->isVariableLength() && !valueType->isVariableLength())
        {
            // Both are fixed size
            out << ", entrySize: " << (keyType->minWireSize() + valueType->minWireSize());
        }
        if (withBitSequence && isReferenceType(valueType))
        {
            out << ", withBitSequence: true";
        }
        out << ", " << encodeAction(keyType, scope)
            << ", " << encodeAction(valueType, scope) << ");";
    }
}

void
Slice::CsGenerator::writeTaggedUnmarshalCode(
    Output& out,
    const OptionalPtr& optionalType,
    const string& scope,
    const string& param,
    int tag,
    const MemberPtr& dataMember)
{
    assert(optionalType);
    TypePtr type = optionalType->underlying();

    BuiltinPtr builtin = BuiltinPtr::dynamicCast(type);
    StructPtr st = StructPtr::dynamicCast(type);
    SequencePtr seq = SequencePtr::dynamicCast(type);

    out << param << " = ";

    if (type->isClassType())
    {
        out << "decoder.DecodeTaggedClass<" << typeToString(type, scope) << ">(" << tag << ")";
    }
    else if (type->isInterfaceType())
    {
        out << "IceRpc.IceDecoderPrxExtensions.DecodeTaggedPrx<"<< typeToString(type, scope) << ">(decoder, "
            << tag << ");";
    }
    else if (builtin)
    {
        out << "decoder.DecodeTagged" << builtinSuffixTable[builtin->kind()] << "(" << tag << ")";
    }
    else if (st)
    {
        out << "decoder.DecodeTaggedStruct(" << tag << ", fixedSize: " << (st->isVariableLength() ? "false" : "true")
            << ", " << decodeFunc(st, scope) << ")";
    }
    else if (auto en = EnumPtr::dynamicCast(type))
    {
        const string tmpName = (dataMember ? dataMember->name() : param) + "_";
        string suffix = en->underlying() ? builtinSuffix(en->underlying()) : "Size";
        string underlyingType = en->underlying() ? typeToString(en->underlying(), "") : "int";

        out << "decoder.DecodeTagged" << suffix << "(" << tag << ") is " << underlyingType << " " << tmpName << " ? "
            << helperName(en, scope) << ".As" << en->name() << "(" << tmpName << ") : ("
            << typeToString(en, scope) << "?)null";
    }
    else if (seq)
    {
        const TypePtr elementType = seq->type();
        if (isFixedSizeNumericSequence(seq) && !seq->hasMetadataWithPrefix("cs:generic"))
        {
            out << "decoder.DecodeTaggedArray";
            if (auto enElement = EnumPtr::dynamicCast(elementType); enElement && !enElement->isUnchecked())
            {
                out << "(" << tag << ", (" << typeToString(enElement, scope) << " e) => _ = "
                    << helperName(enElement, scope) << ".As" << enElement->name()
                    << "((" << typeToString(enElement->underlying(), scope) << ")e))";
            }
            else
            {
                out << "<" << typeToString(elementType, scope) << ">(" << tag << ")";
            }
        }
        else if (seq->hasMetadataWithPrefix("cs:generic:"))
        {
            const string tmpName = (dataMember ? dataMember->name() : param) + "_";
            if (auto optional = OptionalPtr::dynamicCast(elementType); optional && optional->encodedUsingBitSequence())
            {
                TypePtr underlying = optional->underlying();
                out << "decoder.DecodeTaggedSequence(" << tag << ", "
                    << (isReferenceType(underlying) ? "withBitSequence: true, " : "")
                    << decodeFunc(elementType, scope)
                    << ") is global::System.Collections.Generic.ICollection<" << typeToString(elementType, scope)
                    << "> " << tmpName << " ? new " << typeToString(seq, scope, false, true) << "(" << tmpName << ")"
                    << " : null";
            }
            else
            {
                out << "decoder.DecodeTaggedSequence("
                    << tag << ", minElementSize: " << elementType->minWireSize() << ", fixedSize: "
                    << (elementType->isVariableLength() ? "false" : "true")
                    << ", " << decodeFunc(elementType, scope)
                    << ") is global::System.Collections.Generic.ICollection<" << typeToString(elementType, scope)
                    << "> " << tmpName << " ? new " << typeToString(seq, scope, false, true) << "(" << tmpName << ")"
                    << " : null";
            }
        }
        else
        {
            if (auto optional = OptionalPtr::dynamicCast(elementType); optional && optional->encodedUsingBitSequence())
            {
                TypePtr underlying = optional->underlying();
                out << "decoder.DecodeTaggedArray(" << tag << ", "
                    << (isReferenceType(underlying) ? "withBitSequence: true, " : "")
                    << decodeFunc(underlying, scope) << ")";
            }
            else
            {
                out << "decoder.DecodeTaggedArray(" << tag << ", minElementSize: " << elementType->minWireSize()
                    << ", fixedSize: " << (elementType->isVariableLength() ? "false" : "true")
                    << ", " << decodeFunc(elementType, scope) << ")";
            }
        }
    }
    else
    {
        DictionaryPtr d = DictionaryPtr::dynamicCast(type);
        assert(d);
        TypePtr keyType = d->keyType();
        TypePtr valueType = d->valueType();
        bool withBitSequence = false;

        if (auto optional = OptionalPtr::dynamicCast(valueType); optional && optional->encodedUsingBitSequence())
        {
            withBitSequence = true;
            valueType = optional->underlying();
        }

        bool fixedSize = !keyType->isVariableLength() && !valueType->isVariableLength();
        bool sorted = d->findMetadataWithPrefix("cs:generic:") == "SortedDictionary";

        out << "decoder.DecodeTagged" << (sorted ? "Sorted" : "") << "Dictionary(" << tag
            << ", minKeySize: " << keyType->minWireSize();
        if (!withBitSequence)
        {
            out << ", minValueSize: " << valueType->minWireSize();
        }
        if (withBitSequence && isReferenceType(valueType))
        {
            out << ", withBitSequence: true";
        }
        if (!withBitSequence)
        {
            out << ", fixedSize: " << (fixedSize ? "true" : "false");
        }
        out << ", " << decodeFunc(keyType, scope) << ", " << decodeFunc(valueType, scope) << ")";
    }
    out << ";";
}

string
Slice::CsGenerator::sequenceMarshalCode(
    const SequencePtr& seq,
    const string& scope,
    const string& value,
    bool readOnly,
    bool isParam)
{
    TypePtr type = seq->type();
    ostringstream out;

    bool hasCustomType = seq->hasMetadataWithPrefix("cs:generic");

    if (isFixedSizeNumericSequence(seq) && (readOnly || !hasCustomType))
    {
        if (isParam && readOnly && !hasCustomType)
        {
            out << "encoder.EncodeSequence(" << value << ".Span)";
        }
        else
        {
            // value is an IEnumerable<T>
            out << "encoder.EncodeSequence(" << value << ")";
        }
    }
    else if (auto optional = OptionalPtr::dynamicCast(type); optional && optional->encodedUsingBitSequence())
    {
        TypePtr underlying = optional->underlying();
        out << "encoder.EncodeSequence(" << value;
        if (isReferenceType(underlying))
        {
            out << ", withBitSequence: true";
        }
        out << ", " << encodeAction(underlying, scope, readOnly) << ")";
    }
    else
    {
        out << "encoder.EncodeSequence(" << value << ", " << encodeAction(type, scope, readOnly) << ")";
    }
    return out.str();
}

string
Slice::CsGenerator::sequenceUnmarshalCode(const SequencePtr& seq, const string& scope)
{
    string generic = seq->findMetadataWithPrefix("cs:generic:");

    TypePtr type = seq->type();
    BuiltinPtr builtin = BuiltinPtr::dynamicCast(type);
    auto en = EnumPtr::dynamicCast(type);

    ostringstream out;
    if (generic.empty())
    {
        if ((builtin && builtin->isNumericTypeOrBool() && !builtin->isVariableLength()) ||
            (en && en->underlying() && en->isUnchecked()))
        {
            out << "decoder.DecodeArray<" << typeToString(type, scope) << ">()";
        }
        else if (en && en->underlying())
        {
            out << "decoder.DecodeArray((" << typeToString(en, scope) << " e) => _ = " << helperName(en, scope)
                << ".As" << en->name() << "((" << typeToString(en->underlying(), scope) << ")e))";
        }
        else if (auto optional = OptionalPtr::dynamicCast(type); optional && optional->encodedUsingBitSequence())
        {
            TypePtr underlying = optional->underlying();
            out << "decoder.DecodeArray(" << (isReferenceType(underlying) ? "withBitSequence: true, " : "")
                << decodeFunc(underlying, scope) << ")";
        }
        else
        {
            out << "decoder.DecodeArray(minElementSize: " << type->minWireSize() << ", "
                << decodeFunc(type, scope) << ")";
        }
    }
    else
    {
        out << "new " << typeToString(seq, scope, false, true) << "(";
        if (generic == "Stack")
        {
            out << "global::System.Linq.Enumerable.Reverse(";
        }

        if ((builtin && builtin->isNumericTypeOrBool() && !builtin->isVariableLength()) ||
            (en && en->underlying() && en->isUnchecked()))
        {
            // We always read an array even when mapped to a collection, as it's expected to be faster than unmarshaling
            // the collection elements one by one.
            out << "decoder.DecodeArray<" << typeToString(type, scope) << ">()";
        }
        else if (en && en->underlying())
        {
            out << "decoder.DecodeArray((" << typeToString(en, scope) << " e) => _ = "
                << helperName(en, scope) << ".As" << en->name()
                << "((" << typeToString(en->underlying(), scope) << ")e))";
        }
        else if (auto optional = OptionalPtr::dynamicCast(type); optional && optional->encodedUsingBitSequence())
        {
            TypePtr underlying = optional->underlying();
            out << "decoder.DecodeSequence(" << (isReferenceType(underlying) ? "withBitSequence: true, " : "")
                << decodeFunc(underlying, scope) << ")";
        }
        else
        {
            out << "decoder.DecodeSequence(minElementSize: " << type->minWireSize() << ", "
                << decodeFunc(type, scope) << ")";
        }

        if (generic == "Stack")
        {
            out << ")";
        }
        out << ")";
    }
    return out.str();
}

string
Slice::CsGenerator::dictionaryMarshalCode(const DictionaryPtr& dict, const string& scope, const string& param)
{
    TypePtr key = dict->keyType();
    TypePtr value = dict->valueType();

    bool withBitSequence = false;
    if (auto optional = OptionalPtr::dynamicCast(value); optional && optional->encodedUsingBitSequence())
    {
        withBitSequence = true;
        value = optional->underlying();
    }

    ostringstream out;

    out << "encoder.EncodeDictionary(" << param;
    if (withBitSequence && isReferenceType(value))
    {
        out << ", withBitSequence: true";
    }
    out << ", " << encodeAction(key, scope)
        << ", " << encodeAction(value, scope) << ")";
    return out.str();
}

string
Slice::CsGenerator::dictionaryUnmarshalCode(const DictionaryPtr& dict, const string& scope)
{
    TypePtr key = dict->keyType();
    TypePtr value = dict->valueType();
    string generic = dict->findMetadataWithPrefix("cs:generic:");
    string dictS = typeToString(dict, scope);

    bool withBitSequence = false;
    if (auto optional = OptionalPtr::dynamicCast(value); optional && optional->encodedUsingBitSequence())
    {
        withBitSequence = true;
        value = optional->underlying();
    }

    ostringstream out;
    out << "decoder.";
    out << (generic == "SortedDictionary" ? "DecodeSortedDictionary(" : "DecodeDictionary(");
    out << "minKeySize: " << key->minWireSize() << ", ";
    if (!withBitSequence)
    {
        out << "minValueSize: " << value->minWireSize() << ", ";
    }
    if (withBitSequence && isReferenceType(value))
    {
        out << "withBitSequence: true, ";
    }
    out << decodeFunc(key, scope) << ", " << decodeFunc(value, scope);
    if (SequencePtr::dynamicCast(value) || DictionaryPtr::dynamicCast(value))
    {
        out << " as " << typeToString(value, scope);
    }
    out << ")";
    return out.str();
}

void
Slice::CsGenerator::writeConstantValue(Output& out, const TypePtr& type, const SyntaxTreeBasePtr& valueType,
    const string& value, const string& ns)
{
    ConstPtr constant = ConstPtr::dynamicCast(valueType);
    if (constant)
    {
        out << getUnqualified(constant, ns, "Constants.");
    }
    else
    {
        TypePtr underlying = unwrapIfOptional(type);

        if (auto builtin = BuiltinPtr::dynamicCast(underlying))
        {
            switch (builtin->kind())
            {
                case Builtin::KindString:
                    out << "\"" << toStringLiteral(value, "\a\b\f\n\r\t\v\0", "", UCN, 0) << "\"";
                    break;
                case Builtin::KindUShort:
                case Builtin::KindUInt:
                case Builtin::KindVarUInt:
                    out << value << "U";
                    break;
                case Builtin::KindLong:
                case Builtin::KindVarLong:
                    out << value << "L";
                    break;
                case Builtin::KindULong:
                case Builtin::KindVarULong:
                    out << value << "UL";
                    break;
                case Builtin::KindFloat:
                    out << value << "F";
                    break;
                case Builtin::KindDouble:
                    out << value << "D";
                    break;
                default:
                    out << value;
            }
        }
        else if (EnumPtr::dynamicCast(underlying))
        {
            EnumeratorPtr lte = EnumeratorPtr::dynamicCast(valueType);
            assert(lte);
            out << fixId(lte->scoped());
        }
        else
        {
            out << value;
        }
    }
}

void
Slice::CsGenerator::validateMetadata(const UnitPtr& u)
{
    MetadataVisitor visitor;
    u->visit(&visitor, true);
}

bool
Slice::CsGenerator::MetadataVisitor::visitUnitStart(const UnitPtr& p)
{
    //
    // Validate global metadata in the top-level file and all included files.
    //
    StringList files = p->allFiles();
    for(StringList::iterator q = files.begin(); q != files.end(); ++q)
    {
        string file = *q;
        DefinitionContextPtr dc = p->findDefinitionContext(file);
        assert(dc);
        StringList globalMetadata = dc->getAllMetadata();
        StringList newGlobalMetadata;
        static const string csPrefix = "cs:";
        static const string clrPrefix = "clr:";

        for(StringList::iterator r = globalMetadata.begin(); r != globalMetadata.end(); ++r)
        {
            string& s = *r;
            string oldS = s;

            if(s.find(clrPrefix) == 0)
            {
                s.replace(0, clrPrefix.size(), csPrefix);
            }

            if(s.find(csPrefix) == 0)
            {
                static const string csAttributePrefix = csPrefix + "attribute:";
                if(!(s.find(csAttributePrefix) == 0 && s.size() > csAttributePrefix.size()))
                {
                    dc->warning(InvalidMetadata, file, -1, "ignoring invalid global metadata '" + oldS + "'");
                    continue;
                }
            }
            newGlobalMetadata.push_back(oldS);
        }

        dc->setMetadata(newGlobalMetadata);
    }
    return true;
}

bool
Slice::CsGenerator::MetadataVisitor::visitModuleStart(const ModulePtr& p)
{
    validate(p);
    return true;
}

void
Slice::CsGenerator::MetadataVisitor::visitModuleEnd(const ModulePtr&)
{
}

void
Slice::CsGenerator::MetadataVisitor::visitClassDecl(const ClassDeclPtr& p)
{
    validate(p);
}

bool
Slice::CsGenerator::MetadataVisitor::visitClassDefStart(const ClassDefPtr& p)
{
    validate(p);
    return true;
}

void
Slice::CsGenerator::MetadataVisitor::visitClassDefEnd(const ClassDefPtr&)
{
}

bool
Slice::CsGenerator::MetadataVisitor::visitExceptionStart(const ExceptionPtr& p)
{
    validate(p);
    return true;
}

void
Slice::CsGenerator::MetadataVisitor::visitExceptionEnd(const ExceptionPtr&)
{
}

bool
Slice::CsGenerator::MetadataVisitor::visitStructStart(const StructPtr& p)
{
    validate(p);
    return true;
}

void
Slice::CsGenerator::MetadataVisitor::visitStructEnd(const StructPtr&)
{
}

void
Slice::CsGenerator::MetadataVisitor::visitOperation(const OperationPtr& p)
{
    validate(p);
    for (const auto& param : p->allMembers())
    {
        visitParameter(param);
    }
}

void
Slice::CsGenerator::MetadataVisitor::visitParameter(const MemberPtr& p)
{
    validate(p);
}

void
Slice::CsGenerator::MetadataVisitor::visitDataMember(const MemberPtr& p)
{
    validate(p);
}

void
Slice::CsGenerator::MetadataVisitor::visitSequence(const SequencePtr& p)
{
    validate(p);
}

void
Slice::CsGenerator::MetadataVisitor::visitDictionary(const DictionaryPtr& p)
{
    validate(p);
}

void
Slice::CsGenerator::MetadataVisitor::visitEnum(const EnumPtr& p)
{
    validate(p);
}

void
Slice::CsGenerator::MetadataVisitor::visitConst(const ConstPtr& p)
{
    validate(p);
}

void
Slice::CsGenerator::MetadataVisitor::validate(const ContainedPtr& cont)
{
    const string msg = "ignoring invalid metadata";

    StringList localMetadata = cont->getAllMetadata();
    StringList newLocalMetadata;

    const UnitPtr ut = cont->unit();
    const DefinitionContextPtr dc = ut->findDefinitionContext(cont->file());
    assert(dc);

    for(StringList::iterator p = localMetadata.begin(); p != localMetadata.end(); ++p)
    {
        string& s = *p;
        string oldS = s;

        const string csPrefix = "cs:";
        const string clrPrefix = "clr:";

        if(s.find(clrPrefix) == 0)
        {
            s.replace(0, clrPrefix.size(), csPrefix);
        }

        if(s.find(csPrefix) == 0)
        {
            SequencePtr seq = SequencePtr::dynamicCast(cont);
            if(seq)
            {
                static const string csGenericPrefix = csPrefix + "generic:";
                if(s.find(csGenericPrefix) == 0)
                {
                    string type = s.substr(csGenericPrefix.size());
                    if(!type.empty())
                    {
                        newLocalMetadata.push_back(s);
                        continue; // Custom type or List<T>
                    }
                }
            }
            else if(StructPtr::dynamicCast(cont))
            {
                if (s == "cs:readonly" || s == "cs:custom-equals")
                {
                    newLocalMetadata.push_back(s);
                    continue;
                }
            }
            else if(DictionaryPtr::dynamicCast(cont))
            {
                static const string csGenericPrefix = csPrefix + "generic:";
                if(s.find(csGenericPrefix) == 0)
                {
                    string type = s.substr(csGenericPrefix.size());
                    if(type == "SortedDictionary" ||  type == "SortedList")
                    {
                        newLocalMetadata.push_back(s);
                        continue;
                    }
                }
            }
            else if(ModulePtr::dynamicCast(cont))
            {
                static const string csNamespacePrefix = csPrefix + "namespace:";
                if(s.find(csNamespacePrefix) == 0 && s.size() > csNamespacePrefix.size())
                {
                    newLocalMetadata.push_back(s);
                    continue;
                }
            }

            static const string csAttributePrefix = csPrefix + "attribute:";
            if(s.find(csAttributePrefix) == 0 && s.size() > csAttributePrefix.size())
            {
                newLocalMetadata.push_back(s);
                continue;
            }

            dc->warning(InvalidMetadata, cont->file(), cont->line(), msg + " '" + oldS + "'");
            continue;
        }
        newLocalMetadata.push_back(s);
    }

    cont->setMetadata(newLocalMetadata);
}

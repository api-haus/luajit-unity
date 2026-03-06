using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LuaGameCodegen
{
    [Generator]
    public class LuaBridgeGenerator : IIncrementalGenerator
    {
        const string LuaBridgeAttrShort = "LuaBridge";
        const string LuaBridgeAttrFull = "LuaECS.Core.LuaBridgeAttribute";
        const string LuaBridgeForAttrShort = "LuaBridgeFor";
        const string LuaBridgeForAttrFull = "LuaECS.Core.LuaBridgeForAttribute";
        const string IComponentDataFull = "Unity.Entities.IComponentData";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // [LuaBridge] on structs
            var structTargets = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsStructWithLuaBridgeAttr(node),
                    transform: static (ctx, ct) => ExtractFromStruct(ctx, ct))
                .Where(static x => x != null)
                .Select(static (x, _) => x.Value);

            context.RegisterSourceOutput(structTargets, GenerateBridge);

            // [assembly: LuaBridgeFor(...)] on assembly
            var assemblyTargets = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsAssemblyLuaBridgeForAttr(node),
                    transform: static (ctx, ct) => ExtractFromAssemblyAttribute(ctx, ct))
                .Where(static x => x != null)
                .Select(static (x, _) => x.Value);

            context.RegisterSourceOutput(assemblyTargets, GenerateBridge);
        }

        // ── Syntax predicates ───────────────────────────────────────────

        static bool IsStructWithLuaBridgeAttr(SyntaxNode node)
        {
            if (node is not StructDeclarationSyntax s)
                return false;
            foreach (var attrList in s.AttributeLists)
            {
                foreach (var attr in attrList.Attributes)
                {
                    var name = attr.Name.ToString();
                    if (name == LuaBridgeAttrShort || name == "LuaBridgeAttribute"
                        || name.EndsWith(".LuaBridge") || name.EndsWith(".LuaBridgeAttribute"))
                        return true;
                }
            }
            return false;
        }

        static bool IsAssemblyLuaBridgeForAttr(SyntaxNode node)
        {
            if (node is not AttributeSyntax attr)
                return false;
            if (attr.Parent is not AttributeListSyntax attrList)
                return false;
            if (attrList.Target == null || attrList.Target.Identifier.Text != "assembly")
                return false;
            var name = attr.Name.ToString();
            return name == LuaBridgeForAttrShort || name == "LuaBridgeForAttribute"
                   || name.EndsWith(".LuaBridgeFor") || name.EndsWith(".LuaBridgeForAttribute");
        }

        // ── Semantic transforms ─────────────────────────────────────────

        static BridgeTarget? ExtractFromStruct(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var structSyntax = (StructDeclarationSyntax)ctx.Node;
            var symbol = ctx.SemanticModel.GetDeclaredSymbol(structSyntax, ct);
            if (symbol == null || !ImplementsIComponentData(symbol))
                return null;

            foreach (var attrData in symbol.GetAttributes())
            {
                if (attrData.AttributeClass?.ToDisplayString() != LuaBridgeAttrFull)
                    continue;

                var luaName = (string)null;
                var readOnly = false;

                if (attrData.ConstructorArguments.Length > 0)
                    luaName = attrData.ConstructorArguments[0].Value as string;
                if (attrData.ConstructorArguments.Length > 1 && attrData.ConstructorArguments[1].Value is bool b)
                    readOnly = b;

                if (string.IsNullOrEmpty(luaName))
                    luaName = SnakeCaseHelper.ToSnakeCase(symbol.Name);

                var fields = GetSupportedFields(symbol, readOnly);
                if (fields.Length == 0)
                    return null;

                return new BridgeTarget(symbol.Name, symbol.ToDisplayString(), luaName, readOnly, fields);
            }

            return null;
        }

        static BridgeTarget? ExtractFromAssemblyAttribute(GeneratorSyntaxContext ctx, CancellationToken ct)
        {
            var attrSyntax = (AttributeSyntax)ctx.Node;

            var attrSymbol = ctx.SemanticModel.GetSymbolInfo(attrSyntax, ct).Symbol;
            if (attrSymbol == null)
                return null;

            var containingType = attrSymbol.ContainingType?.ToDisplayString();
            if (containingType != LuaBridgeForAttrFull)
                return null;

            // Get attribute arguments from syntax since we can't easily get AttributeData here
            var args = attrSyntax.ArgumentList?.Arguments;
            if (args == null || args.Value.Count == 0)
                return null;

            // First arg should be typeof(T)
            if (args.Value[0].Expression is not TypeOfExpressionSyntax typeOfExpr)
                return null;

            var typeInfo = ctx.SemanticModel.GetTypeInfo(typeOfExpr.Type, ct);
            if (typeInfo.Type is not INamedTypeSymbol targetType)
                return null;

            if (!ImplementsIComponentData(targetType))
                return null;

            string luaName = null;
            var readOnly = false;

            // Parse remaining args (positional or named)
            for (var i = 1; i < args.Value.Count; i++)
            {
                var arg = args.Value[i];
                var constVal = ctx.SemanticModel.GetConstantValue(arg.Expression, ct);
                if (!constVal.HasValue)
                    continue;

                if (arg.NameEquals != null)
                {
                    var propName = arg.NameEquals.Name.Identifier.Text;
                    if (propName == "luaName" && constVal.Value is string s)
                        luaName = s;
                    else if (propName == "readOnly" && constVal.Value is bool rb)
                        readOnly = rb;
                }
                else
                {
                    // positional
                    if (i == 1 && constVal.Value is string s)
                        luaName = s;
                    else if (i == 2 && constVal.Value is bool rb)
                        readOnly = rb;
                }
            }

            if (string.IsNullOrEmpty(luaName))
                luaName = SnakeCaseHelper.ToSnakeCase(targetType.Name);

            var fields = GetSupportedFields(targetType, readOnly);
            if (fields.Length == 0)
                return null;

            return new BridgeTarget(targetType.Name, targetType.ToDisplayString(), luaName, readOnly, fields);
        }

        // ── Helpers ─────────────────────────────────────────────────────

        static bool ImplementsIComponentData(INamedTypeSymbol symbol)
        {
            foreach (var iface in symbol.AllInterfaces)
            {
                if (iface.ToDisplayString() == IComponentDataFull)
                    return true;
            }
            return false;
        }

        static ImmutableArray<FieldInfo> GetSupportedFields(INamedTypeSymbol type, bool readOnly)
        {
            var builder = ImmutableArray.CreateBuilder<FieldInfo>();
            foreach (var member in type.GetMembers())
            {
                if (!(member is IFieldSymbol field))
                    continue;
                if (field.IsStatic || field.IsConst || field.DeclaredAccessibility != Accessibility.Public)
                    continue;
                var luaType = MapFieldType(field.Type);
                if (luaType == LuaFieldType.Unsupported)
                    continue;
                builder.Add(new FieldInfo(field.Name, SnakeCaseHelper.ToSnakeCase(field.Name), luaType, readOnly));
            }
            return builder.ToImmutable();
        }

        static LuaFieldType MapFieldType(ITypeSymbol type)
        {
            var name = type.ToDisplayString();
            switch (name)
            {
                case "float": return LuaFieldType.Float;
                case "int": return LuaFieldType.Int;
                case "bool": return LuaFieldType.Bool;
                case "Unity.Mathematics.float2": return LuaFieldType.Float2;
                case "Unity.Mathematics.float3": return LuaFieldType.Float3;
                case "Unity.Mathematics.float4": return LuaFieldType.Float4;
                case "Unity.Mathematics.quaternion": return LuaFieldType.Quaternion;
                default: return LuaFieldType.Unsupported;
            }
        }

        // ── Code generation ─────────────────────────────────────────────

        static void GenerateBridge(SourceProductionContext ctx, BridgeTarget target)
        {
            var sb = new StringBuilder();
            var typeName = target.TypeName;
            var fullTypeName = target.FullTypeName;
            var luaName = target.LuaName;
            var readOnly = target.ReadOnly;
            var fields = target.Fields;
            var className = "Lua" + typeName + "Bridge";

            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("namespace LuaECS.Generated");
            sb.AppendLine("{");
            sb.AppendLine("\tusing AOT;");
            sb.AppendLine("\tusing Core;");
            sb.AppendLine("\tusing LuaNET.LuaJIT;");
            sb.AppendLine("\tusing Unity.Burst;");
            sb.AppendLine("\tusing Unity.Entities;");
            sb.AppendLine("\tusing Unity.Mathematics;");
            sb.AppendLine();

            sb.AppendLine("\tpublic static class " + className);
            sb.AppendLine("\t{");
            sb.AppendLine("\t\tstruct LookupMarker_" + typeName + " { }");
            sb.AppendLine();
            sb.AppendLine("\t\tstatic readonly SharedStatic<ComponentLookup<" + fullTypeName + ">> s_lookup =");
            sb.AppendLine("\t\t\tSharedStatic<ComponentLookup<" + fullTypeName + ">>.GetOrCreate<LookupMarker_" + typeName + ", ComponentLookup<" + fullTypeName + ">>();");
            sb.AppendLine();

            // Auto-register
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("\t\t[UnityEditor.InitializeOnLoadMethod]");
            sb.AppendLine("#endif");
            sb.AppendLine("\t\t[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine("\t\tstatic void AutoRegister()");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\tLuaComponentRegistry.RegisterBridge(");
            sb.AppendLine("\t\t\t\t\"" + luaName + "\",");
            sb.AppendLine("\t\t\t\tComponentType.ReadWrite<" + fullTypeName + ">(),");
            sb.AppendLine("\t\t\t\tRegister,");
            sb.AppendLine("\t\t\t\tUpdateLookup");
            sb.AppendLine("\t\t\t);");
            sb.AppendLine("\t\t}");
            sb.AppendLine();

            // Register
            sb.AppendLine("\t\tpublic static void Register(lua_State l)");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\tLua.lua_newtable(l);");
            foreach (var field in fields)
            {
                sb.AppendLine("\t\t\tLua.lua_pushcfunction(l, Get_" + field.Name + ");");
                sb.AppendLine("\t\t\tLua.lua_setfield(l, -2, \"get_" + field.LuaName + "\");");
                if (!field.IsReadOnly)
                {
                    sb.AppendLine("\t\t\tLua.lua_pushcfunction(l, Set_" + field.Name + ");");
                    sb.AppendLine("\t\t\tLua.lua_setfield(l, -2, \"set_" + field.LuaName + "\");");
                }
            }
            sb.AppendLine("\t\t\tLua.lua_setglobal(l, \"" + luaName + "\");");
            sb.AppendLine("\t\t}");
            sb.AppendLine();

            // UpdateLookup
            sb.AppendLine("\t\tpublic static void UpdateLookup(ref SystemState state)");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\ts_lookup.Data = state.GetComponentLookup<" + fullTypeName + ">(" + (readOnly ? "true" : "false") + ");");
            sb.AppendLine("\t\t}");
            sb.AppendLine();

            // Helpers
            EmitHelpers(sb, fields);

            // Getters/setters
            foreach (var field in fields)
            {
                EmitGetter(sb, field);
                if (!field.IsReadOnly)
                    EmitSetter(sb, field);
            }

            sb.AppendLine("\t}");
            sb.AppendLine("}");

            ctx.AddSource(className + ".g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        static void EmitHelpers(StringBuilder sb, ImmutableArray<FieldInfo> fields)
        {
            var needsFloat2 = fields.Any(f => f.Type == LuaFieldType.Float2);
            var needsFloat3 = fields.Any(f => f.Type == LuaFieldType.Float3);
            var needsFloat4 = fields.Any(f => f.Type == LuaFieldType.Float4);
            var needsQuat = fields.Any(f => f.Type == LuaFieldType.Quaternion);

            if (needsFloat2)
            {
                sb.AppendLine("\t\tstatic void PushFloat2(lua_State l, float2 v)");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\tLua.lua_newtable(l);");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, v.x); Lua.lua_setfield(l, -2, \"x\");");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, v.y); Lua.lua_setfield(l, -2, \"y\");");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
                sb.AppendLine("\t\tstatic float2 ReadFloat2(lua_State l, int idx)");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\tvar r = float2.zero;");
                sb.AppendLine("\t\t\tif (idx < 0) idx = Lua.lua_gettop(l) + idx + 1;");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"x\"); if (Lua.lua_isnumber(l, -1) != 0) r.x = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"y\"); if (Lua.lua_isnumber(l, -1) != 0) r.y = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\treturn r;");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            if (needsFloat3)
            {
                sb.AppendLine("\t\tstatic void PushFloat3(lua_State l, float3 v)");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\tLua.lua_newtable(l);");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, v.x); Lua.lua_setfield(l, -2, \"x\");");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, v.y); Lua.lua_setfield(l, -2, \"y\");");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, v.z); Lua.lua_setfield(l, -2, \"z\");");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
                sb.AppendLine("\t\tstatic float3 ReadFloat3(lua_State l, int idx)");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\tvar r = float3.zero;");
                sb.AppendLine("\t\t\tif (idx < 0) idx = Lua.lua_gettop(l) + idx + 1;");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"x\"); if (Lua.lua_isnumber(l, -1) != 0) r.x = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"y\"); if (Lua.lua_isnumber(l, -1) != 0) r.y = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"z\"); if (Lua.lua_isnumber(l, -1) != 0) r.z = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\treturn r;");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            if (needsFloat4)
            {
                sb.AppendLine("\t\tstatic void PushFloat4(lua_State l, float4 v)");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\tLua.lua_newtable(l);");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, v.x); Lua.lua_setfield(l, -2, \"x\");");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, v.y); Lua.lua_setfield(l, -2, \"y\");");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, v.z); Lua.lua_setfield(l, -2, \"z\");");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, v.w); Lua.lua_setfield(l, -2, \"w\");");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
                sb.AppendLine("\t\tstatic float4 ReadFloat4(lua_State l, int idx)");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\tvar r = float4.zero;");
                sb.AppendLine("\t\t\tif (idx < 0) idx = Lua.lua_gettop(l) + idx + 1;");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"x\"); if (Lua.lua_isnumber(l, -1) != 0) r.x = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"y\"); if (Lua.lua_isnumber(l, -1) != 0) r.y = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"z\"); if (Lua.lua_isnumber(l, -1) != 0) r.z = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"w\"); if (Lua.lua_isnumber(l, -1) != 0) r.w = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\treturn r;");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            if (needsQuat)
            {
                sb.AppendLine("\t\tstatic void PushQuaternion(lua_State l, quaternion q)");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\tLua.lua_newtable(l);");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, q.value.x); Lua.lua_setfield(l, -2, \"x\");");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, q.value.y); Lua.lua_setfield(l, -2, \"y\");");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, q.value.z); Lua.lua_setfield(l, -2, \"z\");");
                sb.AppendLine("\t\t\tLua.lua_pushnumber(l, q.value.w); Lua.lua_setfield(l, -2, \"w\");");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
                sb.AppendLine("\t\tstatic quaternion ReadQuaternion(lua_State l, int idx)");
                sb.AppendLine("\t\t{");
                sb.AppendLine("\t\t\tvar r = quaternion.identity;");
                sb.AppendLine("\t\t\tif (idx < 0) idx = Lua.lua_gettop(l) + idx + 1;");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"x\"); if (Lua.lua_isnumber(l, -1) != 0) r.value.x = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"y\"); if (Lua.lua_isnumber(l, -1) != 0) r.value.y = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"z\"); if (Lua.lua_isnumber(l, -1) != 0) r.value.z = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\tLua.lua_getfield(l, idx, \"w\"); if (Lua.lua_isnumber(l, -1) != 0) r.value.w = (float)Lua.lua_tonumber(l, -1); Lua.lua_pop(l, 1);");
                sb.AppendLine("\t\t\treturn r;");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }
        }

        static string GetPushCode(LuaFieldType type, string expr)
        {
            switch (type)
            {
                case LuaFieldType.Float: return "Lua.lua_pushnumber(l, " + expr + ");";
                case LuaFieldType.Int: return "Lua.lua_pushinteger(l, " + expr + ");";
                case LuaFieldType.Bool: return "Lua.lua_pushboolean(l, " + expr + " ? 1 : 0);";
                case LuaFieldType.Float2: return "PushFloat2(l, " + expr + ");";
                case LuaFieldType.Float3: return "PushFloat3(l, " + expr + ");";
                case LuaFieldType.Float4: return "PushFloat4(l, " + expr + ");";
                case LuaFieldType.Quaternion: return "PushQuaternion(l, " + expr + ");";
                default: return "Lua.lua_pushnil(l);";
            }
        }

        static string GetReadCode(LuaFieldType type, int idx)
        {
            switch (type)
            {
                case LuaFieldType.Float: return "(float)Lua.lua_tonumber(l, " + idx + ")";
                case LuaFieldType.Int: return "(int)Lua.lua_tointeger(l, " + idx + ")";
                case LuaFieldType.Bool: return "Lua.lua_toboolean(l, " + idx + ") != 0";
                case LuaFieldType.Float2: return "ReadFloat2(l, " + idx + ")";
                case LuaFieldType.Float3: return "ReadFloat3(l, " + idx + ")";
                case LuaFieldType.Float4: return "ReadFloat4(l, " + idx + ")";
                case LuaFieldType.Quaternion: return "ReadQuaternion(l, " + idx + ")";
                default: return "default";
            }
        }

        static void EmitGetter(StringBuilder sb, FieldInfo field)
        {
            sb.AppendLine("\t\t[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]");
            sb.AppendLine("\t\tstatic int Get_" + field.Name + "(lua_State l)");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\tvar entityId = (int)Lua.lua_tointeger(l, 1);");
            sb.AppendLine("\t\t\tvar entity = LuaECSBridge.GetEntityFromIdBurst(entityId);");
            sb.AppendLine("\t\t\tif (entity == Entity.Null || !s_lookup.Data.HasComponent(entity))");
            sb.AppendLine("\t\t\t{");
            sb.AppendLine("\t\t\t\tLua.lua_pushnil(l);");
            sb.AppendLine("\t\t\t\treturn 1;");
            sb.AppendLine("\t\t\t}");
            sb.AppendLine("\t\t\tvar comp = s_lookup.Data[entity];");
            sb.AppendLine("\t\t\t" + GetPushCode(field.Type, "comp." + field.Name));
            sb.AppendLine("\t\t\treturn 1;");
            sb.AppendLine("\t\t}");
            sb.AppendLine();
        }

        static void EmitSetter(StringBuilder sb, FieldInfo field)
        {
            sb.AppendLine("\t\t[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]");
            sb.AppendLine("\t\tstatic int Set_" + field.Name + "(lua_State l)");
            sb.AppendLine("\t\t{");
            sb.AppendLine("\t\t\tvar entityId = (int)Lua.lua_tointeger(l, 1);");
            sb.AppendLine("\t\t\tvar entity = LuaECSBridge.GetEntityFromIdBurst(entityId);");
            sb.AppendLine("\t\t\tif (entity == Entity.Null || !s_lookup.Data.HasComponent(entity))");
            sb.AppendLine("\t\t\t\treturn 0;");
            sb.AppendLine("\t\t\tvar comp = s_lookup.Data[entity];");
            sb.AppendLine("\t\t\tcomp." + field.Name + " = " + GetReadCode(field.Type, 2) + ";");
            sb.AppendLine("\t\t\ts_lookup.Data[entity] = comp;");
            sb.AppendLine("\t\t\treturn 0;");
            sb.AppendLine("\t\t}");
            sb.AppendLine();
        }

        // ── Data types (plain structs for netstandard2.0) ───────────────

        struct BridgeTarget
        {
            public string TypeName;
            public string FullTypeName;
            public string LuaName;
            public bool ReadOnly;
            public ImmutableArray<FieldInfo> Fields;

            public BridgeTarget(string typeName, string fullTypeName, string luaName, bool readOnly, ImmutableArray<FieldInfo> fields)
            {
                TypeName = typeName;
                FullTypeName = fullTypeName;
                LuaName = luaName;
                ReadOnly = readOnly;
                Fields = fields;
            }
        }

        struct FieldInfo
        {
            public string Name;
            public string LuaName;
            public LuaFieldType Type;
            public bool IsReadOnly;

            public FieldInfo(string name, string luaName, LuaFieldType type, bool isReadOnly)
            {
                Name = name;
                LuaName = luaName;
                Type = type;
                IsReadOnly = isReadOnly;
            }
        }

        enum LuaFieldType
        {
            Float,
            Int,
            Bool,
            Float2,
            Float3,
            Float4,
            Quaternion,
            Unsupported,
        }
    }
}

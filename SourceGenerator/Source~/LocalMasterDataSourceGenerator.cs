using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace LocalMasterData.SourceGenerator
{
    [Generator]
    public sealed class LocalMasterDataSourceGenerator : ISourceGenerator
    {
        private const string AttributeMetadataName = "LocalMasterData.LocalMasterDataAttribute";
        private const string InterfaceMetadataName = "LocalMasterData.ILocalMasterDataTable";

        private static readonly DiagnosticDescriptor InvalidTable = new(
            "LMD001", "Invalid local master data table",
            "'{0}' must be a non-abstract, non-generic ILocalMasterDataTable with an accessible parameterless constructor",
            "LocalMasterData", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor InvalidRecord = new(
            "LMD004", "Invalid local master data record",
            "'{0}' must have public, non-indexed, readable and writable instance properties (init-only setters are not supported)",
            "LocalMasterData", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor InvalidSheetName = new(
            "LMD002", "Invalid local master data sheet name",
            "'{0}' has an empty local master data sheet name",
            "LocalMasterData", DiagnosticSeverity.Error, true);

        private static readonly DiagnosticDescriptor DuplicateSheetName = new(
            "LMD003", "Duplicate local master data sheet name",
            "Local master data sheet name '{0}' is used by both '{1}' and '{2}'",
            "LocalMasterData", DiagnosticSeverity.Error, true);

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(static () => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
            {
                return;
            }

            var attributeType = context.Compilation.GetTypeByMetadataName(AttributeMetadataName);
            var interfaceType = context.Compilation.GetTypeByMetadataName(InterfaceMetadataName);
            if (attributeType == null || interfaceType == null)
            {
                return;
            }

            var tables = new List<TableInfo>();
            foreach (var declaration in receiver.Candidates)
            {
                var semanticModel = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (!(semanticModel.GetDeclaredSymbol(declaration) is INamedTypeSymbol type))
                {
                    continue;
                }

                var attribute = type.GetAttributes()
                    .FirstOrDefault(x => SymbolEqualityComparer.Default.Equals(x.AttributeClass, attributeType));
                if (attribute == null)
                {
                    continue;
                }

                var sheetName = attribute.ConstructorArguments.Length == 1
                    ? attribute.ConstructorArguments[0].Value as string
                    : null;
                if (string.IsNullOrWhiteSpace(sheetName))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidSheetName, declaration.Identifier.GetLocation(), type.ToDisplayString()));
                    continue;
                }

                if (!IsValidTable(type, interfaceType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidTable, declaration.Identifier.GetLocation(), type.ToDisplayString()));
                    continue;
                }

                var recordBase = type.BaseType;
                while (recordBase != null && recordBase.OriginalDefinition.ToDisplayString() != "LocalMasterData.LocalMasterDataTable<TKey, TRecord, TTable>") recordBase = recordBase.BaseType;
                var invalid = false;
                for (var recordType = recordBase?.TypeArguments[1] as INamedTypeSymbol; recordType != null; recordType = recordType.BaseType)
                    foreach (var property in recordType.GetMembers().OfType<IPropertySymbol>())
                        if (property.DeclaredAccessibility == Accessibility.Public &&
                            (property.IsStatic || property.IsIndexer || property.GetMethod?.DeclaredAccessibility != Accessibility.Public ||
                             property.SetMethod?.DeclaredAccessibility != Accessibility.Public || property.SetMethod.IsInitOnly))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(InvalidRecord, declaration.Identifier.GetLocation(), property.ToDisplayString()));
                            invalid = true;
                        }
                if (invalid) continue;
                if (tables.Any(t => SymbolEqualityComparer.Default.Equals(t.Type, type))) continue;
                tables.Add(new TableInfo(sheetName!, type, declaration.Identifier.GetLocation()));
            }

            var hasDuplicate = false;
            foreach (var group in tables.GroupBy(x => x.SheetName, StringComparer.Ordinal))
            {
                var duplicateTables = group.ToArray();
                if (duplicateTables.Length < 2)
                {
                    continue;
                }

                hasDuplicate = true;
                for (var i = 1; i < duplicateTables.Length; i++)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        DuplicateSheetName, duplicateTables[i].Location, group.Key,
                        duplicateTables[0].Type.ToDisplayString(), duplicateTables[i].Type.ToDisplayString()));
                }
            }

            if (tables.Count == 0 || hasDuplicate)
            {
                return;
            }

            tables.Sort((a, b) => StringComparer.Ordinal.Compare(a.SheetName, b.SheetName));
            context.AddSource(
                "LocalMasterData.GeneratedRegistry.g.cs",
                SourceText.From(GenerateSource(context.Compilation.AssemblyName ?? "Unknown", tables), Encoding.UTF8));
        }

        private static bool IsValidTable(INamedTypeSymbol type, INamedTypeSymbol interfaceType)
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType)
            {
                return false;
            }

            if (!type.AllInterfaces.Any(x => SymbolEqualityComparer.Default.Equals(x, interfaceType)))
            {
                return false;
            }

            return type.InstanceConstructors.Any(constructor =>
                constructor.Parameters.Length == 0 &&
                (constructor.DeclaredAccessibility == Accessibility.Public ||
                 constructor.DeclaredAccessibility == Accessibility.Internal ||
                 constructor.DeclaredAccessibility == Accessibility.ProtectedOrInternal));
        }

        private static string GenerateSource(string assemblyName, IReadOnlyList<TableInfo> tables)
        {
            var source = new StringBuilder();
            source.AppendLine("// <auto-generated />");
            source.AppendLine("#nullable enable");
            source.AppendLine("internal static class __LocalMasterDataGeneratedRegistry");
            source.AppendLine("{");
            source.AppendLine("    [global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
            source.AppendLine("#if UNITY_EDITOR");
            source.AppendLine("    [global::UnityEditor.InitializeOnLoadMethod]");
            source.AppendLine("#endif");
            source.AppendLine("    private static void Register()");
            source.AppendLine("    {");
            source.Append("        global::LocalMasterData.LocalMasterDataReader.RegisterTableFactory(\"");
            source.Append(Escape(assemblyName));
            source.AppendLine("\", CreateTables);");
            source.AppendLine("    }");
            source.AppendLine();
            source.AppendLine("    private static global::System.Collections.Generic.IReadOnlyList<global::LocalMasterData.LocalMasterDataTableRegistration> CreateTables()");
            source.AppendLine("    {");
            foreach (var table in tables) AppendReader(source, table);
            source.AppendLine("        return new global::LocalMasterData.LocalMasterDataTableRegistration[]");
            source.AppendLine("        {");

            foreach (var table in tables)
            {
                var typeName = table.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                source.Append("            new global::LocalMasterData.LocalMasterDataTableRegistration(\"");
                source.Append(Escape(table.SheetName));
                source.Append("\", typeof(");
                source.Append(typeName);
                source.Append("), new ");
                source.Append(typeName);
                source.AppendLine("()),");
            }

            source.AppendLine("        };");
            source.AppendLine("    }");
            source.AppendLine("}");
            return source.ToString();
        }

        private static void AppendReader(StringBuilder source, TableInfo table)
        {
            var baseType = table.Type.BaseType;
            while (baseType != null && baseType.OriginalDefinition.ToDisplayString() != "LocalMasterData.LocalMasterDataTable<TKey, TRecord, TTable>") baseType = baseType.BaseType;
            if (baseType == null) return; // Custom interface implementations own their serializer.
            var record = (INamedTypeSymbol)baseType.TypeArguments[1];
            var properties = new List<IPropertySymbol>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (var current = record; current != null; current = current.BaseType)
                foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
                    if (!property.IsStatic && property.DeclaredAccessibility == Accessibility.Public && names.Add(property.Name))
                        properties.Add(property);

            source.Append("        ").Append(baseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(".RegisterGeneratedReader(new string[] { ");
            foreach (var property in properties) source.Append("\"").Append(Escape(property.Name)).Append("\", ");
            source.AppendLine("}, () => {");
            for (var i = 0; i < properties.Count; i++)
                if (ReadExpression(properties[i].Type) == null)
                    source.Append("            var read").Append(i).Append(" = global::LocalMasterData.LocalMasterDataResolver.GetReader<")
                        .Append(properties[i].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine(">();");
            source.Append("            return reader => new ").Append(record.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine(" {");
            for (var i = 0; i < properties.Count; i++)
                source.Append("                @").Append(properties[i].Name).Append(" = ")
                    .Append(ReadExpression(properties[i].Type) ?? ("read" + i + "(reader)")).AppendLine(",");
            source.AppendLine("            };\n        });");
        }

        private static string? ReadExpression(ITypeSymbol type)
        {
            if (type.TypeKind == TypeKind.Enum)
            {
                var underlying = ReadExpression(((INamedTypeSymbol)type).EnumUnderlyingType!);
                return underlying == null ? null : "(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" + underlying;
            }
            switch (type.SpecialType)
            {
                case SpecialType.System_Int16: return "reader.ReadInt16()";
                case SpecialType.System_Int32: return "reader.ReadInt32()";
                case SpecialType.System_Int64: return "reader.ReadInt64()";
                case SpecialType.System_UInt16: return "reader.ReadUInt16()";
                case SpecialType.System_UInt32: return "reader.ReadUInt32()";
                case SpecialType.System_UInt64: return "reader.ReadUInt64()";
                case SpecialType.System_Single: return "reader.ReadSingle()";
                case SpecialType.System_Double: return "reader.ReadDouble()";
                case SpecialType.System_Boolean: return "reader.ReadBoolean()";
                case SpecialType.System_String: return "reader.ReadString()";
                case SpecialType.System_DateTime: return "global::System.DateTime.FromBinary(reader.ReadInt64())";
                default: return null;
            }
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private sealed class SyntaxReceiver : ISyntaxReceiver
        {
            public List<ClassDeclarationSyntax> Candidates { get; } = new List<ClassDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                var declaration = syntaxNode as ClassDeclarationSyntax;
                if (declaration != null && declaration.AttributeLists.Count > 0)
                {
                    Candidates.Add(declaration);
                }
            }
        }

        private sealed class TableInfo
        {
            public TableInfo(string sheetName, INamedTypeSymbol type, Location location)
            {
                SheetName = sheetName;
                Type = type;
                Location = location;
            }

            public string SheetName { get; }
            public INamedTypeSymbol Type { get; }
            public Location Location { get; }
        }
    }
}

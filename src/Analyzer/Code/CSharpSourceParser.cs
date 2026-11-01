using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RefKind = UnityAriadne.Analyzer.Yaml.RefKind;

namespace UnityAriadne.Analyzer.Code;

/// <summary>
/// Syntax-level extraction from one .cs file (Phase 2): declared types, base lists,
/// field types (serialized and non-serialized), GetComponent&lt;T&gt;-family calls,
/// [RequireComponent], [CustomEditor(typeof(X))], and Resources.Load path literals.
/// Roslyn parse only — no compilation, no DLLs.
/// </summary>
/// <remarks>
/// Files are parsed with UNITY_EDITOR and the cumulative UNITY_*_OR_NEWER symbols
/// defined (fixture is Unity 6000.0), so <c>#if UNITY_EDITOR</c> branches are visible
/// and <c>#if !UNITY_EDITOR</c> runtime-only branches are not — a documented
/// approximation, not a bug.
/// </remarks>
public static class CSharpSourceParser
{
    private static readonly CSharpParseOptions ParseOptions =
        new(preprocessorSymbols: BuildPreprocessorSymbols());

    private static readonly HashSet<string> ComponentMethods = new(StringComparer.Ordinal)
    {
        "GetComponent", "GetComponents", "TryGetComponent",
        "GetComponentInChildren", "GetComponentsInChildren",
        "GetComponentInParent", "GetComponentsInParent",
        "AddComponent",
        "FindObjectOfType", "FindObjectsOfType", "FindObjectsOfTypeAll",
        "FindFirstObjectByType", "FindAnyObjectByType", "FindObjectsByType",
    };

    private static readonly HashSet<string> ResourceLoadMethods = new(StringComparer.Ordinal)
    {
        "Load", "LoadAsync", "LoadAll",
    };

    /// <summary>Static Addressables methods whose first argument is an address/label key.
    /// Instance calls on AssetReference fields take no key — the field itself is already
    /// an asset-layer m_AssetGUID edge.</summary>
    private static readonly HashSet<string> AddressablesLoadMethods = new(StringComparer.Ordinal)
    {
        "LoadAssetAsync", "LoadAssetsAsync", "LoadSceneAsync", "InstantiateAsync",
        "DownloadDependenciesAsync",
    };

    public static CSharpSourceModel Parse(string text)
    {
        var model = new CSharpSourceModel();
        var root = CSharpSyntaxTree.ParseText(text, ParseOptions).GetCompilationUnitRoot();

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case UsingDirectiveSyntax usingDirective:
                    AddUsing(usingDirective, model);
                    break;
                case BaseTypeDeclarationSyntax typeDeclaration:
                    AddDeclaredType(typeDeclaration, typeDeclaration.Identifier.Text, model);
                    if (typeDeclaration is TypeDeclarationSyntax { BaseList: not null } withBases)
                    {
                        AddBaseTypes(withBases, model);
                    }

                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    AddDeclaredType(delegateDeclaration, delegateDeclaration.Identifier.Text, model);
                    break;
                case FieldDeclarationSyntax field:
                    AddField(field, model);
                    break;
                case InvocationExpressionSyntax invocation:
                    AddInvocation(invocation, model);
                    break;
                case AttributeSyntax attribute:
                    AddAttribute(attribute, model);
                    break;
            }
        }

        return model;
    }

    private static void AddUsing(UsingDirectiveSyntax usingDirective, CSharpSourceModel model)
    {
        // "using static X.Y" imports members, not type names in scope — skip.
        if (usingDirective.StaticKeyword != default || usingDirective.NamespaceOrType is not NameSyntax name)
        {
            return;
        }

        if (usingDirective.Alias is not null)
        {
            model.UsingAliases[usingDirective.Alias.Name.Identifier.Text] = DottedText(name);
        }
        else
        {
            model.Usings.Add(DottedText(name));
        }
    }

    private static void AddDeclaredType(SyntaxNode declaration, string name, CSharpSourceModel model)
    {
        var (namespaceName, declaringTypes) = EnclosingScope(declaration);
        var fullName = string.Join(".",
            new[] { namespaceName, declaringTypes, name }.Where(p => p.Length > 0));
        model.DeclaredTypes.Add(new DeclaredType(name, fullName, namespaceName, declaringTypes.Length > 0));
    }

    private static void AddBaseTypes(TypeDeclarationSyntax typeDeclaration, CSharpSourceModel model)
    {
        foreach (var baseType in typeDeclaration.BaseList!.Types)
        {
            AddTypeNames(
                baseType.Type, RefKind.Inherits,
                $"{typeDeclaration.Identifier.Text} : {baseType.Type}", typeDeclaration, model);
        }
    }

    /// <summary>
    /// Every field yields a site; the kind encodes Unity's serialization rule at syntax
    /// level: public or [SerializeField]/[SerializeReference], never static/const/readonly
    /// → serialized_field; anything else → nonserialized_field (backlog item, was a
    /// Phase 2 scope-out).
    /// </summary>
    private static void AddField(FieldDeclarationSyntax field, CSharpSourceModel model)
    {
        var modifiers = field.Modifiers;
        var serialized = !modifiers.Any(SyntaxKind.StaticKeyword)
                         && !modifiers.Any(SyntaxKind.ConstKeyword)
                         && !modifiers.Any(SyntaxKind.ReadOnlyKeyword)
                         && (modifiers.Any(SyntaxKind.PublicKeyword)
                             || HasAttribute(field.AttributeLists, "SerializeField")
                             || HasAttribute(field.AttributeLists, "SerializeReference"));

        var owner = OwnerTypeName(field);
        foreach (var declarator in field.Declaration.Variables)
        {
            AddTypeNames(
                field.Declaration.Type,
                serialized ? RefKind.SerializedField : RefKind.NonserializedField,
                $"{owner}.{declarator.Identifier.Text}", field, model);
        }
    }

    private static void AddInvocation(InvocationExpressionSyntax invocation, CSharpSourceModel model)
    {
        var (methodName, targetExpression) = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => (member.Name, member.Expression),
            MemberBindingExpressionSyntax binding => (binding.Name, null), // obj?.GetComponent<T>()
            SimpleNameSyntax simple => (simple, (ExpressionSyntax?)null),
            _ => (null, null),
        };
        if (methodName is null)
        {
            return;
        }

        var identifier = methodName.Identifier.Text;
        if (ResourceLoadMethods.Contains(identifier) && IsResourcesClass(targetExpression))
        {
            AddResourcesLoad(invocation, identifier, model);
            return;
        }

        if (AddressablesLoadMethods.Contains(identifier) && IsAddressablesClass(targetExpression))
        {
            AddAddressablesLoad(invocation, identifier, model);
            return;
        }

        if (!ComponentMethods.Contains(identifier))
        {
            return;
        }

        var owner = OwnerTypeName(invocation);
        if (methodName is GenericNameSyntax generic)
        {
            foreach (var argument in generic.TypeArgumentList.Arguments)
            {
                AddTypeNames(argument, RefKind.GetComponent, $"{owner}.{generic}", invocation, model);
            }
        }
        else if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf)
        {
            AddTypeNames(
                typeOf.Type, RefKind.GetComponent,
                $"{owner}.{identifier}(typeof({typeOf.Type}))", invocation, model);
        }
    }

    private static void AddResourcesLoad(InvocationExpressionSyntax invocation, string methodName, CSharpSourceModel model)
    {
        if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var owner = OwnerTypeName(invocation);
            model.ResourceLoads.Add(new ResourceLoadSite(
                literal.Token.ValueText,
                $"{owner}.Resources.{methodName}(\"{literal.Token.ValueText}\")"));
        }
        else
        {
            model.NonLiteralResourceLoads++;
        }
    }

    private static void AddAttribute(AttributeSyntax attribute, CSharpSourceModel model)
    {
        var name = attribute.Name is QualifiedNameSyntax qualified
            ? qualified.Right.Identifier.Text
            : (attribute.Name as SimpleNameSyntax)?.Identifier.Text;
        if (attribute.ArgumentList is null)
        {
            return;
        }

        switch (name)
        {
            case "RequireComponent" or "RequireComponentAttribute":
            {
                var owner = OwnerTypeName(attribute);
                foreach (var argument in attribute.ArgumentList.Arguments)
                {
                    if (argument.Expression is TypeOfExpressionSyntax typeOf)
                    {
                        AddTypeNames(
                            typeOf.Type, RefKind.GetComponent,
                            $"{owner}.[RequireComponent(typeof({typeOf.Type}))]", attribute, model);
                    }
                }

                break;
            }

            // Backlog item (Phase 2 scope-out): only the first argument is the inspected
            // type — the optional second is editorForChildClasses. The URP-only
            // [CustomEditorForRenderPipeline] variant stays out of scope.
            case "CustomEditor" or "CustomEditorAttribute"
                when attribute.ArgumentList.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax inspected:
                AddTypeNames(
                    inspected.Type, RefKind.CustomEditor,
                    $"{OwnerTypeName(attribute)}.[CustomEditor(typeof({inspected.Type}))]", attribute, model);
                break;
        }
    }

    /// <summary>
    /// Collects every resolvable type name inside a type syntax: unwraps arrays,
    /// nullables, tuples and generic argument lists; skips predefined types.
    /// Qualified names become one dotted site (namespace or outer-type qualifier).
    /// </summary>
    private static void AddTypeNames(
        TypeSyntax type, string kind, string propertyPath, SyntaxNode site, CSharpSourceModel model)
    {
        switch (type)
        {
            case IdentifierNameSyntax identifier when identifier.Identifier.Text != "var":
                AddSite(identifier.Identifier.Text);
                break;
            case GenericNameSyntax generic:
                AddSite(generic.Identifier.Text);
                foreach (var argument in generic.TypeArgumentList.Arguments)
                {
                    AddTypeNames(argument, kind, propertyPath, site, model);
                }

                break;
            case QualifiedNameSyntax qualified:
                AddSite(DottedText(qualified));
                if (qualified.Right is GenericNameSyntax rightGeneric)
                {
                    foreach (var argument in rightGeneric.TypeArgumentList.Arguments)
                    {
                        AddTypeNames(argument, kind, propertyPath, site, model);
                    }
                }

                break;
            case AliasQualifiedNameSyntax aliasQualified: // global::Foo.Bar
                AddTypeNames(aliasQualified.Name, kind, propertyPath, site, model);
                break;
            case ArrayTypeSyntax array:
                AddTypeNames(array.ElementType, kind, propertyPath, site, model);
                break;
            case NullableTypeSyntax nullable:
                AddTypeNames(nullable.ElementType, kind, propertyPath, site, model);
                break;
            case TupleTypeSyntax tuple:
                foreach (var element in tuple.Elements)
                {
                    AddTypeNames(element.Type, kind, propertyPath, site, model);
                }

                break;
        }

        void AddSite(string typeText) =>
            model.TypeRefs.Add(new TypeRefSite(typeText, propertyPath, kind, EnclosingScope(site).Namespace));
    }

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> lists, string name) =>
        lists.SelectMany(list => list.Attributes).Any(attribute =>
        {
            var text = attribute.Name is QualifiedNameSyntax qualified
                ? qualified.Right.Identifier.Text
                : (attribute.Name as SimpleNameSyntax)?.Identifier.Text;
            return text == name || text == name + "Attribute";
        });

    private static void AddAddressablesLoad(InvocationExpressionSyntax invocation, string methodName, CSharpSourceModel model)
    {
        if (invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            var owner = OwnerTypeName(invocation);
            model.AddressablesLoads.Add(new AddressablesLoadSite(
                literal.Token.ValueText,
                $"{owner}.Addressables.{methodName}(\"{literal.Token.ValueText}\")"));
        }
        else
        {
            model.NonLiteralAddressablesLoads++;
        }
    }

    private static bool IsResourcesClass(ExpressionSyntax? expression)
    {
        var text = expression?.ToString();
        return text is "Resources" || text?.EndsWith(".Resources", StringComparison.Ordinal) == true;
    }

    private static bool IsAddressablesClass(ExpressionSyntax? expression)
    {
        var text = expression?.ToString();
        return text is "Addressables" || text?.EndsWith(".Addressables", StringComparison.Ordinal) == true;
    }

    /// <summary>Nearest enclosing type name, for property paths ("" outside any type).</summary>
    private static string OwnerTypeName(SyntaxNode node)
    {
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is BaseTypeDeclarationSyntax type)
            {
                return type.Identifier.Text;
            }
        }

        return "";
    }

    /// <summary>Dotted namespace chain and declaring-type chain around a node (either may be "").</summary>
    private static (string Namespace, string DeclaringTypes) EnclosingScope(SyntaxNode node)
    {
        var namespaceParts = new List<string>();
        var typeParts = new List<string>();
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            switch (ancestor)
            {
                case BaseTypeDeclarationSyntax type:
                    typeParts.Insert(0, type.Identifier.Text);
                    break;
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    namespaceParts.Insert(0, namespaceDeclaration.Name.ToString());
                    break;
            }
        }

        return (string.Join(".", namespaceParts), string.Join(".", typeParts));
    }

    private static string DottedText(NameSyntax name) => name switch
    {
        QualifiedNameSyntax qualified => DottedText(qualified.Left) + "." + qualified.Right.Identifier.Text,
        AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.Text,
        SimpleNameSyntax simple => simple.Identifier.Text,
        _ => name.ToString(),
    };

    /// <summary>
    /// UNITY_EDITOR plus the cumulative version symbols a Unity 6000.0 editor defines,
    /// so version-gated package code parses the way Unity compiles it.
    /// </summary>
    private static string[] BuildPreprocessorSymbols()
    {
        var symbols = new List<string> { "UNITY_EDITOR", "UNITY_6000_0", "UNITY_6000_0_OR_NEWER" };
        (int Major, int FirstMinor, int LastMinor)[] released =
        [
            (5, 3, 6), (2017, 1, 4), (2018, 1, 4), (2019, 1, 4),
            (2020, 1, 3), (2021, 1, 3), (2022, 1, 3), (2023, 1, 3),
        ];
        foreach (var (major, firstMinor, lastMinor) in released)
        {
            for (var minor = firstMinor; minor <= lastMinor; minor++)
            {
                symbols.Add($"UNITY_{major}_{minor}_OR_NEWER");
            }
        }

        return [.. symbols];
    }
}

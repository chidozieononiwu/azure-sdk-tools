// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using ApiView;
using APIView;
using APIView.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.SymbolDisplay;
using System.Collections.Immutable;
using System.ComponentModel;

namespace csharp_api_parser.TreeToken
{
    internal class CodeFileBuilder
    {
        private static readonly char[] _newlineChars = new char[] { '\r', '\n' };

        SymbolDisplayFormat _defaultDisplayFormat = new SymbolDisplayFormat(
            SymbolDisplayGlobalNamespaceStyle.Omitted,
            delegateStyle: SymbolDisplayDelegateStyle.NameAndSignature,
            extensionMethodStyle: SymbolDisplayExtensionMethodStyle.StaticMethod,
            propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.AllowDefaultLiteral |
                                  SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                                  SymbolDisplayMiscellaneousOptions.RemoveAttributeSuffix |
                                  SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                                  SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier,
            kindOptions: SymbolDisplayKindOptions.IncludeMemberKeyword,
            parameterOptions: SymbolDisplayParameterOptions.IncludeDefaultValue |
                              SymbolDisplayParameterOptions.IncludeExtensionThis |
                              SymbolDisplayParameterOptions.IncludeName |
                              SymbolDisplayParameterOptions.IncludeParamsRefOut |
                              SymbolDisplayParameterOptions.IncludeType,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeConstraints |
                             SymbolDisplayGenericsOptions.IncludeTypeConstraints |
                             SymbolDisplayGenericsOptions.IncludeTypeParameters |
                             SymbolDisplayGenericsOptions.IncludeVariance,
            memberOptions: SymbolDisplayMemberOptions.IncludeExplicitInterface |
                           SymbolDisplayMemberOptions.IncludeConstantValue |
                           SymbolDisplayMemberOptions.IncludeModifiers |
                           SymbolDisplayMemberOptions.IncludeParameters |
                           SymbolDisplayMemberOptions.IncludeType
        );

        private IAssemblySymbol? _assembly;

        public ICodeFileBuilderSymbolOrderProvider SymbolOrderProvider { get; set; } = new CodeFileBuilderSymbolOrderProvider();

        public const string CurrentVersion = "26";

        private IEnumerable<INamespaceSymbol> EnumerateNamespaces(IAssemblySymbol assemblySymbol)
        {
            var stack = new Stack<INamespaceSymbol>();
            stack.Push(assemblySymbol.GlobalNamespace);
            while (stack.TryPop(out var currentNamespace))
            {
                if (HasAnyPublicTypes(currentNamespace))
                {
                    yield return currentNamespace;
                }
                foreach (var subNamespace in currentNamespace.GetNamespaceMembers())
                {
                    stack.Push(subNamespace);
                }
            }
        }

        public CodeFile Build(IAssemblySymbol assemblySymbol, bool runAnalysis, List<DependencyInfo> dependencies)
        {
            _assembly = assemblySymbol;
            var analyzer = new Analyzer();

            if (runAnalysis)
            {
                analyzer.VisitAssembly(assemblySymbol);
            }

            var apiTree = new TreeNodeList<APITreeNode>();
            apiTree.Alignment = TreeNodeListAlignment.Vertical;

            var builder = new CodeFileTokensBuilder();

            BuildDependencies(apiTree, dependencies);
            BuildInternalsVisibleToAttributes(apiTree, assemblySymbol);

            var navigationItems = new List<NavigationItem>();
            foreach (var namespaceSymbol in SymbolOrderProvider.OrderNamespaces(EnumerateNamespaces(assemblySymbol)))
            {
                if (namespaceSymbol.IsGlobalNamespace)
                {
                    foreach (var namedTypeSymbol in SymbolOrderProvider.OrderTypes(namespaceSymbol.GetTypeMembers()))
                    {
                        BuildType(apiTree, builder, namedTypeSymbol, navigationItems, false);
                    }
                }
                else
                {
                    BuildNamespace(apiTree, builder, namespaceSymbol, navigationItems);
                }
            }

            NavigationItem assemblyNavigationItem = new NavigationItem()
            {
                Text = assemblySymbol.Name + ".dll",
                ChildItems = navigationItems.ToArray(),
                Tags = { { "TypeKind", "assembly" } }
            };

            var node = new CodeFile()
            {
                Name = $"{assemblySymbol.Name} ({assemblySymbol.Identity.Version})",
                Language = "C#",
                Tokens = builder.Tokens.ToArray(),
                VersionString = CurrentVersion,
                Navigation = new[] { assemblyNavigationItem },
                Diagnostics = analyzer.Results.ToArray(),
                PackageName = assemblySymbol.Name,
                PackageVersion = assemblySymbol.Identity.Version.ToString()
            };

            return node;
        }

        public static void BuildInternalsVisibleToAttributes(TreeNodeList<APITreeNode> apiTree, IAssemblySymbol assemblySymbol)
        {
            var assemblyAttributes = assemblySymbol.GetAttributes()
                .Where(a =>
                    a.AttributeClass?.Name == "InternalsVisibleToAttribute" &&
                    !a.ConstructorArguments[0].Value?.ToString()?.Contains(".Tests") == true &&
                    !a.ConstructorArguments[0].Value?.ToString()?.Contains(".Perf") == true &&
                    !a.ConstructorArguments[0].Value?.ToString()?.Contains("DynamicProxyGenAssembly2") == true);
            if (assemblyAttributes != null && assemblyAttributes.Any())
            {
                apiTree.Add(new APITreeNode(value: "Exposes internals to:", kind: APITreeNodeKind.Text));
                foreach (AttributeData attribute in assemblyAttributes)
                {
                    if (attribute.ConstructorArguments.Length > 0)
                    {
                        var param = attribute.ConstructorArguments[0].Value?.ToString();
                        if (!String.IsNullOrEmpty(param))
                        {
                            var firstComma = param?.IndexOf(',');
                            param = firstComma > 0 ? param?[..(int)firstComma] : param;
                            apiTree.Add(new APITreeNode(value: param!, kind: APITreeNodeKind.InternalsVisibleTo));
                        }
                    }
                }
                apiTree.Add(APITreeNode.CreateSpacerNode());
            }
        }

        public static void BuildDependencies(TreeNodeList<APITreeNode> apiTree, List<DependencyInfo> dependencies)
        {
            if (dependencies != null && dependencies.Any())
            {
                apiTree.Add(APITreeNode.CreateSpacerNode());
                apiTree.Add(new APITreeNode(value: "Dependencies:", kind: APITreeNodeKind.Text));

                foreach (DependencyInfo dependency in dependencies)
                {
                    apiTree.Add(new APITreeNode(value: dependency.Name, kind: APITreeNodeKind.Dependency));
                    apiTree.Add(new APITreeNode(value: $"-{dependency.Version}", kind: APITreeNodeKind.Text));
                }
                apiTree.Add(APITreeNode.CreateSpacerNode());
            }
        }

        private void BuildNamespace(List<APITreeNode> apiTree, CodeFileTokensBuilder builder, INamespaceSymbol namespaceSymbol, List<NavigationItem> navigationItems)
        {
            bool isHidden = HasOnlyHiddenTypes(namespaceSymbol);

            if (isHidden)
            {
                builder.Append(null, CodeFileTokenKind.HiddenApiRangeStart);
            }
            builder.Keyword(SyntaxKind.NamespaceKeyword);
            builder.Space();
            BuildNamespaceName(builder, namespaceSymbol);

            builder.Space();
            builder.Punctuation(SyntaxKind.OpenBraceToken);
            builder.IncrementIndent();
            builder.NewLine();

            List<NavigationItem> namespaceItems = new List<NavigationItem>();
            foreach (var namedTypeSymbol in SymbolOrderProvider.OrderTypes(namespaceSymbol.GetTypeMembers()))
            {
                BuildType(builder, namedTypeSymbol, namespaceItems, isHidden);
            }

            CloseBrace(builder);

            var namespaceItem = new NavigationItem()
            {
                NavigationId = namespaceSymbol.GetId(),
                Text = namespaceSymbol.ToDisplayString(),
                ChildItems = namespaceItems.ToArray(),
                Tags = { { "TypeKind", "namespace" } },
                IsHiddenApi = isHidden
            };
            navigationItems.Add(namespaceItem);
            if (isHidden)
            {
                builder.Append(null, CodeFileTokenKind.HiddenApiRangeEnd);
            }
        }

        private void BuildNamespaceName(CodeFileTokensBuilder builder, INamespaceSymbol namespaceSymbol)
        {
            if (!namespaceSymbol.ContainingNamespace.IsGlobalNamespace)
            {
                BuildNamespaceName(builder, namespaceSymbol.ContainingNamespace);
                builder.Punctuation(SyntaxKind.DotToken);
            }
            NodeFromSymbol(builder, namespaceSymbol);
        }

        private bool HasAnyPublicTypes(INamespaceSymbol subNamespaceSymbol)
        {
            return subNamespaceSymbol.GetTypeMembers().Any(IsAccessible);
        }

        private bool HasOnlyHiddenTypes(INamespaceSymbol namespaceSymbol)
        {
            return namespaceSymbol.GetTypeMembers().All(t => IsHiddenFromIntellisense(t) || !IsAccessible(t));
        }

        private void BuildType(List<APITreeNode> apiTree, CodeFileTokensBuilder builder, INamedTypeSymbol namedType, List<NavigationItem> navigationBuilder, bool inHiddenScope)
        {
            if (!IsAccessible(namedType))
            {
                return;
            }

            bool isHidden = IsHiddenFromIntellisense(namedType);
            var navigationItem = new NavigationItem()
            {
                NavigationId = namedType.GetId(),
                Text = namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                IsHiddenApi = isHidden
            };
            navigationBuilder.Add(navigationItem);
            navigationItem.Tags.Add("TypeKind", namedType.TypeKind.ToString().ToLowerInvariant());
    
            var apiTreeNode = new APITreeNode(value: namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), kind: APITreeNodeKind.Type)
            {
                NavigationId = namedType.GetId(),
                DefinitionId = namedType.GetId()
            };
            apiTreeNode.Properties.Add("TypeKind", namedType.TypeKind.ToString().ToLowerInvariant());

            if (isHidden && !inHiddenScope)
            {
                apiTreeNode.Properties.Add("IsHiddenAPI", true.ToString());
                builder.Append(null, CodeFileTokenKind.HiddenApiRangeStart);
            }

            BuildDocumentation(apiTreeNode, namedType);
            BuildAttributes(apiTreeNode, builder, namedType.GetAttributes());
            BuildVisibility(apiTreeNode, namedType);

            apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateSpaceNode());

            switch (namedType.TypeKind)
            {
                case TypeKind.Class:
                    BuildClassModifiers(apiTreeNode, builder, namedType);
                    apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.ClassKeyword));
                    break;
                case TypeKind.Delegate:
                    apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.DelegateKeyword));
                    break;
                case TypeKind.Enum:
                    apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.EnumKeyword));
                    break;
                case TypeKind.Interface:
                    apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.InterfaceKeyword));
                    break;
                case TypeKind.Struct:
                    if (namedType.IsReadOnly)
                    {
                        apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.ReadOnlyKeyword));
                        apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateSpaceNode());
                    }
                    apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.StructKeyword));
                    break;
            }

            apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateSpaceNode());

            var tkList = apiTreeNode.EndTokens;
            tkList = DisplayName(tkList, namedType, namedType);

            if (namedType.TypeKind == TypeKind.Delegate)
            {
                tkList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.SemicolonToken));
                return;
            }

            tkList.Add(APITokenTreeNode.CreateSpaceNode());
            tkList = BuildBaseType(tkList, namedType);

            tkList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.OpenBraceToken));

            foreach (var namedTypeSymbol in SymbolOrderProvider.OrderTypes(namedType.GetTypeMembers()))
            {
                BuildType(apiTreeNode.Children, builder, namedTypeSymbol, navigationBuilder, inHiddenScope || isHidden);
            }

            foreach (var member in SymbolOrderProvider.OrderMembers(namedType.GetMembers()))
            {
                if (member.Kind == SymbolKind.NamedType || member.IsImplicitlyDeclared || !IsAccessible(member)) continue;
                if (member is IMethodSymbol method)
                {
                    if (method.MethodKind == MethodKind.PropertyGet ||
                        method.MethodKind == MethodKind.PropertySet ||
                        method.MethodKind == MethodKind.EventAdd ||
                        method.MethodKind == MethodKind.EventRemove ||
                        method.MethodKind == MethodKind.EventRaise)
                    {
                        continue;
                    }
                }
                BuildMember(apiTreeNode.Children, builder, member, inHiddenScope);
            }
            apiTreeNode.BottomTokens.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.CloseBraceToken));
            apiTree.Add(apiTreeNode);
        }

        private void BuildDocumentation(APITreeNode apiTreeNode, ISymbol symbol)
        {
            var lines = symbol.GetDocumentationCommentXml()?.Trim().Split(_newlineChars);

            if (lines != null)
            {
                if (lines.All(string.IsNullOrWhiteSpace))
                {
                    return;
                }
                foreach (var line in lines)
                {
                    apiTreeNode.TopTokens.Add(new APITokenTreeNode(value: $"// {line.Trim()}", kind: APITokenTreeNodeKind.Comment));
                }
            }
        }

        private static void BuildClassModifiers(APITreeNode apiTreeNode, CodeFileTokensBuilder builder, INamedTypeSymbol namedType)
        {
            if (namedType.IsAbstract)
            {
                apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.AbstractKeyword));
                apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateSpaceNode());
            }

            if (namedType.IsStatic)
            {
                apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.StaticKeyword));
                apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateSpaceNode());
            }

            if (namedType.IsSealed)
            {
                apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.SealedKeyword));
                apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateSpaceNode());
            }
        }

        private TreeNodeList<APITokenTreeNode> BuildBaseType(TreeNodeList<APITokenTreeNode> tokenNodeList, INamedTypeSymbol namedType)
        {
            bool first = true;
            var tkList = tokenNodeList;

            if (namedType.BaseType != null &&
                namedType.BaseType.SpecialType == SpecialType.None)
            {
                tkList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.ColonToken));
                tkList.Add(APITokenTreeNode.CreateSpaceNode());
                first = false;

                tkList = DisplayName(tkList, namedType.BaseType);
            }

            foreach (var typeInterface in namedType.Interfaces)
            {
                if (!IsAccessible(typeInterface)) continue;

                if (!first)
                {
                    tkList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.CommaToken));
                    tkList.Add(APITokenTreeNode.CreateSpaceNode());
                }
                else
                {
                    tkList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.ColonToken));
                    tkList.Add(APITokenTreeNode.CreateSpaceNode());
                    first = false;
                }

                tkList = DisplayName(tkList, typeInterface);
            }

            if (!first)
            {
                tkList.Add(APITokenTreeNode.CreateSpaceNode());
            }
            return tkList;
        }

        private static void CloseBrace(CodeFileTokensBuilder builder)
        {
            builder.DecrementIndent();
            builder.WriteIndent();
            builder.Punctuation(SyntaxKind.CloseBraceToken);
            builder.NewLine();
        }

        private void BuildMember(List<APITreeNode> apiTree, CodeFileTokensBuilder builder, ISymbol member, bool inHiddenScope)
        {
            bool isHidden = IsHiddenFromIntellisense(member);

            if (isHidden && !inHiddenScope)
            {
                builder.Append(null, CodeFileTokenKind.HiddenApiRangeStart);
            }

            BuildDocumentation(builder, member);
            BuildAttributes(builder, member.GetAttributes());

            builder.WriteIndent();
            NodeFromSymbol(builder, member);

            if (member.Kind == SymbolKind.Field && member.ContainingType.TypeKind == TypeKind.Enum)
            {
                builder.Punctuation(SyntaxKind.CommaToken);
            }
            else if (member.Kind != SymbolKind.Property)
            {
                builder.Punctuation(SyntaxKind.SemicolonToken);
            }

            builder.NewLine();
            if (isHidden && !inHiddenScope)
            {
                builder.Append(null, CodeFileTokenKind.HiddenApiRangeEnd);
            }
        }

        private void BuildAttributes(APITreeNode apiTreeNode, CodeFileTokensBuilder builder, ImmutableArray<AttributeData> attributes)
        {
            const string attributeSuffix = "Attribute";
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeClass != null)
                {
                    if ((!IsAccessible(attribute.AttributeClass) &&
                        attribute.AttributeClass.Name != "FriendAttribute" &&
                        attribute.AttributeClass.ContainingNamespace.ToString() != "System.Diagnostics.CodeAnalysis")
                        || IsSkippedAttribute(attribute.AttributeClass))
                    {
                        continue;
                    }

                    var attributesTokenNode = APITokenTreeNode.CreateEmptyNode();
                    var tnList = attributesTokenNode.Children;

                    if (attribute.AttributeClass.DeclaredAccessibility == Accessibility.Internal || attribute.AttributeClass.DeclaredAccessibility == Accessibility.Friend)
                    {
                        tnList.Add(APITokenTreeNode.CreateKeywordNode("internal"));
                        tnList.Add(APITokenTreeNode.CreateSpaceNode());
                    }

                    tnList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.OpenBracketToken));

                    var name = attribute.AttributeClass.Name;
                    if (name.EndsWith(attributeSuffix))
                    {
                        name = name.Substring(0, name.Length - attributeSuffix.Length);
                    }

                    tnList.Add(new APITokenTreeNode(name, APITokenTreeNodeKind.TypeName));

                    if (attribute.ConstructorArguments.Any())
                    {
                        tnList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.OpenParenToken));

                        bool first = true;

                        foreach (var argument in attribute.ConstructorArguments)
                        {
                            if (!first)
                            {
                                tnList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.CommaToken));
                                tnList.Add(APITokenTreeNode.CreateSpaceNode());
                            }
                            else
                            {
                                first = false;
                            }
                            tnList = BuildTypedConstant(tnList, argument);
                        }

                        foreach (var argument in attribute.NamedArguments)
                        {
                            if (!first)
                            {
                                tnList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.CommaToken));
                                tnList.Add(APITokenTreeNode.CreateSpaceNode());
                            }
                            else
                            {
                                first = false;
                            }
                            tnList.Add(new APITokenTreeNode(value: argument.Key, kind: APITokenTreeNodeKind.Text));
                            tnList.Add(APITokenTreeNode.CreateSpaceNode());
                            tnList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.EqualsToken));
                            tnList.Add(APITokenTreeNode.CreateSpaceNode());
                            tnList = BuildTypedConstant(tnList, argument.Value);
                        }
                        tnList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.CloseParenToken));
                    }
                    tnList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.CloseBracketToken));
                    apiTreeNode.TopTokens.Add(attributesTokenNode);
                }
            }
        }

        private bool IsSkippedAttribute(INamedTypeSymbol attributeAttributeClass)
        {
            switch (attributeAttributeClass.Name)
            {
                case "DebuggerStepThroughAttribute":
                case "AsyncStateMachineAttribute":
                case "IteratorStateMachineAttribute":
                case "DefaultMemberAttribute":
                case "AsyncIteratorStateMachineAttribute":
                case "EditorBrowsableAttribute":
                case "NullableAttribute":
                case "NullableContextAttribute":
                    return true;
                default:
                    return false;
            }
        }

        private bool IsHiddenFromIntellisense(ISymbol member) =>
            member.GetAttributes().Any(d => d.AttributeClass?.Name == "EditorBrowsableAttribute"
                                            && (EditorBrowsableState)d.ConstructorArguments[0].Value! == EditorBrowsableState.Never);

        private bool IsDecoratedWithAttribute(ISymbol member, string attributeName) =>
            member.GetAttributes().Any(d => d.AttributeClass?.Name == attributeName);

        private TreeNodeList<APITokenTreeNode> BuildTypedConstant(TreeNodeList<APITokenTreeNode> tokenNodeList, TypedConstant typedConstant)
        {
            var tnList = tokenNodeList;
            if (typedConstant.IsNull)
            {
                tnList.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.NullKeyword));
            }
            else if (typedConstant.Kind == TypedConstantKind.Enum)
            {
                new CodeFileBuilderEnumFormatter(tnList).Format(typedConstant.Type, typedConstant.Value);
            }
            else if (typedConstant.Kind == TypedConstantKind.Type)
            {
                tnList.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.TypeOfKeyword));
                tnList.Add(APITokenTreeNode.CreatePunctuationNode("("));
                tnList = DisplayName(tnList, (ITypeSymbol)typedConstant.Value!);
                tnList.Add(APITokenTreeNode.CreatePunctuationNode(")"));
            }
            else if (typedConstant.Kind == TypedConstantKind.Array)
            {
                tnList.Add(APITokenTreeNode.CreateKeywordNode(SyntaxKind.NewKeyword));
                tnList.Add(APITokenTreeNode.CreatePunctuationNode("[] {"));

                bool first = true;

                foreach (var value in typedConstant.Values)
                {
                    if (!first)
                    {
                        tnList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.CommaToken));
                        tnList.Add(APITokenTreeNode.CreateSpaceNode());
                    }
                    else
                    {
                        first = false;
                    }

                    tnList = BuildTypedConstant(tnList, value);
                }
                tnList.Add(APITokenTreeNode.CreatePunctuationNode("}"));
            }
            else
            {
                if (typedConstant.Value is string s)
                {
                    tnList.Add(new APITokenTreeNode(value: ObjectDisplay.FormatLiteral(s, ObjectDisplayOptions.UseQuotes | ObjectDisplayOptions.EscapeNonPrintableCharacters),
                        kind: APITokenTreeNodeKind.StringLiteral));
                }
                else
                {
                    tnList.Add(new APITokenTreeNode(value: ObjectDisplay.FormatPrimitive(typedConstant.Value, ObjectDisplayOptions.None),
                        kind: APITokenTreeNodeKind.Literal));
                }
            }
            return tnList;
        }

        private void NodeFromSymbol(CodeFileTokensBuilder builder, ISymbol symbol)
        {
            builder.Append(new CodeFileToken()
            {
                DefinitionId = symbol.GetId(),
                Kind = CodeFileTokenKind.LineIdMarker
            });
            DisplayName(builder, symbol, symbol);
        }

        private void BuildVisibility(APITreeNode apiTreeNode, ISymbol symbol)
        {
            apiTreeNode.StartTokens.Add(APITokenTreeNode.CreateKeywordNode(ToEffectiveAccessibility(symbol.DeclaredAccessibility)));
        }

        private TreeNodeList<APITokenTreeNode> DisplayName(TreeNodeList<APITokenTreeNode> tokenNodeList, ISymbol symbol, ISymbol? definedSymbol = null)
        {
            var tnList = tokenNodeList;

            if (NeedsAccessibility(symbol))
            {
                tnList.Add(APITokenTreeNode.CreateKeywordNode(ToEffectiveAccessibility(symbol.DeclaredAccessibility)));
                tnList.Add(APITokenTreeNode.CreateSpaceNode());
            }
            if (symbol is IPropertySymbol propSymbol && propSymbol.DeclaredAccessibility != Accessibility.Internal)
            {
                var parts = propSymbol.ToDisplayParts(_defaultDisplayFormat);
                for (int i = 0; i < parts.Length; i++)
                {
                    // Skip internal setters
                    if (parts[i].Kind == SymbolDisplayPartKind.Keyword && parts[i].ToString() == "internal")
                    {
                        while (i < parts.Length && parts[i].ToString() != "}")
                        {
                            i++;
                        }
                    }
                    tnList = MapToken(tnList, definedSymbol, parts[i]);
                }
            }
            else
            {
                foreach (var symbolDisplayPart in symbol.ToDisplayParts(_defaultDisplayFormat))
                {
                    tnList = MapToken(tnList, definedSymbol, symbolDisplayPart);
                }
            }

            return tnList;
        }

        private bool NeedsAccessibility(ISymbol symbol)
        {
            return symbol switch
            {
                INamespaceSymbol => false,
                INamedTypeSymbol => false,
                IFieldSymbol fieldSymbol => fieldSymbol.ContainingType.TypeKind != TypeKind.Enum,
                IMethodSymbol methodSymbol => !methodSymbol.ExplicitInterfaceImplementations.Any() &&
                                              methodSymbol.ContainingType.TypeKind != TypeKind.Interface,
                IPropertySymbol propertySymbol => !propertySymbol.ExplicitInterfaceImplementations.Any() &&
                                                  propertySymbol.ContainingType.TypeKind != TypeKind.Interface,
                _ => true
            };
        }

        private TreeNodeList<APITokenTreeNode> MapToken(TreeNodeList<APITokenTreeNode> tokenNodeList, ISymbol? definedSymbol, SymbolDisplayPart symbolDisplayPart)
        {
            APITokenTreeNodeKind kind;

            switch (symbolDisplayPart.Kind)
            {
                case SymbolDisplayPartKind.TypeParameterName:
                case SymbolDisplayPartKind.AliasName:
                case SymbolDisplayPartKind.AssemblyName:
                case SymbolDisplayPartKind.ClassName:
                case SymbolDisplayPartKind.DelegateName:
                case SymbolDisplayPartKind.EnumName:
                case SymbolDisplayPartKind.ErrorTypeName:
                case SymbolDisplayPartKind.InterfaceName:
                case SymbolDisplayPartKind.StructName:
                    kind = APITokenTreeNodeKind.TypeName;
                    break;
                case SymbolDisplayPartKind.Keyword:
                    kind = APITokenTreeNodeKind.Keyword;
                    break;
                case SymbolDisplayPartKind.LineBreak:
                    kind = APITokenTreeNodeKind.Empty;
                    break;
                case SymbolDisplayPartKind.StringLiteral:
                    kind = APITokenTreeNodeKind.StringLiteral;
                    break;
                case SymbolDisplayPartKind.Punctuation:
                    kind = APITokenTreeNodeKind.Punctuation;
                    break;
                case SymbolDisplayPartKind.Space:
                    kind = APITokenTreeNodeKind.Space;
                    break;
                case SymbolDisplayPartKind.PropertyName:
                case SymbolDisplayPartKind.EventName:
                case SymbolDisplayPartKind.FieldName:
                case SymbolDisplayPartKind.MethodName:
                case SymbolDisplayPartKind.Operator:
                case SymbolDisplayPartKind.EnumMemberName:
                case SymbolDisplayPartKind.ExtensionMethodName:
                case SymbolDisplayPartKind.ConstantName:
                    kind = APITokenTreeNodeKind.MemberName;
                    break;
                default:
                    kind = APITokenTreeNodeKind.Text;
                    break;
            }

            string? navigateToId = null;
            var symbol = symbolDisplayPart.Symbol;

            if (symbol is INamedTypeSymbol &&
                (definedSymbol == null || !SymbolEqualityComparer.Default.Equals(definedSymbol, symbol)) &&
                SymbolEqualityComparer.Default.Equals(_assembly, symbol.ContainingAssembly))
            {
                navigateToId = symbol.GetId();
            }

            var tokenNode = APITokenTreeNode.CreateEmptyNode();

            if (symbolDisplayPart.Kind == SymbolDisplayPartKind.LineBreak)
            {
                tokenNodeList.Add(tokenNode);
                return APITokenTreeNode.SimulateLineBreak(tokenNode);
            }

            tokenNode = new APITokenTreeNode(value: symbolDisplayPart.ToString(), kind: kind)
            {
                DefinitionId = (definedSymbol != null && SymbolEqualityComparer.Default.Equals(definedSymbol, symbol)) ? definedSymbol.GetId() : null,
                NavigateToId = navigateToId,
            };
            tokenNodeList.Add(tokenNode);
            return tokenNodeList;
        }

        private Accessibility ToEffectiveAccessibility(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.ProtectedAndInternal:
                    return Accessibility.Internal;
                case Accessibility.ProtectedOrInternal:
                    return Accessibility.Protected;
                default:
                    return accessibility;
            }
        }

        private bool IsAccessible(ISymbol s)
        {
            switch (s.DeclaredAccessibility)
            {
                case Accessibility.Protected:
                case Accessibility.ProtectedOrInternal:
                case Accessibility.Public:
                    return true;
                case Accessibility.Internal:
                    return s.GetAttributes().Any(a => a.AttributeClass.Name == "FriendAttribute");
                default:
                    return IsAccessibleExplicitInterfaceImplementation(s);
            }
        }

        private bool IsAccessibleExplicitInterfaceImplementation(ISymbol s)
        {
            return s switch
            {
                IMethodSymbol methodSymbol => methodSymbol.ExplicitInterfaceImplementations.Any(i => IsAccessible(i.ContainingType)),
                IPropertySymbol propertySymbol => propertySymbol.ExplicitInterfaceImplementations.Any(i => IsAccessible(i.ContainingType)),
                _ => false
            };
        }

        internal class CodeFileBuilderEnumFormatter : AbstractSymbolDisplayVisitor
        {
            private readonly TreeNodeList<APITokenTreeNode> _tokenNodeList;

            public CodeFileBuilderEnumFormatter(TreeNodeList<APITokenTreeNode> tokenNodeList) : base(null, SymbolDisplayFormat.FullyQualifiedFormat, false, null, 0, false)
            {
                _tokenNodeList = tokenNodeList;
            }

            protected override AbstractSymbolDisplayVisitor MakeNotFirstVisitor(bool inNamespaceOrType = false)
            {
                return this;
            }

            protected override void AddLiteralValue(SpecialType type, object value)
            {
                _tokenNodeList.Add(new APITokenTreeNode(ObjectDisplay.FormatPrimitive(value, ObjectDisplayOptions.None), APITokenTreeNodeKind.Literal));
            }

            protected override void AddExplicitlyCastedLiteralValue(INamedTypeSymbol namedType, SpecialType type, object value)
            {
                _tokenNodeList.Add(new APITokenTreeNode(ObjectDisplay.FormatPrimitive(value, ObjectDisplayOptions.None), APITokenTreeNodeKind.Literal));
            }

            protected override void AddSpace()
            {
                _tokenNodeList.Add(APITokenTreeNode.CreateSpaceNode());
            }

            protected override void AddBitwiseOr()
            {
                _tokenNodeList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.BarToken));
            }

            public override void VisitField(IFieldSymbol symbol)
            {
                _tokenNodeList.Add(new APITokenTreeNode(symbol.Type.Name, APITokenTreeNodeKind.TypeName));
                _tokenNodeList.Add(APITokenTreeNode.CreatePunctuationNode(SyntaxKind.DotToken));
                _tokenNodeList.Add(new APITokenTreeNode(symbol.Name, APITokenTreeNodeKind.MemberName));
            }

            public void Format(ITypeSymbol? type, object? typedConstantValue)
            {
                AddNonNullConstantValue(type, typedConstantValue);
            }
        }
    }
}

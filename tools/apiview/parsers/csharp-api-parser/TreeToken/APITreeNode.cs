using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace csharp_api_parser.TreeToken
{
    internal enum APITreeNodeKind
    {
        Empty = 0,
        Space = 1,
        Text = 2,
        Dependency = 3,
        InternalsVisibleTo = 4,
        Type = 5,
    }

    internal enum APITokenTreeNodeKind
    {
        Empty = 0,
        Space = 1,
        Comment = 2,
        Keyword = 3,
        Punctuation = 4,
        TypeName = 5,
        MemberName = 6,
        Literal = 7,
        StringLiteral = 8,
        Text = 9,
    }

    internal enum TreeNodeListAlignment
    {
        Horizontal,
        Vertical
    }

    internal enum APITokenTreeChildrenRenderFormat
    {
        Code,
        Table
    }

    internal class TreeNodeList<T> : List<T>
    {
        public T? ParentNode { get; set; } = default(T);
        public TreeNodeListAlignment Alignment { get; set; }
    }

    internal class APITokenTreeNode
    {
        public string Value { get; }
        public APITokenTreeNodeKind Kind { get; }
        public string? DefinitionId { get; set; }
        public string? NavigateToId { get; set; }
        public TreeNodeList<APITokenTreeNode> Children { get; } = new TreeNodeList<APITokenTreeNode>();
        public APITokenTreeChildrenRenderFormat ChildrenRenderFormat { get; set; }

        public APITokenTreeNode(string value, APITokenTreeNodeKind kind)
        {
            Value = value;
            Kind = kind;
            Children.Alignment = TreeNodeListAlignment.Horizontal;
            Children.ParentNode = this;
        }

        public static APITokenTreeNode CreateEmptyNode()
        {
            return new APITokenTreeNode(value: String.Empty, kind: APITokenTreeNodeKind.Empty);
        }

        public static APITokenTreeNode CreateSpaceNode()
        {
            return new APITokenTreeNode(value: String.Empty, kind: APITokenTreeNodeKind.Space);
        }

        public static APITokenTreeNode CreatePunctuationNode(SyntaxKind syntaxKind)
        {
            var punctuation = SyntaxFacts.GetText(syntaxKind);
            return CreatePunctuationNode(punctuation);
        }

        public static APITokenTreeNode CreatePunctuationNode(string value)
        {
            return new APITokenTreeNode(value: value, kind: APITokenTreeNodeKind.Punctuation);
        }

        public static APITokenTreeNode CreateKeywordNode(SyntaxKind syntaxKind)
        {
            var keyword = SyntaxFacts.GetText(syntaxKind);
            return CreateKeywordNode(keyword);
        }

        public static APITokenTreeNode CreateKeywordNode(Accessibility accessibility)
        {
            var punctuation = SyntaxFacts.GetText(accessibility);
            return CreateKeywordNode(punctuation);
        }

        public static APITokenTreeNode CreateKeywordNode(string value)
        {
            return new APITokenTreeNode(value: value, kind: APITokenTreeNodeKind.Keyword);
        }

        /// <summary>
        /// Simulating a Line Break by using a vertical alignment for the children of supplied tokenNode
        /// </summary>
        /// <param name="tokenNode"></param>
        /// <returns></returns>
        public static TreeNodeList<APITokenTreeNode> SimulateLineBreak(APITokenTreeNode tokenNode)
        {
            tokenNode.Children.Alignment = TreeNodeListAlignment.Vertical;
            var childTokenNode = CreateEmptyNode();
            childTokenNode.Children.Alignment = TreeNodeListAlignment.Horizontal;
            tokenNode.Children.Add(childTokenNode);
            return childTokenNode.Children;
        }

        public void AddChild(APITokenTreeNode child)
        {
            Children.Add(child);
        }

        public void SetChildrenAlignment(TreeNodeListAlignment alignment)
        {
            Children.Alignment = alignment;
        }
    }

    internal class APITreeNode
    {
        public string Value { get; }
        public APITreeNodeKind Kind { get; }
        public Dictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public TreeNodeList<APITreeNode> Children { get; } = new TreeNodeList<APITreeNode>();
        public TreeNodeList<APITokenTreeNode> TopTokens { get; } = new TreeNodeList<APITokenTreeNode>();
        public TreeNodeList<APITokenTreeNode> BottomTokens { get; } = new TreeNodeList<APITokenTreeNode>();
        public TreeNodeList<APITokenTreeNode> StartTokens { get; } = new TreeNodeList<APITokenTreeNode>();
        public TreeNodeList<APITokenTreeNode> EndTokens { get; } = new TreeNodeList<APITokenTreeNode>();
        public string? NavigationId { get; set; }
        public string? DefinitionId { get; set; }

        /// <summary>
        /// Represent a node in the APITree
        /// </summary>
        /// <param name="value"></param> the actual value of the node
        /// <param name="kind"></param> allows the rendered to understand how to render the node
        public APITreeNode(string value, APITreeNodeKind kind)
        {
            Value = value;
            Kind = kind;
            Children.Alignment = TreeNodeListAlignment.Vertical;
            Children.ParentNode = this;

            TopTokens.Alignment = TreeNodeListAlignment.Vertical;
            BottomTokens.Alignment = TreeNodeListAlignment.Vertical;
            StartTokens.Alignment = TreeNodeListAlignment.Horizontal;
            EndTokens.Alignment = TreeNodeListAlignment.Horizontal;
        }

        public static APITreeNode CreateEmptyNode()
        {
            return new APITreeNode(value: String.Empty, kind: APITreeNodeKind.Empty);
        }

        public static APITreeNode CreateSpacerNode()
        {
            return new APITreeNode(value: String.Empty, kind: APITreeNodeKind.Space);
        }

        public void AddChild(APITreeNode child)
        {
            Children.Add(child);
        }

        public void SetChildrenAlignment(TreeNodeListAlignment alignment)
        {
            Children.Alignment = alignment;
        }

        public void SetTopTokensAlignment(TreeNodeListAlignment alignment)
        {
            TopTokens.Alignment = alignment;
        }

        public void SetTopBottomAlignment(TreeNodeListAlignment alignment)
        {
            BottomTokens.Alignment = alignment;
        }

        public void SetStartTokensAlignment(TreeNodeListAlignment alignment)
        {
            StartTokens.Alignment = alignment;
        }

        public void SetEndTokensAlignment(TreeNodeListAlignment alignment)
        {
            EndTokens.Alignment = alignment;
        }
    }
}

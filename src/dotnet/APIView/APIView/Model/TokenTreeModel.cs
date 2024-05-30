using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace csharp_api_parser.TreeToken
{
    public enum StructuredTokenKind
    {
        Content = 0,
        LineBreak = 1,
        NoneBreakingSpace = 2,
        TabSpace = 3,
        ParameterSeparator = 4,
        Url = 5
    }


    public class StructuredToken
    {
        public string Value { get; set; } = string.Empty;
        public string Id { get; set; }
        public StructuredTokenKind Kind { get; set; }
        public HashSet<string> Tags { get; set; } = new HashSet<string>();
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public HashSet<string> RenderClasses { get; set; } = new HashSet<string>();

        public StructuredToken()
        {
            new StructuredToken(string.Empty);
        }

        public StructuredToken(string value)
        {
            Value = value;
            Kind = StructuredTokenKind.Content;
        }

        public static StructuredToken CreateLineBreakToken()
        {
            var token = new StructuredToken();
            token.Kind = StructuredTokenKind.LineBreak;
            return token;
        }

        public static StructuredToken CreateEmptyToken(string id = null)
        {
            var token = new StructuredToken();
            if (!string.IsNullOrEmpty(id))
            {
                token.Id = id;
            }
            token.Kind = StructuredTokenKind.Content;
            return token;
        }

        public static StructuredToken CreateSpaceToken()
        {
            var token = new StructuredToken();
            token.Kind = StructuredTokenKind.NoneBreakingSpace;
            return token;
        }

        public static StructuredToken CreateParameterSeparatorToken()
        {
            var token = new StructuredToken();
            token.Kind = StructuredTokenKind.ParameterSeparator;
            return token;
        }

        public static StructuredToken CreateTextToken(string value, string id = null)
        {
            var token = new StructuredToken(value);
            if (!string.IsNullOrEmpty(id))
            {
                token.Id = id;
            }
            token.RenderClasses.Add("text");
            return token;
        }

        public static StructuredToken CreateKeywordToken(string value)
        {
            var token = new StructuredToken(value);
            token.RenderClasses.Add("keyword");
            return token;
        }

        public static StructuredToken CreateKeywordToken(SyntaxKind syntaxKind)
        {
            return CreateKeywordToken(SyntaxFacts.GetText(syntaxKind));
        }

        public static StructuredToken CreateKeywordToken(Accessibility accessibility)
        {
            return CreateKeywordToken(SyntaxFacts.GetText(accessibility));
        }

        public static StructuredToken CreatePunctuationToken(string value)
        {
            var token = new StructuredToken(value);
            token.RenderClasses.Add("punctuation");
            return token;
        }

        public static StructuredToken CreatePunctuationToken(SyntaxKind syntaxKind)
        {
            return CreatePunctuationToken(SyntaxFacts.GetText(syntaxKind));
        }

        public static StructuredToken CreateTypeNameToken(string value)
        {
            var token = new StructuredToken(value);
            token.RenderClasses.Add("type-name");
            return token;
        }

        public static StructuredToken CreateMemberNameToken(string value)
        {
            var token = new StructuredToken(value);
            token.RenderClasses.Add("member-name");
            return token;
        }

        public static StructuredToken CreateLiteralToken(string value)
        {
            var token = new StructuredToken(value);
            token.RenderClasses.Add("literal");
            return token;
        }

        public static StructuredToken CreateStringLiteralToken(string value)
        {
            var token = new StructuredToken(value);
            token.RenderClasses.Add("string-literal");
            return token;
        }
    }

    public class APITreeNode
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Kind { get; set; }
        public HashSet<string> Tags { get; set; } = new HashSet<string>();
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public List<StructuredToken> TopTokens { get; set; } = new List<StructuredToken>();
        public List<StructuredToken> BottomTokens { get; set; } = new List<StructuredToken>();
        public List<APITreeNode> Children { get; set; } = new List<APITreeNode>();

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 23 + (Name != null ? Name.GetHashCode() : 0);
            hash = hash * 23 + (Id != null ? Id.GetHashCode() : 0);
            hash = hash * 23 + (Kind != null ? Kind.GetHashCode() : 0);
            return hash;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            var other = (APITreeNode)obj;
            return Name == other.Name && Id == other.Id && Kind == other.Kind;
        }

        public void SortChildren()
        {
            if (Kind.Equals("Namespace") || Kind.Equals("Type") || Kind.Equals("Member"))
            {
                Children.Sort((x, y) => x.Name.CompareTo(y.Name));
            }
            foreach (var child in Children)
            {
                child.SortChildren();
            }
        }
    }
}

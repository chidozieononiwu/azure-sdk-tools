# Contributing

This page describes how to contribute to [APIView](../../../src//dotnet/APIView/APIViewWeb/CONTRIBUTING.md) language level parsers.
Specifically how to create or update a language parser to produce tree style tokens for APIView.

## Creating Tree Style Tokens
The main idea is to capture the hierachy of the API using a tree data structure, then maintain a flat list of tokens for each node of the tree.

![APITree](APITree.svg)

Each tree node has top tokens which should be used to capture the main data on the node. If your language requires it use the bottom tokens to capture data that closes out the node.

- Here are the models needed
  ```
  object APITreeNode
    string Name
    string Id
    string Kind
    Set<string> Tags
    Dictionary<string, string> Properties
    List<StructuredToken> TopTokens
    List<StructuredToken> BottomTokens
    List<APITreeNode> Children

  object StructuredToken
    string Value
    string Id
    StructuredTokenKind Kind
    Dictionary<string, string> Properties 
    Set<string> RenderClasses 

  enum StructuredTokenKind
    Content
    LineBreak
    NoneBreakingSpace
    TabSpace
    ParameterSeparator
    Url
  ```
### APITreeNode
- `Name` : The name of the tree node which will be used for page navigation.
- `Id` : Id of the node, which should be unique at the node level. i.e. unique among its siblings. Also the id should be [valid HTML id](https://developer.mozilla.org/en-US/docs/Web/HTML/Global_attributes/id).
- `Kind` : What kind of node is it. (namespace, class, module, method e.t.c)
- `Tags` : Use this for thigs like `Deprecated`, `Hidden`
- `Properties` : If the node needs more specification e.g. Use `SubKind` entry to make the node kind more specific. Feel free to push any other data that you think will be useful here, then file an issue for further implementation in APIView.
- `TopTokens` : The main data of the node.
- `BottomToken` : Data that closes out the node.
- `Childrens` : Node immediate descendant

### StructuredToken
- `Value` : The token value which will be dispalyed.
- `Id` : which will be used to navigate and find token on page.
- `Kind` : Could be `LineBreak` `NoneBreakingSpace` `TabSpace` `ParameterSeparator` `Url` `Content`
  All tokens should be content except for spacing tokens. ParameterSeparator should be used between method or function parameters. Spacing token dont need to have value.
- `Properties` : Capture any other interesting data here. e.g Use `GroupId` : `documentation` to group consecutive tokens.
- `RenderClasses` : Add css classes for how the tokens will be rendred.

If you want to have space between the API nodes add an empty token and lineBreak at the end of bottom tokens to simulate one empty line.

Dont worry about indentation that will be handeled by the tree structure, unless you want to have indentation between the tokens then use `TabSpace` token kind.

If your packages contains multiple languages then you will have multiple trees with multiple roots.
Assign the final parsed value to `APIForest` property of the `CodeFile`.

## Commons Scenarios
- TEXT, KEYWORD, COMMENT : Add `text`, `keyword`, `comment` to RenderClasses of the token
- NEW_LINE : Create a token with `Kind = LineBreak`
- WHITE_SPACE :  Create token with `Kind = NoneBreakingSpace`
- PUNCTUATION : Create a token with `Kind = Content` and the `Value = the punctuation`
- DOCUMENTATION : Add `GroupId = documentation` in the properties of the token. This identifies a range of consecutive tokens as belonging to a group.
- SKIP_DIFF :  Add `SkipDiff` to the node Tag to indicate that diff should be skipped.
- LINE_ID_MARKER : You can add a empty token. `Kind = Content` and `Value = Empty String` then give it an Id to make it commentable.
- EXTERNAL_LINK : Create a single token set `Kind = Url`, `Value = link` then add the link text as a properties `LinkText`;
- Common Tags: `Deprecated`, `Hidden`, `HideFromNavigation`



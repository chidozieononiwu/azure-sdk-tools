// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using APIView;
using csharp_api_parser.TreeToken;
using System;
using System.Collections.Generic;

namespace ApiView
{
    public class TreeTokenCodeFile
    {
        private string _versionString;

        [Obsolete("This is only for back compat, VersionString should be used")]
        public int Version { get; set; }

        public string VersionString
        {
#pragma warning disable 618
            get => _versionString ?? Version.ToString();
#pragma warning restore 618
            set => _versionString = value;
        }

        public string Name { get; set; }

        public string Language { get; set; }

        public string LanguageVariant { get; set; }

        public string PackageName { get; set; }

        public string ServiceName { get; set; }

        public string PackageDisplayName { get; set; }

        public string PackageVersion { get; set; }

        public string CrossLanguagePackageId { get; set; }

        public List<APITreeNode> APIForest { get; set; } = new List<APITreeNode>();

        public CodeDiagnostic[] Diagnostics { get; set; }
    }
}

using System.CommandLine;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using ApiView;
using Newtonsoft.Json;

var inputOption = new Option<FileInfo>("--packageFilePath", "C# Package (.nupkg) file").ExistingOnly();
inputOption.IsRequired = true;

var outputOption = new Option<DirectoryInfo>("--outputPath", "Directory for the output Token File").ExistingOnly();
var runAnalysis = new Argument<bool>("runAnalysis", "Run Analysis on the package");
runAnalysis.SetDefaultValue(true);

var rootCommand = new RootCommand("Parse C# Package (.nupkg) to APIView Tokens")
{
    inputOption,
    outputOption,
    runAnalysis
};

rootCommand.SetHandler((FileInfo packageFilePath, DirectoryInfo OutputDirectory, bool runAnalysis) =>
{
    try
    {
        using (var stream = packageFilePath.OpenRead())
        {
            HandlePackageFileParsing(stream, packageFilePath, OutputDirectory, runAnalysis);
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error Reading PackageFile : {ex.Message}");
    }
}, inputOption, outputOption, runAnalysis);

return rootCommand.InvokeAsync(args).Result;


static async void HandlePackageFileParsing(Stream stream, FileInfo packageFilePath, DirectoryInfo OutputDirectory, bool runAnalysis)
{
    ZipArchive? zipArchive = null;
    Stream? dllStream = stream;
    Stream? docStream = null;
    List<DependencyInfo>? dependencies = null;

    try
    {
        if (IsNuget(packageFilePath.FullName))
        {
            zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
            var nuspecEntry = zipArchive.Entries.Single(entry => IsNuspec(entry.Name));
            var dllEntries = zipArchive.Entries.Where(entry => IsDll(entry.Name)).ToArray();

            if (dllEntries.Length == 0)
            {
                Console.Error.WriteLine($"PackageFile {packageFilePath.FullName} contains no dlls.");
                return;
            }

            var dllEntry = dllEntries.First();
            if (dllEntries.Length > 1)
            {
                // If there are multiple dlls in the nupkg (e.g. Cosmos), try to find the first that matches the nuspec name, but
                // fallback to just using the first one.
                dllEntry = dllEntries.FirstOrDefault(
                    dll => Path.GetFileNameWithoutExtension(nuspecEntry.Name)
                        .Equals(Path.GetFileNameWithoutExtension(dll.Name), StringComparison.OrdinalIgnoreCase)) ?? dllEntry;
            }

            dllStream = dllEntry.Open();
            var docEntry = zipArchive.GetEntry(Path.ChangeExtension(dllEntry.FullName, ".xml"));
            if (docEntry != null)
            {
                docStream = docEntry.Open();
            }
            using var nuspecStream = nuspecEntry.Open();
            var document = XDocument.Load(nuspecStream);
            var dependencyElements = document.Descendants().Where(e => e.Name.LocalName == "dependency");
            dependencies = new List<DependencyInfo>();
            dependencies.AddRange(
                    dependencyElements.Select(dependency => new DependencyInfo(
                            dependency.Attribute("id")?.Value,
                                dependency.Attribute("version")?.Value)));
            // filter duplicates and sort
            if (dependencies.Any())
            {
                dependencies = dependencies
                .GroupBy(d => d.Name)
                .Select(d => d.First())
                .OrderBy(d => d.Name).ToList();
            }
        }

        var assemblySymbol = CompilationFactory.GetCompilation(dllStream, docStream);
        if (assemblySymbol == null)
        {
            Console.Error.WriteLine($"PackageFile {packageFilePath.FullName} contains no Assembly Symbol.");
            return;
        }
        var treeTokenCodeFile = new csharp_api_parser.TreeToken.CodeFileBuilder().Build(assemblySymbol, runAnalysis, dependencies);
        var tokenFilePath = Path.Combine(OutputDirectory.FullName, $"{assemblySymbol.Name}.json");

        using (StreamWriter fileWriter = File.CreateText(tokenFilePath))
        {
            JsonSerializer serializer = new JsonSerializer();
            serializer.Serialize(fileWriter, treeTokenCodeFile);
        }
        Console.WriteLine($"TokenCodeFile File {tokenFilePath} Generated Successfully.");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error Parsing PackageFile : {ex.Message}");
    }
    finally
    {
        zipArchive?.Dispose();
    }

}

static bool IsNuget(string name)
{
    return name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
}

static bool IsNuspec(string name)
{
    return name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase);
}

static bool IsDll(string name)
{
    return name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
}

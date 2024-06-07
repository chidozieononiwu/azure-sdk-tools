using Azure.Storage.Blobs;
using ConvertFlatToTreeTokens;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;


var config = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets(typeof(Program).Assembly)
    .Build();

var cosmosClient = new CosmosClient(config["CosmosConnectionString"]);
var apiRevisionContainer = cosmosClient.GetContainer("APIViewV2", "APIRevisions");
var orginalsContainer = new BlobContainerClient(config["BlobConnectionString"], "originals");
var codefilesContainer = new BlobContainerClient(config["BlobConnectionString"], "codefiles");

async Task<List<APIRevisionListItemModel>> GetCodeFilesIds(string language)
{
    var query = $"SELECT * FROM c WHERE c.Language = '{language}' AND c.id = 'cfa75d944a3645f5b315498ca020a8db'";
    QueryDefinition queryDefinition = new QueryDefinition(query);
    using FeedIterator<APIRevisionListItemModel> feedIterator = apiRevisionContainer.GetItemQueryIterator<APIRevisionListItemModel>(queryDefinition);
    var result = new List<APIRevisionListItemModel>();
    while (feedIterator.HasMoreResults)
    {
        FeedResponse<APIRevisionListItemModel> response = await feedIterator.ReadNextAsync();
        result.AddRange(response);
    }
    Console.WriteLine($"Total API Revisions: {result.Count}");
    return result;
}

var apiRevisions = await GetCodeFilesIds("C#");
Console.WriteLine($"##[command] {apiRevisions} Total APIRevisions");

int processed = 0;
int remaining = apiRevisions.Count;

foreach (var apiRevision in apiRevisions)
{
    if (apiRevision.Files.Count > 0 && apiRevision.Files[0].HasOriginal)
    {
        BlobClient originalBlobClient = orginalsContainer.GetBlobClient(apiRevision.Files[0].FileId);
        BlobClient codeFileBlobClient = codefilesContainer.GetBlobClient(apiRevision.Id + "/" + apiRevision.Files[0].FileId);

        if (await originalBlobClient.ExistsAsync())
        {
            var originalsBlob = await originalBlobClient.DownloadAsync();

            string tempNugetFile = apiRevision.Files[0].FileId + ".nupkg";
            string tempNugetFilePath = Path.Combine(Path.GetTempPath(), tempNugetFile);

            string tempDirectoryPath = Path.GetTempPath();

            using (FileStream fileStream = File.Create(tempNugetFilePath))
            {
                originalsBlob.Value.Content.CopyTo(fileStream);
            }

            Process process = new Process();
            process.StartInfo.FileName = "CSharpAPIParserForAPIView";
            process.StartInfo.Arguments = $"--packageFilePath {tempNugetFilePath} --outputDirectoryPath {tempDirectoryPath} --outputFileName {apiRevision.Files[0].FileId}";
            process.Start();
            process.WaitForExit();

            string tempCodeFilePath = Path.Combine(tempDirectoryPath, apiRevision.Files[0].FileId + ".json.tgz");

            if (File.Exists(tempCodeFilePath))
            {
                try
                {
                    using (FileStream uploadFileStream = File.OpenRead(tempCodeFilePath))
                    {
                        codeFileBlobClient.Upload(uploadFileStream, true);
                    }

                    apiRevision.Files[0].VersionString = "27";
                    apiRevision.Files[0].ParserStyle = "Tree";
                    await apiRevisionContainer.UpsertItemAsync(apiRevision, new PartitionKey(apiRevision.ReviewId));

                    processed++;
                    remaining--;

                    File.Delete(tempCodeFilePath);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"##[command]Successfully Generated TreeToken at {apiRevision.Id} / {apiRevision.Files[0].FileId}");
                    Console.WriteLine($"##[command] processed: {processed}");
                    Console.WriteLine($"##[command] remaining: {remaining}");
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"##[error]Failed to upload TreeToken at {apiRevision.Id} / {apiRevision.Files[0].FileId}");
                    Console.WriteLine(ex.Message);
                }
            }

            File.Delete(tempNugetFilePath);
        }
    }

}

Console.WriteLine("Done");










using CloneAPIViewDB;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualBasic.FileIO;
using System.Text;
using System.Text.RegularExpressions;


var config = new ConfigurationBuilder()
    .AddUserSecrets(typeof(Program).Assembly)
    .Build();

var cosmosClient = new CosmosClient(config["CosmosConnectionString"]);
var reviewsContainerOld = cosmosClient.GetContainer("APIView", "Reviews");
var commentsContainerOld = cosmosClient.GetContainer("APIView", "Comments");
var prContainerOld = cosmosClient.GetContainer("APIView", "PullRequests");
var samplesContainerOld = cosmosClient.GetContainer("APIView", "UsageSamples");

var reviewsContainerNew = cosmosClient.GetContainer("APIViewV2", "Reviews");
var revisionsContainerNew = cosmosClient.GetContainer("APIViewV2", "APIRevisions");
var commentsContainerNew = cosmosClient.GetContainer("APIViewV2", "Comments");
var prContainerNew = cosmosClient.GetContainer("APIViewV2", "PullRequests");
var samplesContainerNew = cosmosClient.GetContainer("APIView", "SamplesRevisions");

static string ArrayToQueryString<T>(IEnumerable<T> items)
{
    var result = new StringBuilder();
    result.Append("(");
    foreach (var item in items)
    {
        if (item is int)
        {
            result.Append($"{item},");
        }
        else
        {
            result.Append($"\"{item}\",");
        }

    }
    if (result[result.Length - 1] == ',')
    {
        result.Remove(result.Length - 1, 1);
    }
    result.Append(")");
    return result.ToString();
}

static void LogToFile(string message, LogType type) {
    string logFile = string.Empty;
    if (type == LogType.Review)
    {
        logFile = "C:\\Users\\chononiw\\OneDrive - Microsoft\\Jobs\\APIView\\APIView Restructure\\Clone APIView DB\\CloneAPIViewDB\\CloneAPIView\\LogsStagingRun6\\create-review-swagger.txt";
    }
    else if (type == LogType.Samples)
    {
        logFile = "C:\\Users\\chononiw\\OneDrive - Microsoft\\Jobs\\APIView\\APIView Restructure\\Clone APIView DB\\CloneAPIViewDB\\CloneAPIView\\LogsStagingRun6\\create-samples-swagger.txt";
    }
    else if (type == LogType.PR)
    {
        logFile = "C:\\Users\\chononiw\\OneDrive - Microsoft\\Jobs\\APIView\\APIView Restructure\\Clone APIView DB\\CloneAPIViewDB\\CloneAPIView\\LogsStagingRun6\\create-pr-swagger.txt";
    }
    else if (type == LogType.Revision)
    {
        logFile = "C:\\Users\\chononiw\\OneDrive - Microsoft\\Jobs\\APIView\\APIView Restructure\\Clone APIView DB\\CloneAPIViewDB\\CloneAPIView\\LogsStagingRun6\\create-revision-swagger.txtt";
    }

    File.AppendAllText(logFile, message + Environment.NewLine);
} 

// Read Revisions in Reviews
// Check to see if there are any Reviews that have the same package name for that language
// If there is create a revision with ReviewId found, add the revision Id to the review
// If a review with the same package name and language is not present log the review, revisionid, Language, PackageName and Review type to a file
async Task CreateRevisions(Container reviewsContainerOld, Container reviewsContainerNew, Container revisionsContainerNew,
    List<string> languages, DateTime lastUpdate = default(DateTime), string? csvFilePath = null, bool limit = false) 
{
    HashSet<string> officialPackageNames = new HashSet<string>();
    if (!String.IsNullOrEmpty(csvFilePath))
    {
        officialPackageNames = GetOfficialPackageNamesFromCSVFile(csvFilePath);
    }

    var reviewsOld = new List<ReviewModelOld>();

    if (limit)
    {
        var existingReviews = new List<ReviewModel>();
        var existingReviewQuery = $@"SELECT * FROM c WHERE c.Language = @language";
        var exisitngReviewsQueryDefinition = new QueryDefinition(existingReviewQuery).WithParameter("@language", languages[0]);
        var existingReviewsItemQueryIterator = reviewsContainerNew.GetItemQueryIterator<ReviewModel>(exisitngReviewsQueryDefinition);

        while (existingReviewsItemQueryIterator.HasMoreResults)
        {
            var result = await existingReviewsItemQueryIterator.ReadNextAsync();
            existingReviews.AddRange(result.Resource);
        }

        HashSet<string> existingPackageNames = existingReviews.Select(r => r.PackageName).ToHashSet();

        var languagesAsQueryString = ArrayToQueryString<string>(languages);
        var existingPackageNamesAsQueryString = ArrayToQueryString<string>(existingPackageNames);
        var queryOld = $@"
SELECT * FROM c
WHERE c.Revisions[0].Files[0].Language IN {languagesAsQueryString} AND c.Revisions[0].Files[0].PackageName IN {existingPackageNamesAsQueryString}";
        var queryDefinitionOld = new QueryDefinition(queryOld);
        var itemQueryIteratorOld = reviewsContainerOld.GetItemQueryIterator<ReviewModelOld>(queryDefinitionOld);

        while (itemQueryIteratorOld.HasMoreResults)
        {
            var result = await itemQueryIteratorOld.ReadNextAsync();
            reviewsOld.AddRange(result.Resource);
        }
    }
    else
    {
        var languagesAsQueryString = ArrayToQueryString<string>(languages);
        var queryOld = $@"
SELECT * FROM c
WHERE c.Revisions[0].Files[0].Language IN {languagesAsQueryString}";
        var queryDefinitionOld = new QueryDefinition(queryOld);
        var itemQueryIteratorOld = reviewsContainerOld.GetItemQueryIterator<ReviewModelOld>(queryDefinitionOld);

        while (itemQueryIteratorOld.HasMoreResults)
        {
            var result = await itemQueryIteratorOld.ReadNextAsync();
            reviewsOld.AddRange(result.Resource);
        }
    }

    foreach (var reviewOld in reviewsOld)
    {
        if (lastUpdate != default(DateTime) && reviewOld.LastUpdated != default(DateTime) && reviewOld.LastUpdated <= lastUpdate)
        {
            continue;
        }

        if (!reviewOld.Revisions.Any())
        {
            //LogToFile($"ReviewOld {reviewOld.ReviewId} has no Revisions...");
            continue;
        }

        var packageName = reviewOld.Revisions.Last().Files.First().PackageName;
        var language = reviewOld.Revisions.Last().Files.First().Language;
        var reviewNew = new ReviewModel();

        if (IsOfficialPackageName(packageName, language, officialPackageNames))
        {
            // In cases where we don't find an explicit packagename
            if (language == "C#")
            {
                packageName = ResolvePackageNameCSharp(reviewOld, officialPackageNames);
            }
            else if (language == "Go")
            {
                packageName = ResolvePackageNameGo(reviewOld);
            }
            else if (language == "Java")
            {
                packageName = ResolvePackageNameJava(reviewOld);
            }
            else if (language == "JavaScript" || language == "Python")
            {
                ResolvePackageNameJavaScriptOrPython(reviewOld);
            }
        }

        if (!String.IsNullOrEmpty(packageName) && !String.IsNullOrEmpty(language) && IsOfficialPackageName(packageName, language, officialPackageNames))
        {
            var reviewsNew = new List<ReviewModel>();
            var queryNew = $@"SELECT * FROM c WHERE c.PackageName = @packageName AND c.Language = @language";
            var queryDefinitionNew = new QueryDefinition(queryNew)
                .WithParameter("@packageName", packageName)
                .WithParameter("@language", language);
            var itemQueryIteratorNew = reviewsContainerNew.GetItemQueryIterator<ReviewModel>(queryDefinitionNew);

            while (itemQueryIteratorNew.HasMoreResults)
            {
                var result = await itemQueryIteratorNew.ReadNextAsync();
                reviewsNew.AddRange(result.Resource);
            }
            if (reviewsNew.Count() == 1)
            {
                reviewNew = reviewsNew.First();
            }
            else
            {
                if (reviewsNew.Count() > 1)
                {
                    //LogToFile($"Multiple Reviews with same PackageName : {packageName} and Language : {language}...[ {reviewsNew.Select(r => r.Id).ToArray()} ]");
                    continue;
                }
                else
                {
                    continue; 
                    // Create new Review for that package name and language
                    var review = new ReviewModel();
                    review.Id = Guid.NewGuid().ToString("N");
                    review.Language = language;
                    review.IsClosed = true;
                    review.PackageName = packageName;
                    await reviewsContainerNew.UpsertItemAsync(review, new PartitionKey(review.Id));
                    reviewNew = review;
                }
            }

            foreach (var revisionOld in reviewOld.Revisions)
            {
                if (language != "Swagger" && language != "TypeSpec" && reviewOld.Revisions.Count > 1 && reviewOld.FilterType == APIRevisionType.PullRequest && revisionOld.RevisionNumber == 0)
                {
                    //LogToFile($"RevisionOld {revisionOld.RevisionId} is baseline of a PR Review...");
                    continue;
                }

                List<PatchOperation> reviewNewPatchOperations = new List<PatchOperation>();

                // Create Revision Document
                var revisionNew = new APIRevisionModel();
                revisionNew.Id = revisionOld.RevisionId;
                revisionNew.ReviewId = reviewNew.Id;
                revisionNew.PackageName = reviewNew.PackageName;
                revisionNew.Language = reviewNew.Language;

                foreach (var file in revisionOld.Files)
                {
                    revisionNew.Files.Add(
                        new APICodeFileModel() 
                        {
                            FileId = file.ReviewFileId,
                            Name = file.Name,
                            Language = file.Language,
                            VersionString = file.VersionString,
                            LanguageVariant = file.LanguageVariant,
                            HasOriginal = file.HasOriginal,
                            CreationDate = file.CreationDate,
                            RunAnalysis = file.RunAnalysis,
                            PackageName = file.PackageName,
                            FileName = file.FileName,
                            PackageVersion = file.PackageVersion
                        }
                    );
                }
                revisionNew.Label = revisionOld.Label;
                revisionNew.ChangeHistory.Add(new APIRevisionChangeHistoryModel()
                {
                    ChangeAction = APIRevisionChangeAction.Created,
                    ChangedBy = revisionOld.Author,
                    ChangedOn = revisionOld.CreationDate
                });
                revisionNew.CreatedBy = revisionOld.Author;
                revisionNew.CreatedOn = revisionOld.CreationDate;

                reviewNew.IsClosed = false;
                if (reviewNew.ChangeHistory.Any() && reviewNew.ChangeHistory.Where(ch => ch.ChangeAction == ReviewChangeAction.Created).Any())
                {
                    if (
                        reviewNew.ChangeHistory.First(ch => ch.ChangeAction == ReviewChangeAction.Created).ChangedOn == default(DateTime) ||
                        reviewNew.ChangeHistory.First(ch => ch.ChangeAction == ReviewChangeAction.Created).ChangedOn > revisionOld.CreationDate)
                    {
                        reviewNew.ChangeHistory.First(ch => ch.ChangeAction == ReviewChangeAction.Created).ChangedOn = revisionOld.CreationDate;
                        reviewNew.ChangeHistory.First(ch => ch.ChangeAction == ReviewChangeAction.Created).ChangedBy = revisionOld.Author;
                        reviewNew.CreatedOn = revisionOld.CreationDate;
                        reviewNew.CreatedBy = revisionOld.Author;
                    }
                }
                else
                {
                    reviewNew.ChangeHistory.Add(new ReviewChangeHistoryModel()
                    {
                        ChangeAction = ReviewChangeAction.Created,
                        ChangedBy = revisionOld.Author,
                        ChangedOn = revisionOld.CreationDate
                    });
                    reviewNew.CreatedOn = revisionOld.CreationDate;
                    reviewNew.CreatedBy = revisionOld.Author;
                }

                if (reviewNew.LastUpdatedOn == default(DateTime) || reviewNew.LastUpdatedOn > revisionNew.CreatedOn)
                {
                    reviewNew.LastUpdatedOn = revisionNew.CreatedOn;
                }

                if (revisionOld.IsApproved)
                {
                    reviewNew.IsApproved = true;
                    revisionNew.IsApproved = true;
                    foreach (var approver in revisionOld.Approvers)
                    {
                        revisionNew.ChangeHistory.Add(new APIRevisionChangeHistoryModel()
                        {
                            ChangeAction = APIRevisionChangeAction.Approved,
                            ChangedBy = approver
                        });
                        revisionNew.Approvers.Add(approver);
                    }
                }

                revisionNew.APIRevisionType = reviewOld.FilterType;
                if (revisionOld.HeadingsOfSectionsWithDiff.Where(items => items.Value.Any()).Any())
                {
                    foreach (var key in revisionOld.HeadingsOfSectionsWithDiff.Keys)
                    {
                        if (revisionOld.HeadingsOfSectionsWithDiff[key].Any())
                        {
                            revisionNew.HeadingsOfSectionsWithDiff.Add(key, revisionOld.HeadingsOfSectionsWithDiff[key]);
                        }
                    }
                }
                revisionNew.IsDeleted = false;

                Console.WriteLine($"Patching Review: {reviewNew.Id}");
                LogToFile($"Patching Review: {reviewNew.Id}", type: LogType.Review);
                reviewNewPatchOperations.Add(PatchOperation.Set("/IsClosed", reviewNew.IsClosed));
                reviewNewPatchOperations.Add(PatchOperation.Set("/IsApproved", reviewNew.IsApproved));
                reviewNewPatchOperations.Add(PatchOperation.Set("/ChangeHistory", reviewNew.ChangeHistory));
                reviewNewPatchOperations.Add(PatchOperation.Set("/CreatedOn", reviewNew.CreatedOn));
                reviewNewPatchOperations.Add(PatchOperation.Set("/LastUpdatedOn", reviewNew.LastUpdatedOn));
                reviewNewPatchOperations.Add(PatchOperation.Set("/CreatedBy", reviewNew.CreatedBy));
                await reviewsContainerNew.PatchItemAsync<ReviewModel>(
                    id: reviewNew.Id,
                    partitionKey: new PartitionKey(reviewNew.Id),
                    patchOperations: reviewNewPatchOperations);

                Console.WriteLine($"Creating Revision: {revisionNew.Id}");
                LogToFile($"Creating Revision: {revisionNew.Id}", type: LogType.Revision);
                await revisionsContainerNew.UpsertItemAsync(revisionNew, new PartitionKey(revisionNew.ReviewId));
            }
        }
        else
        {
            //LogToFile($"ReviewOld {reviewOld.ReviewId} has no PackageName or Language in its Revisions");
            continue;
        }
    }
}

// For All Languages Except Swagger and TypeSpec. Java need nodification to the logic to accomodate GroupId
[Obsolete]
static async Task CreateRevewsFromCSVFiles(Container reviewsContainerNew, string filePAth, string language)
{

    using (TextFieldParser parser = new TextFieldParser(filePAth))
    {
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");

        int rowCount = 0;
        while (!parser.EndOfData)
        {
            string[] feilds = parser.ReadFields();
            int columnCount = 0;

            var review = new ReviewModel();
            review.Id = Guid.NewGuid().ToString("N");
            review.Language = language;
            review.IsClosed = true;

            bool isNewPackage = false;
            bool isActive = false;
            bool isHidden = false;

            foreach (var feild in feilds)
            {
                if (columnCount == 0)
                    review.PackageName = feild;

                if (columnCount == 3)
                    //review.PackageDisplayName = feild;

                if (columnCount == 4)
                    //review.ServiceName = feild;

                if (columnCount == 8)
                   // review.PackageType = feild;

                if (columnCount == 9 && feild == "true")
                    isNewPackage = true;

                if (columnCount == 13 && feild != "deprecated")
                    isActive = true;

                if (columnCount == 15 && feild == "true")
                    isHidden = true;

                columnCount++;
            }

            if (rowCount > 0 && isNewPackage && isActive && !isHidden && !String.IsNullOrEmpty(review.PackageName))
            {
                Console.Write("Creating Review...");
                await reviewsContainerNew.UpsertItemAsync(review, new PartitionKey(review.Id));
            }

            rowCount++;
        }
    }
}

/// Get official Package Names from csv file
static HashSet<string> GetOfficialPackageNamesFromCSVFile(string filePAth)
{
    HashSet<string> officialPackageNames = new HashSet<string>();
    using (TextFieldParser parser = new TextFieldParser(filePAth))
    {
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        var rowCount = 0;

        while (!parser.EndOfData)
        {
            string[] feilds = parser.ReadFields();

            if (rowCount > 0)
            {
                foreach (var feild in feilds)
                {
                    officialPackageNames.Add(feild);
                    break;
                }
            }
            rowCount++;
        }
    }
    return officialPackageNames;
}

static string ResolvePackageNameCSharp(ReviewModelOld reviewOld, HashSet<string> officialPackageNames)
{
    string result = String.Empty;
    var revisionsWithFileNames = reviewOld.Revisions.Where(r => !String.IsNullOrEmpty(r.Files[0].FileName));
    if (revisionsWithFileNames.Any())
    {
        var fileName = revisionsWithFileNames.Last().Files[0].FileName;
        if (fileName.EndsWith(".dll"))
        {
            result = fileName.Replace(".dll", "");
        }
        else
        {
            result = fileName;
        }
    }

    if (String.IsNullOrEmpty(result) || !IsOfficialPackageName(result, "C#", officialPackageNames))
    {
        var revisionsWithNames = reviewOld.Revisions.Where(r => !String.IsNullOrEmpty(r.Files[0].Name));
        if (revisionsWithNames.Any())
        {
            var name = revisionsWithNames.Last().Files[0].Name;
            if (name.EndsWith(".dll"))
            {
                result = name.Replace(".dll", "");
            }
            else if (name.Contains('(') && name.Contains(')'))
            {
                result = name.Split(' ')[0];
            }
            else 
            {
                result = name;
            }
        }
    }
    return result;
}

static string ResolvePackageNameGo(ReviewModelOld reviewOld)
{
    string result = String.Empty;
    var revisionsWithNames = reviewOld.Revisions.Where(r => !String.IsNullOrEmpty(r.Files[0].Name));
    if (revisionsWithNames.Any())
    {
        result = revisionsWithNames.Last().Files[0].Name;
    }
    return result;
}

static string ResolvePackageNameJava(ReviewModelOld reviewOld)
{
    string result = String.Empty;
    var revisionsWithNames = reviewOld.Revisions.Where(r => !String.IsNullOrEmpty(r.Files[0].Name));
    if (revisionsWithNames.Any())
    {
        var name = revisionsWithNames.Last().Files[0].Name;
        var regex = new Regex(@"(.+)-[0-9]\.[0-9]\.[0-9]-.*", RegexOptions.IgnoreCase);

        if (name.Contains('(') && name.Contains(')'))
        {
            result = name.Split(' ')[0];
        }
        else if (regex.IsMatch(name))
        {
            Match match = regex.Match(name);
            result = match.Groups[1].Value;
        }
        else if (name.EndsWith(".sources.jar"))
        {
            result = name.Replace(".sources.jar", "");
        }
        else
        {
            result = name;
        }
    }
    return result;
}

static string ResolvePackageNameJavaScriptOrPython(ReviewModelOld reviewOld)
{
    string result = String.Empty;
    var revisionsWithNames = reviewOld.Revisions.Where(r => !String.IsNullOrEmpty(r.Files[0].Name));
    if (revisionsWithNames.Any())
    {
        result = revisionsWithNames.Last().Files[0].Name;
    }
    return result;
}

static bool IsOfficialPackageName (string name, string language, HashSet<string> officialPackageNames)
{
    if (language == "TypeSpec" || language == "Swagger")
    {
        return true;
    }
    if (language == "Go")
    {
        return officialPackageNames.Where(n => {
                var nameParts = n.Split('/');
                var pn = nameParts[nameParts.Length - 1];
                return pn == name;
            }).Any();
    }
    return officialPackageNames.Contains(name);
}

// The one to Use
static async Task CreateReviewsFromExistingReviews(Container reviewsContainerOld, Container reviewsContainerNew,
    Container prContainerOld, Container prContainerNew, Container samplesContainerOld, Container samplesContainerNew,
    string language, DateTime lastUpdate = default(DateTime), string? csvFilePath=null, int? limit=null)
{
    HashSet<string> officialPackageNames = new HashSet<string>();
    var reviewsOld = new List<ReviewModelOld>();
    var query = $"SELECT * FROM c WHERE c.Revisions[0].Files[0].Language = @Language";
    if (language == "Swagger" || language == "TypeSpec")
    {
        query = $"SELECT * FROM c WHERE c.FilterType != 0 AND c.Revisions[0].Files[0].Language = @Language";
    }
    var queryDefinition = new QueryDefinition(query).WithParameter("@Language", language);
    var itemQueryIterator = reviewsContainerOld.GetItemQueryIterator<ReviewModelOld>(queryDefinition);

    while (itemQueryIterator.HasMoreResults)
    {
        var result = await itemQueryIterator.ReadNextAsync();
        reviewsOld.AddRange(result.Resource);
    }

    if (!String.IsNullOrEmpty(csvFilePath))
    {
        officialPackageNames = GetOfficialPackageNamesFromCSVFile(csvFilePath);
    }

    var newReviews = new HashSet<string>();

    foreach (var reviewOld in reviewsOld)
    {
        if (lastUpdate != default(DateTime) && reviewOld.LastUpdated != default(DateTime) && reviewOld.LastUpdated <= lastUpdate)
        {
            continue;
        }

        var revisionsWithPackageNames = reviewOld.Revisions.Where(r => !String.IsNullOrEmpty(r.Files[0].PackageName));
        var reviewKey = String.Empty;

        if (!revisionsWithPackageNames.Any())
        {
            // In cases where we don't find an explicit packagename
            if (language == "C#")
            {
                reviewKey = ResolvePackageNameCSharp(reviewOld, officialPackageNames);
            }
            else if (language == "Go")
            {
                reviewKey = ResolvePackageNameGo(reviewOld);
            }
            else if (language == "Java")
            {
                reviewKey = ResolvePackageNameJava(reviewOld);
            }
            else if (language == "JavaScript" || language == "Python")
            {
                ResolvePackageNameJavaScriptOrPython(reviewOld);
            }
        }
        else 
        {
            reviewKey = revisionsWithPackageNames.Last().Files[0].PackageName;
        }

        // Handle cases where jave groupId is appended to package name
        if (language == "Java" && (reviewKey.StartsWith("com.azure:") || reviewKey.StartsWith("com.azure.resourcemanager:")))
        {
            reviewKey = reviewKey.StartsWith("com.azure:") ? reviewKey.Replace("com.azure:", "") : reviewKey.Replace("com.azure.resourcemanager:", "");
        }

        if (String.IsNullOrEmpty(reviewKey))
        {
            // LogToFile($"Skip Creating for ReviewId {reviewOld.ReviewId} Empty Package Name");
            continue;
        }

        if (language == "Swagger" || language == "TypeSpec" || IsOfficialPackageName(reviewKey, language, officialPackageNames))
        {
            var review = new ReviewModel();
            if (!newReviews.Contains(reviewKey))
            {
                // Create Review Document
                review.Id = Guid.NewGuid().ToString("N");
                review.PackageName = reviewKey;
                review.Language = language;
                review.IsClosed = true;
                review.IsApproved = false;
                review.IsDeleted = false;
                newReviews.Add(reviewKey);
            }
            else
            {
                review = await GetReviewByPackageName(reviewsContainerNew, reviewKey, language);
            }

            // Create Pull Requests Associated with the Review
            var pullRequestsOld = new List<PullRequestModelOld>();
            var prQuery = $"SELECT * FROM c WHERE c.ReviewId = @reviewId";
            var prQueryDefinition = new QueryDefinition(prQuery).WithParameter("@reviewId", reviewOld.ReviewId);
            var prItemQueryIterator = prContainerOld.GetItemQueryIterator<PullRequestModelOld>(prQueryDefinition);
            while (prItemQueryIterator.HasMoreResults)
            {
                var result = await prItemQueryIterator.ReadNextAsync();
                pullRequestsOld.AddRange(result.Resource);
            }

            foreach (var prModelOld in pullRequestsOld)
            {
                var prModelNew = new PullRequestModel();
                prModelNew.Id = prModelOld.PullRequestId;
                prModelNew.PullRequestNumber = prModelOld.PullRequestNumber;
                prModelNew.Commits = prModelOld.Commits;
                prModelNew.RepoName = prModelOld.RepoName;
                prModelNew.FilePath = prModelOld.FilePath;
                prModelNew.IsOpen = prModelOld.IsOpen;
                prModelNew.ReviewId = review.Id;
                prModelNew.CreatedBy = prModelOld.Author;
                prModelNew.PackageName = review.PackageName;
                prModelNew.Language = review.Language;
                prModelNew.Assignee = prModelOld.Assignee;
                prModelNew.IsDeleted = false;
                Console.WriteLine($"PR    : {reviewKey}");
                LogToFile($"PR    : {reviewKey}", type: LogType.PR);
                await prContainerNew.UpsertItemAsync(prModelNew, new PartitionKey(prModelNew.ReviewId));
            }

            // Create Sample Revisions Associated with the Review
            var samplesOld = new List<UsageSampleModel>();
            var samplesQuery = $"SELECT * FROM c WHERE c.ReviewId = @reviewId";
            var samplesQueryDefinition = new QueryDefinition(samplesQuery).WithParameter("@reviewId", reviewOld.ReviewId);
            var samplesItemQueryIterator = samplesContainerOld.GetItemQueryIterator<UsageSampleModel>(samplesQueryDefinition);
            while (samplesItemQueryIterator.HasMoreResults)
            {
                var result = await samplesItemQueryIterator.ReadNextAsync();
                samplesOld.AddRange(result.Resource);
            }

            foreach (var sampleOld in samplesOld)
            {
                foreach (var sampleOldRevision in sampleOld.Revisions)
                {
                    var sampleNewRevision = new SampleRevisionModel();
                    sampleNewRevision.Id = Guid.NewGuid().ToString("N");
                    sampleNewRevision.ReviewId = review.Id;
                    sampleNewRevision.PackageName = review.PackageName;
                    sampleNewRevision.Language = review.Language;
                    sampleNewRevision.FileId = sampleOldRevision.FileId;
                    sampleNewRevision.OriginalFileId = sampleOldRevision.OriginalFileId;
                    sampleNewRevision.OriginalFileName = sampleOldRevision.OriginalFileName;
                    sampleNewRevision.CreatedBy = sampleOldRevision.CreatedBy;
                    sampleNewRevision.CreatedOn = sampleOldRevision.CreatedOn;
                    sampleNewRevision.Title = sampleOldRevision.RevisionTitle;
                    sampleNewRevision.IsDeleted = sampleOldRevision.RevisionIsDeleted;
                    Console.WriteLine($"Sample: {reviewKey}");
                    LogToFile($"Sample: {reviewKey}", type: LogType.Samples);
                    await samplesContainerNew.UpsertItemAsync(sampleNewRevision, new PartitionKey(sampleNewRevision.ReviewId));
                }
            }

            if (reviewOld.RequestedReviewers != null)
            {
                foreach (var assignee in reviewOld.RequestedReviewers)
                {
                    review.AssignedReviewers.Append(
                        new ReviewAssignmentModel
                        {
                            AssignedTo = assignee,
                            AssignedBy = "azure-sdk",
                            AssignedOn = DateTime.UtcNow
                        });
                }
            }

            if (reviewOld.Subscribers != null)
            {
                review.Subscribers.Union(reviewOld.Subscribers);
            }
            Console.WriteLine($"Review: {reviewKey}");
            LogToFile($"Review: {reviewKey}", type: LogType.Review);
            await reviewsContainerNew.UpsertItemAsync(review, new PartitionKey(review.Id));

            if (limit != null)
            {
                limit--;
                if (limit == 0)
                {
                    break;
                }
            }
        }
        else 
        {
            //LogToFile($"Skip Creating Review : {reviewOld.ReviewId} with Resolved PackageName: {reviewKey}. Package name is not in Official List.");
        }
    }
}

static async Task<ReviewModel> GetReviewByPackageName(Container reviewsContainerNew, string packageName, string language)
{
    var query = $"SELECT * FROM c WHERE c.PackageName = @packageName AND c.Language = @language";
    var queryDefinition = new QueryDefinition(query)
        .WithParameter("@packageName", packageName)
        .WithParameter("@language", language);
    var itemQueryIterator = reviewsContainerNew.GetItemQueryIterator<ReviewModel>(queryDefinition);
    return (await itemQueryIterator.ReadNextAsync()).Resource.Single();
}

static async Task UpdatePullRequests(Container prContainerNew)
{
    var pullRequests = new List<PullRequestModel>();
    var prQuery = $"SELECT * FROM c";
    var prQueryDefinition = new QueryDefinition(prQuery);
    var prItemQueryIterator = prContainerNew.GetItemQueryIterator<PullRequestModel>(prQueryDefinition);
    while (prItemQueryIterator.HasMoreResults)
    {
        var result = await prItemQueryIterator.ReadNextAsync();
        pullRequests.AddRange(result.Resource);
    }

    foreach (var prModel in pullRequests)
    {
        await prContainerNew.UpsertItemAsync(prModel, new PartitionKey(prModel.ReviewId));
    }
}

// For each comment in Comments find the reviewOld using the review Id
// Find the Package and language form the first revision of review old
// Find the corresponding review new with the same package and language
// Update the comment with id from Review Old
// Update the Review Id 
async Task UpdateComments(Container commentsContainerOld, Container commentsContainerNew, Container reviewsContainerOld,
        Container reviewsContainerNew, Container revisionsContainerNew, DateTime lastUpdate = default(DateTime)) 
{
    var commentsOld = new List<CommentModelOld>();
    var commentsOldQuery = $"SELECT * FROM c";
    var commentsQueryDefinition = new QueryDefinition(commentsOldQuery);
    var itemQueryIterator = commentsContainerOld.GetItemQueryIterator<CommentModelOld>(commentsQueryDefinition);

    while (itemQueryIterator.HasMoreResults)
    {
        var result = await itemQueryIterator.ReadNextAsync();
        commentsOld.AddRange(result.Resource);
    }

    foreach (var comment in commentsOld)
    {
        if (lastUpdate != default(DateTime) &&
           ((comment.TimeStamp != default(DateTime) && comment.TimeStamp < lastUpdate) ||
           (comment.EditedTimeStamp != default(DateTime) && comment.EditedTimeStamp < lastUpdate))
           )
        {
            continue;
        }   
        bool reviewIDUpdated = false;

        if (!String.IsNullOrEmpty(comment.RevisionId))
        {
            try 
            {
                var revisionResponse = await revisionsContainerNew.ReadItemAsync<APIRevisionModel>(comment.RevisionId, new PartitionKey(comment.ReviewId));
                var revisionNew = revisionResponse.Resource;
                comment.ReviewId = revisionNew.ReviewId;
                reviewIDUpdated = true;
            }
            catch (Exception ex)
            {
                //LogToFile($"Failed retrieve Revision with Id { comment.RevisionId }");
            }
        }
        
        if (reviewIDUpdated == false)
        {
            try 
            {
                var reviewResponse = await reviewsContainerOld.ReadItemAsync<ReviewModelOld>(comment.ReviewId, new PartitionKey(comment.ReviewId));
                var reviewOld = reviewResponse.Resource;

                if (reviewOld.Revisions.Any() && reviewOld.Revisions.Last().Files.Any())
                {
                    var packageName = reviewOld.Revisions.Last().Files[0].PackageName;
                    var language = reviewOld.Revisions.Last().Files[0].Language;

                    if (String.IsNullOrEmpty(packageName) || String.IsNullOrEmpty(language))
                    {
                        //LogToFile($"ReviewOld {reviewOld.ReviewId} has no PackageName or Language in its Revisions");
                        continue;
                    }
                    else
                    {
                        var reviewNewQuery = $"SELECT * FROM c WHERE c.PackageName = @PackageName AND c.Language = @Language";
                        var reviewNewQueryDefinition = new QueryDefinition(reviewNewQuery)
                            .WithParameter("@PackageName", packageName)
                            .WithParameter("@Language", language);
                        var reviewsQueryIterator = reviewsContainerNew.GetItemQueryIterator<ReviewModel>(reviewNewQueryDefinition);

                        var result = await reviewsQueryIterator.ReadNextAsync();
                        var reviewNew = result.Resource.FirstOrDefault();

                        if (reviewNew == null)
                        {
                            //LogToFile($"ReviewNew for {reviewOld.ReviewId} not found");
                            continue;
                        }
                        else
                        {
                            comment.ReviewId = reviewNew.Id;
                        }
                    }
                }
                else
                {
                    //LogToFile($"ReviewOld {reviewOld.ReviewId} has no valid revisions or Files info");
                    continue;
                }
            }
            catch (Exception ex)
            {
                //LogToFile($"Failed retrieve Review with Id {comment.ReviewId}");
                continue;
            }
        }
        var commentNew = new CommentModel();
        commentNew.Id = comment.CommentId;
        commentNew.ReviewId = comment.ReviewId;
        commentNew.ReviewRevisionId = comment.RevisionId;
        commentNew.ElementId = comment.ElementId;
        commentNew.SectionClass = comment.SectionClass;
        commentNew.CommentText = comment.Comment;
        commentNew.ChangeHistory.Add(new CommentChangeHistoryModel()
        {
            ChangeAction = CommentChangeAction.Created,
            ChangedBy = comment.Username,
            ChangedOn = comment.TimeStamp
        });
        commentNew.CreatedOn = comment.TimeStamp;
        commentNew.CreatedBy = comment.Username;
        if (comment.EditedTimeStamp != null)
        {
            commentNew.ChangeHistory.Add(new CommentChangeHistoryModel()
            {
                ChangeAction = CommentChangeAction.Edited,
                ChangedBy = comment.Username,
                ChangedOn = comment.EditedTimeStamp
            });
        }
        commentNew.LastEditedOn = comment.EditedTimeStamp;
        commentNew.IsResolved = comment.IsResolve;
        commentNew.Upvotes = comment.Upvotes;
        commentNew.TaggedUsers = comment.TaggedUsers;
        commentNew.CommentType = (comment.IsUsageSampleComment) ? CommentType.SampleRevision : CommentType.ReviewRevision;
        commentNew.ResolutionLocked = comment.ResolutionLocked;
        commentNew.IsDeleted = false;

        await commentsContainerNew.UpsertItemAsync(commentNew, new PartitionKey(commentNew.ReviewId));
        Console.WriteLine($"Creating New Comment with Id {commentNew.Id} with ReviewId {commentNew.ReviewId}");
    }   
}
static async Task DeleteReviews(Container reviewsContainerNew, string language)
{
    var reviews = new List<ReviewModel>();
    var query = $"SELECT * FROM c WHERE c.Language = @Language";
    var queryDefinition = new QueryDefinition(query).WithParameter("@Language", language);
    var itemQueryIterator = reviewsContainerNew.GetItemQueryIterator<ReviewModel>(queryDefinition);

    while (itemQueryIterator.HasMoreResults)
    {
        var result = await itemQueryIterator.ReadNextAsync();
        reviews.AddRange(result.Resource);
    }

    foreach (var review in reviews)
    {
        Console.WriteLine("Deleting Review..");
        await reviewsContainerNew.DeleteItemAsync<ReviewModel>(review.Id, new PartitionKey(review.Id));
    }
}

static async Task DeletePullRequests(Container prContainerNew, string language)
{
    var prs = new List<PullRequestModel>();
    var query = $"SELECT * FROM c WHERE c.Language = @Language";
    var queryDefinition = new QueryDefinition(query).WithParameter("@Language", language);
    var itemQueryIterator = prContainerNew.GetItemQueryIterator<PullRequestModel>(queryDefinition);

    while (itemQueryIterator.HasMoreResults)
    {
        var result = await itemQueryIterator.ReadNextAsync();
        prs.AddRange(result.Resource);
    }

    foreach (var pr in prs)
    {
        Console.WriteLine("Deleting PullRequest..");
        await prContainerNew.DeleteItemAsync<PullRequestModel>(pr.Id, new PartitionKey(pr.ReviewId));
    }
}
static async Task DeleteAllRevisions(Container revisionsContainerNew)
{
    var revisions = new List<APIRevisionModel>();
    var query = $"SELECT * FROM c";
    var queryDefinition = new QueryDefinition(query);
    var itemQueryIterator = revisionsContainerNew.GetItemQueryIterator<APIRevisionModel>(queryDefinition);

    while (itemQueryIterator.HasMoreResults)
    {
        var result = await itemQueryIterator.ReadNextAsync();
        revisions.AddRange(result.Resource);
    }

    foreach (var revision in revisions)
    {
        Console.WriteLine("Deleting Revision..");
        await revisionsContainerNew.DeleteItemAsync<APIRevisionModel>(revision.Id, new PartitionKey(revision.ReviewId));
    }
}


///Move time back by a few minutes when runing script
//await DeleteAllRevisions(revisionsContainerNew);

//await UpdateComments(commentsContainerOld, commentsContainerNew, reviewsContainerOld, reviewsContainerNew, revisionsContainerNew);
//await AddRevisionIdsToReviews(reviewsContainerNew, revisionsContainerNew);
//await UpdatePullRequests(prContainerNew: prContainerNew);


//await CreateReviewsFromExistingReviews(reviewsContainerOld: reviewsContainerOld, reviewsContainerNew: reviewsContainerNew,
//   prContainerOld: prContainerOld, prContainerNew: prContainerNew, samplesContainerOld: samplesContainerOld, samplesContainerNew: samplesContainerNew,
//    language: "Swagger");

//await CreateRevisions(reviewsContainerOld: reviewsContainerOld, reviewsContainerNew: reviewsContainerNew, revisionsContainerNew: revisionsContainerNew, languages: new List<string> { "Swagger" });

enum LogType
{
    Review,
    Revision,
    Samples,
    PR
};













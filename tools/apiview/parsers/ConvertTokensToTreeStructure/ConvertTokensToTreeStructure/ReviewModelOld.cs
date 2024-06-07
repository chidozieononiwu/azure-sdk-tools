using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ConvertFlatToTreeTokens
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum APIRevisionType
    {
        Manual = 0,
        Automatic,
        PullRequest,
        All
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CommentType
    {
        APIRevision = 0,
        SampleRevision
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ReviewChangeAction
    {
        Created,
        Closed,
        ReOpened,
        Approved,
        ApprovalReverted,
        Deleted,
        Undeleted
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum APIRevisionChangeAction
    {
        Created = 0,
        Approved,
        ApprovalReverted,
        Deleted,
        Undeleted
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum CommentChangeAction
    {
        Created = 0,
        Edited,
        Deleted,
        Undeleted
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ReviewRevisionUserInteraction
    {
        Viewed = 0,
        Commented,
    }

    public abstract class ChangeHistoryModel
    {
        public string ChangedBy { get; set; }
        public DateTime? ChangedOn { get; set; }
        public string Notes { get; set; }

    }

    public class ReviewChangeHistoryModel : ChangeHistoryModel
    {
        public ReviewChangeAction ChangeAction { get; set; }
    }

    public class APIRevisionChangeHistoryModel : ChangeHistoryModel
    {
        public APIRevisionChangeAction ChangeAction { get; set; }
    }

    public class CommentChangeHistoryModel : ChangeHistoryModel
    {
        public CommentChangeAction ChangeAction { get; set; }
    }

    public class ReviewAssignmentModel
    {
        public string AssignedBy { get; set; }
        public string AssignedTo { get; set; }
        public DateTime AssignedOn { get; set; }
    }

    public class BaseListitemModel
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        public string PackageName { get; set; }
        public string Language { get; set; }
    }

    public class ReviewListItemModel : BaseListitemModel
    {
        public HashSet<string> Subscribers { get; set; } = new HashSet<string>();
        public List<ReviewChangeHistoryModel> ChangeHistory { get; set; } = new List<ReviewChangeHistoryModel>();
        public List<ReviewAssignmentModel> AssignedReviewers { get; set; } = new List<ReviewAssignmentModel>();
        public bool IsClosed { get; set; }
        public bool IsApproved { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime LastUpdatedOn { get; set; }
        public bool IsDeleted { get; set; }
    }

    public class APIRevisionListItemModel : BaseListitemModel
    {
        public string ReviewId { get; set; }
        public List<APICodeFileModel> Files { get; set; } = new List<APICodeFileModel>();
        public string Label { get; set; }
        public List<APIRevisionChangeHistoryModel> ChangeHistory { get; set; } = new List<APIRevisionChangeHistoryModel>();
        public APIRevisionType APIRevisionType { get; set; }
        public int? PullRequestNo { get; set; }
        public Dictionary<string, HashSet<int>> HeadingsOfSectionsWithDiff { get; set; } = new Dictionary<string, HashSet<int>>();
        public List<ReviewAssignmentModel> AssignedReviewers { get; set; } = new List<ReviewAssignmentModel>();
        public bool IsApproved { get; set; }
        public HashSet<string> Approvers { get; set; } = new HashSet<string>();
        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime LastUpdatedOn { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsReleased { get; set; }
        public DateTime ReleasedOn { get; set; }
        public HashSet<string> ViewedBy { get; set; } = new HashSet<string>();
    }

    public class APICodeFileModel
    {
        private string _language;
        public string FileId { get; set; }
        // This is field is more of a display name. It is set to name value returned by parser which has package name and version in following format
        // Package name ( Version )
        public string Name { get; set; }
        public string Language
        {
            get => _language ?? (Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "Json" : "C#");
            set => _language = value;
        }

        public string VersionString { get; set; }
        public string ParserStyle { get; set; }
        public string LanguageVariant { get; set; }
        public bool HasOriginal { get; set; }
        public DateTime CreationDate { get; set; } = DateTime.Now;
        [Obsolete("Back compat don't use directly")]
        public bool RunAnalysis { get; set; }
        // Field is used to store package name returned by parser. This is used to lookup review for a specific package
        public string PackageName { get; set; }
        // This field stores original file name uploaded to create review
        public string FileName { get; set; }
        public string PackageVersion { get; set; }
        public string CrossLanguagePackageId { get; set; }
    }
}

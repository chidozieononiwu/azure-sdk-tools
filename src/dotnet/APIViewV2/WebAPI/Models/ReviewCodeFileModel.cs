using WebAPI.Helpers;

namespace WebAPI.Models
{
    public class ReviewCodeFileModel
    {
        private string _language;

        public string ReviewFileId { get; set; } = IdHelper.GenerateId();
        // This is field is more of a display name. It is set to name value returned by parser which has package name and version in following format
        // Package name ( Version )
        public string Name { get; set; }
        public string Language
        {
            get => _language ?? (Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "Json" : "C#");
            set => _language = value;
        }
        public string VersionString { get; set; }
        public bool HasOriginal { get; set; }
        public DateTime CreationDate { get; set; } = DateTime.Now;
        // Field is used to store package name returned by parser. This is used to lookup review for a specific package
        public string PackageName { get; set; }
        // This field stores original file name uploaded to create review
        public string FileName { get; set; }
    }
}

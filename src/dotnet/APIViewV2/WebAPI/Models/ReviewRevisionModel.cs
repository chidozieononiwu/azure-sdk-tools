using System.Xml.Linq;
using Newtonsoft.Json;
using WebAPI.Helpers;

namespace WebAPI.Models
{
    public class ReviewRevisionModel
    {
        private string _name;

        [JsonProperty("id")]
        public string RevisionId { get; set; } = IdHelper.GenerateId();
        public List<ReviewCodeFileModel> Files { get; set; } = new List<ReviewCodeFileModel>();
        public DateTime CreationDate { get; set; }
        public string Name
        {
            get => _name ?? Files.FirstOrDefault()?.Name;
            set => _name = value;
        }


    }
}

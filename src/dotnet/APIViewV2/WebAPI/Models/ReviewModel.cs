using Newtonsoft.Json;
using WebAPI.Helpers;

namespace WebAPI.Models
{
    public class ReviewModel
    {
        [JsonProperty("id")]
        public string ReviewId { get; set; } = IdHelper.GenerateId();
        public string Name { get; set; }
        public string Author { get; set; }
        public DateTime CreationDate { get; set; }
        public bool IsClosed { get; set; }
        public List<ReviewRevisionModel> Revisions { get; set; } = new List<ReviewRevisionModel>();
        public HashSet<string> Subscribers { get; set; } = new HashSet<string>();
    }
}

namespace APIViewWeb.LeanModels
{
    public class CreateCommentModel
    {
        public string ReviewId { get; set; }
        public string ElementId { get; set; }
        public string CommentText { get; set; }
        public CommentType CommentType { get; set; }
        public string ApiRevisionId { get; set; }
        public string SampleRevisionId { get; set; }
        public bool ResolutionLocked { get; set; }
    }

    public class UpdateCommentModel
    {
        public string ReviewId { get; set; }
        public string CommentId { get; set; }
        public string CommentText { get; set; }
    }
}

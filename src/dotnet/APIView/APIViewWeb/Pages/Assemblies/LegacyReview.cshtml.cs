using System.Threading.Tasks;
using APIViewWeb.Managers;
using APIViewWeb.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace APIViewWeb.Pages.Assemblies
{
    public class LegacyReview: PageModel
    {
        private ICommentsManager _commentsManager;
        public readonly UserProfileCache _userProfileCache;

        public LegacyReview(ICommentsManager commentsManager, UserProfileCache userProfileCache)
        {
            _commentsManager = commentsManager;
            _userProfileCache = userProfileCache;
        }

        public string Id { get; set; }

        public ReviewCommentsModel Comments { get; set; }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            Id = id;
            Comments = await _commentsManager.GetReviewCommentsAsync(id);
            return Page();
        }
    }
}

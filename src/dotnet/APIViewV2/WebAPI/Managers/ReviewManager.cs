using WebAPI.Models;
using WebAPI.Repositories;

namespace WebAPI.Managers
{
    public class ReviewManager : IReviewManager
    {
        private readonly ICosmosReviewRepository _reviewsRepository;
        private readonly ILogger<ReviewManager> _logger;

        public ReviewManager(ILogger<ReviewManager> logger, ICosmosReviewRepository reviewRepository) 
        {
            _logger = logger;
            _reviewsRepository = reviewRepository;
        }

        public async Task<(IEnumerable<ReviewsListItemModel> Reviews, int TotalNumberOfReviews)> GetReviewsAsync(int offset, int limit)
        {
            return await _reviewsRepository.GetReviewsAsync(offset, limit);
        }
    }
}

using WebAPI.Models;

namespace WebAPI.Repositories
{
    public interface ICosmosReviewRepository
    {
        /// <summary>
        /// Retrieve Reviews from the Reviews container in CosmosDb after applying filter to the query
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public Task<(IEnumerable<ReviewsListItemModel> Reviews, int TotalNumberOfReviews)> GetReviewsAsync(int offset, int limit);
    }
}

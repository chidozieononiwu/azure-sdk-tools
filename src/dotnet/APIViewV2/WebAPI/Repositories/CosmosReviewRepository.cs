using System.Text;
using Microsoft.Azure.Cosmos;
using WebAPI.Controllers;
using WebAPI.Models;

namespace WebAPI.Repositories
{
    public class CosmosReviewRepository : ICosmosReviewRepository
    {
        private readonly Container _reviewsContainer;
        private readonly ILogger<CosmosReviewRepository> _logger;

        public CosmosReviewRepository(ILogger<CosmosReviewRepository> logger, CosmosClient cosmosClient)
        {
            _logger = logger;
            _reviewsContainer = cosmosClient.GetContainer("APIView", "Reviews");
        }

        public async Task<(IEnumerable<ReviewsListItemModel> Reviews, int TotalNumberOfReviews)> GetReviewsAsync(int offset, int limit)
        {
            (IEnumerable<ReviewsListItemModel> Reviews, int TotalNumberOfReviews) result = (Reviews: new List<ReviewsListItemModel>(), TotalNumberOfReviews: 0);

            var queryStringBuilder = new StringBuilder(@"
SELECT VALUE {
    Id: r.id,
    Name: r.Name,
    Author: r.Author,
    NoOfRevisions: ARRAY_LENGTH(r.Revisions),
    IsClosed: r.IsClosed,
    Subscribers: r.Subscribers,
    IsAutomatic: r.IsAutomatic,
    FilterType: r.FilterType,
    ServiceName: r.ServiceName,
    PackageDisplayName: r.PackageDisplayName,
    LastUpdated: r.LastUpdated
} FROM Reviews r");

            var countQuery = $"SELECT VALUE COUNT(1) FROM({queryStringBuilder.ToString()})";
            QueryDefinition countQueryDefinition = new QueryDefinition(countQuery);
            using FeedIterator<int> countFeedIterator = _reviewsContainer.GetItemQueryIterator<int>(countQueryDefinition);
            while (countFeedIterator.HasMoreResults)
            {
                result.TotalNumberOfReviews = (await countFeedIterator.ReadNextAsync()).SingleOrDefault();
            }

            queryStringBuilder.Append(" OFFSET @offset LIMIT @limit");
            var reviews = new List<ReviewsListItemModel>();
            QueryDefinition queryDefinition = new QueryDefinition(queryStringBuilder.ToString())
                .WithParameter("@offset", offset)
                .WithParameter("@limit", limit);

            using FeedIterator<ReviewsListItemModel> feedIterator = _reviewsContainer.GetItemQueryIterator<ReviewsListItemModel>(queryDefinition);
            while (feedIterator.HasMoreResults)
            {
                FeedResponse<ReviewsListItemModel> response = await feedIterator.ReadNextAsync();
                reviews.AddRange(response);
            }
            result.Reviews = reviews;
            return result;
        }
    }
}

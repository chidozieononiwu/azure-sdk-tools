using System.Reflection.Metadata.Ecma335;
using Microsoft.AspNetCore.Mvc;
using WebAPI.Managers;
using WebAPI.Models;

namespace WebAPI.Controllers;

public class ReviewsController : BaseApiController
{
    private readonly ILogger<ReviewsController> _logger;
    private readonly IReviewManager _reviewManager;

    public ReviewsController(ILogger<ReviewsController> logger, IReviewManager reviewManager)
    {
        _logger = logger;
        _reviewManager = reviewManager;
    }

    [HttpGet(Name = "GetReviews")]
    public async Task<ReviewsListModel> Get(int offset = 0, int limit = 100)
    {
        var result = await _reviewManager.GetReviewsAsync(offset, limit);
        ReviewsListModel reviewListModel = new ReviewsListModel()
        {
            TotalNumberOfReviews = result.TotalNumberOfReviews,
            Reviews = result.Reviews.ToList()
        };
        return reviewListModel; 
    }
}

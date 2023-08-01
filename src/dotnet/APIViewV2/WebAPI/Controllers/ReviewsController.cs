using Microsoft.AspNetCore.Mvc;

namespace WebAPI.Controllers;

public class ReviewsController : BaseApiController
{
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(ILogger<ReviewsController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IEnumerable<WeatherForecast> GetReviews()
    {

    }
}

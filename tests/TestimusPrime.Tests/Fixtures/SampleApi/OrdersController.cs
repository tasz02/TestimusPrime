using Microsoft.AspNetCore.Mvc;

namespace SampleApi;

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health() => Ok();

    [HttpPost]
    public IActionResult Create() => Created("/api/orders/1", new { id = 1 });
}

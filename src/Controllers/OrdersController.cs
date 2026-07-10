using Microsoft.AspNetCore.Mvc;
using StockWaveApi.Models;
using StockWaveApi.Services;

namespace StockWaveApi.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly OrderService _orderService;

    public OrdersController(OrderService orderService)
    {
        _orderService = orderService;
    }

    // GET /orders
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(_orderService.GetAllOrders());
    }

    // GET /orders/{id}
    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        var order = _orderService.GetOrderById(id);
        if (order is null)
            return NotFound(new ErrorResponse { Error = $"No existe un pedido con id \"{id}\"." });

        return Ok(order);
    }

    // POST /orders
    [HttpPost]
    public IActionResult Create([FromBody] CreateOrderRequest? body)
    {
        var items = body?.Items;

        var shapeError = _orderService.ValidateItemsShape(items);
        if (shapeError is not null)
            return BadRequest(new ErrorResponse { Error = shapeError });

        var (order, conflicts) = _orderService.CreateOrder(items!);

        if (conflicts is not null)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "Uno o más productos no tienen stock suficiente.",
                Details = conflicts,
            });
        }

        return StatusCode(201, order);
    }

    // PATCH /orders/{id}/status
    [HttpPatch("{id}/status")]
    public IActionResult UpdateStatus(string id, [FromBody] UpdateStatusRequest? body)
    {
        if (string.IsNullOrWhiteSpace(body?.Status))
            return BadRequest(new ErrorResponse { Error = "El campo \"status\" es obligatorio y debe ser un string." });

        var result = _orderService.UpdateOrderStatus(id, body.Status);

        if (result.NotFound)
            return NotFound(new ErrorResponse { Error = $"No existe un pedido con id \"{id}\"." });

        if (result.ErrorMessage is not null)
            return BadRequest(new ErrorResponse { Error = result.ErrorMessage });

        return Ok(result.Order);
    }
}
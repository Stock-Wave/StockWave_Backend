using Microsoft.AspNetCore.Mvc;
using StockWaveApi.Models;
using StockWaveApi.Services;

namespace StockWaveApi.Controllers;

[ApiController]
[Route("products")]
public class ProductsController : ControllerBase
{
    private readonly ProductService _productService;

    public ProductsController(ProductService productService)
    {
        _productService = productService;
    }

    // GET /products
    [HttpGet]
    public IActionResult GetAll()
    {
        return Ok(_productService.GetAllProducts());
    }

    // GET /products/{id}
    [HttpGet("{id}")]
    public IActionResult GetById(string id)
    {
        var product = _productService.GetProductById(id);
        if (product is null)
            return NotFound(new ErrorResponse { Error = $"No existe un producto con id \"{id}\"." });

        return Ok(product);
    }
}
using StockWaveApi.Data;
using StockWaveApi.Models;

namespace StockWaveApi.Services;

public class ProductService
{
    public const int LowStockThreshold = 5;
    private readonly ProductRepository _repository;

    public ProductService(ProductRepository repository)
    {
        _repository = repository;
    }

    private static ProductDto ToDto(Product product) => new()
    {
        Id = product.Id,
        Name = product.Name,
        Price = product.Price,
        Stock = product.Stock,
        Category = product.Category,
        LowStock = product.Stock <= LowStockThreshold, // se calcula siempre al vuelo
    };

    public List<ProductDto> GetAllProducts() =>
        _repository.GetAll().Select(ToDto).ToList();

    public ProductDto? GetProductById(string id)
    {
        var product = _repository.GetById(id);
        return product is null ? null : ToDto(product);
    }
}
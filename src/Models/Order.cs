namespace StockWaveApi.Models;

public class OrderItem
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class Order
{
    public string Id { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = "pending";
    public string CreatedAt { get; set; } = string.Empty;
}

public class CreateOrderItemRequest
{
    public string? ProductId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest>? Items { get; set; }
}

public class UpdateStatusRequest
{
    public string? Status { get; set; }
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public object? Details { get; set; }
}
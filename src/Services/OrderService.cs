using StockWaveApi.Data;
using StockWaveApi.Models;

namespace StockWaveApi.Services;

public class StockConflict
{
    public string ProductId { get; set; } = string.Empty;
    public string? ProductName { get; set; }
    public int? Requested { get; set; }
    public int? AvailableStock { get; set; }
    public string? Error { get; set; }
}

public class OrderTotals
{
    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
}

public class UpdateStatusResult
{
    public bool NotFound { get; set; }
    public string? ErrorMessage { get; set; }
    public Order? Order { get; set; }
}

public class OrderService
{
    private const int DiscountThresholdUnits = 5;
    private const decimal DiscountRate = 0.10m;

    private static readonly string[] ValidStatuses =
        { "pending", "confirmed", "shipped", "delivered", "cancelled" };

    private static readonly Dictionary<string, string> ForwardTransitions = new()
    {
        { "pending", "confirmed" },
        { "confirmed", "shipped" },
        { "shipped", "delivered" },
    };

    private static readonly string[] CancellableFrom = { "pending", "confirmed" };

    private readonly ProductRepository _productRepository;
    private readonly OrderRepository _orderRepository;

    public OrderService(ProductRepository productRepository, OrderRepository orderRepository)
    {
        _productRepository = productRepository;
        _orderRepository = orderRepository;
    }

    public OrderTotals CalculateTotals(List<OrderItem> items)
    {
        var subtotal = items.Sum(i => i.Quantity * i.UnitPrice);
        var totalUnits = items.Sum(i => i.Quantity);
        var discount = totalUnits >= DiscountThresholdUnits ? subtotal * DiscountRate : 0m;
        var total = subtotal - discount;
        return new OrderTotals { Subtotal = subtotal, Discount = discount, Total = total };
    }

    public string? ValidateItemsShape(List<CreateOrderItemRequest>? items)
    {
        if (items is null || items.Count == 0)
            return "El campo \"items\" debe ser una lista con al menos un producto.";

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (string.IsNullOrWhiteSpace(item.ProductId))
                return $"El item en la posición {i} tiene un \"productId\" inválido o ausente.";
            if (item.Quantity <= 0)
                return $"El item en la posición {i} tiene una \"quantity\" inválida (debe ser entero positivo).";
        }
        return null;
    }

    // Crea el pedido completo dentro de UNA sola transacción de Postgres:
    // valida stock, descuenta, calcula totales e inserta. Si cualquier paso
    // falla, se hace rollback y no queda nada a medio guardar.
    public (Order? order, List<StockConflict>? conflicts) CreateOrder(List<CreateOrderItemRequest> items)
    {
        using var conn = _orderRepository.OpenConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            var conflicts = new List<StockConflict>();
            var productsById = new Dictionary<string, Product>();

            foreach (var item in items)
            {
                var product = _productRepository.GetByIdInTransaction(conn, tx, item.ProductId!);
                if (product is null)
                {
                    conflicts.Add(new StockConflict { ProductId = item.ProductId!, Error = "Producto no encontrado" });
                    continue;
                }

                productsById[item.ProductId!] = product;

                if (item.Quantity > product.Stock)
                {
                    conflicts.Add(new StockConflict
                    {
                        ProductId = item.ProductId!,
                        ProductName = product.Name,
                        Requested = item.Quantity,
                        AvailableStock = product.Stock,
                    });
                }
            }

            if (conflicts.Count > 0)
            {
                tx.Rollback();
                return (null, conflicts);
            }

            // Precio congelado: se toma del producto leído DENTRO de esta misma transacción
            var enrichedItems = items.Select(item => new OrderItem
            {
                ProductId = item.ProductId!,
                Quantity = item.Quantity,
                UnitPrice = productsById[item.ProductId!].Price,
            }).ToList();

            foreach (var item in enrichedItems)
            {
                _productRepository.DecrementStock(conn, tx, item.ProductId, item.Quantity);
            }

            var totals = CalculateTotals(enrichedItems);

            var order = new Order
            {
                Id = _orderRepository.GetNextOrderId(conn, tx),
                Items = enrichedItems,
                Subtotal = totals.Subtotal,
                Discount = totals.Discount,
                Total = totals.Total,
                Status = "pending",
                CreatedAt = DateTime.UtcNow.ToString("o"),
            };

            _orderRepository.InsertOrder(conn, tx, order);

            tx.Commit();
            return (order, null);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public List<Order> GetAllOrders() => _orderRepository.GetAll();

    public Order? GetOrderById(string id) => _orderRepository.GetById(id);

    public UpdateStatusResult UpdateOrderStatus(string id, string newStatus)
    {
        using var conn = _orderRepository.OpenConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            var order = _orderRepository.GetByIdInTransaction(conn, tx, id);
            if (order is null)
            {
                tx.Rollback();
                return new UpdateStatusResult { NotFound = true };
            }

            if (!ValidStatuses.Contains(newStatus))
            {
                tx.Rollback();
                return new UpdateStatusResult { ErrorMessage = $"\"{newStatus}\" no es un estado válido." };
            }

            if (newStatus == "cancelled")
            {
                if (!CancellableFrom.Contains(order.Status))
                {
                    tx.Rollback();
                    return new UpdateStatusResult
                    {
                        ErrorMessage = $"No se puede cancelar un pedido en estado \"{order.Status}\". Solo desde \"pending\" o \"confirmed\"."
                    };
                }

                foreach (var item in order.Items)
                {
                    _productRepository.IncrementStock(conn, tx, item.ProductId, item.Quantity);
                }

                _orderRepository.UpdateStatus(conn, tx, id, "cancelled");
                order.Status = "cancelled";
                tx.Commit();
                return new UpdateStatusResult { Order = order };
            }

            var hasExpected = ForwardTransitions.TryGetValue(order.Status, out var expectedNext);
            if (!hasExpected || expectedNext != newStatus)
            {
                tx.Rollback();
                var expectedText = hasExpected ? expectedNext : "ninguno (estado final)";
                return new UpdateStatusResult
                {
                    ErrorMessage = $"No se puede pasar de \"{order.Status}\" a \"{newStatus}\". Siguiente válido: \"{expectedText}\"."
                };
            }

            _orderRepository.UpdateStatus(conn, tx, id, newStatus);
            order.Status = newStatus;
            tx.Commit();
            return new UpdateStatusResult { Order = order };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
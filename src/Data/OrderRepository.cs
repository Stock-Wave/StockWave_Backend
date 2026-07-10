using Npgsql;
using StockWaveApi.Models;

namespace StockWaveApi.Data;

public class OrderRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public OrderRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public string GetNextOrderId(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        // Secuencia real de Postgres: el id es único incluso si varias
        // instancias de Lambda corren en paralelo.
        using var cmd = new NpgsqlCommand("SELECT nextval('orders_id_seq')", conn, tx);
        var value = cmd.ExecuteScalar();
        return value!.ToString()!;
    }

    public Order? GetById(string id)
    {
        using var conn = _connectionFactory.Create();

        Order? order;
        using (var cmd = new NpgsqlCommand(
            "SELECT id, subtotal, discount, total, status, created_at FROM orders WHERE id = @id", conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            order = MapOrderRow(reader);
        }

        order.Items = GetItems(conn, id);
        return order;
    }

    public List<Order> GetAll()
    {
        using var conn = _connectionFactory.Create();
        var orders = new List<Order>();

        using (var cmd = new NpgsqlCommand(
            "SELECT id, subtotal, discount, total, status, created_at FROM orders ORDER BY created_at DESC", conn))
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                orders.Add(MapOrderRow(reader));
            }
        }

        foreach (var order in orders)
        {
            order.Items = GetItems(conn, order.Id);
        }

        return orders;
    }

    private List<OrderItem> GetItems(NpgsqlConnection conn, string orderId)
    {
        using var cmd = new NpgsqlCommand(
            "SELECT product_id, quantity, unit_price FROM order_items WHERE order_id = @orderId", conn);
        cmd.Parameters.AddWithValue("orderId", orderId);

        using var reader = cmd.ExecuteReader();
        var items = new List<OrderItem>();
        while (reader.Read())
        {
            items.Add(new OrderItem
            {
                ProductId = reader.GetString(0),
                Quantity = reader.GetInt32(1),
                UnitPrice = reader.GetDecimal(2),
            });
        }
        return items;
    }

    // Inserta el pedido completo (cabecera + items) dentro de la MISMA transacción
    // que ya viene abierta desde OrderService, para que no pueda quedar un pedido
    // a medio guardar si algo falla a mitad de camino.
    public void InsertOrder(NpgsqlConnection conn, NpgsqlTransaction tx, Order order)
    {
        using (var cmd = new NpgsqlCommand(
            @"INSERT INTO orders (id, subtotal, discount, total, status, created_at)
              VALUES (@id, @subtotal, @discount, @total, @status, @createdAt)", conn, tx))
        {
            cmd.Parameters.AddWithValue("id", order.Id);
            cmd.Parameters.AddWithValue("subtotal", order.Subtotal);
            cmd.Parameters.AddWithValue("discount", order.Discount);
            cmd.Parameters.AddWithValue("total", order.Total);
            cmd.Parameters.AddWithValue("status", order.Status);
            cmd.Parameters.AddWithValue("createdAt", DateTime.Parse(order.CreatedAt).ToUniversalTime());
            cmd.ExecuteNonQuery();
        }

        foreach (var item in order.Items)
        {
            using var cmd = new NpgsqlCommand(
                @"INSERT INTO order_items (order_id, product_id, quantity, unit_price)
                  VALUES (@orderId, @productId, @quantity, @unitPrice)", conn, tx);
            cmd.Parameters.AddWithValue("orderId", order.Id);
            cmd.Parameters.AddWithValue("productId", item.ProductId);
            cmd.Parameters.AddWithValue("quantity", item.Quantity);
            cmd.Parameters.AddWithValue("unitPrice", item.UnitPrice);
            cmd.ExecuteNonQuery();
        }
    }

    public void UpdateStatus(NpgsqlConnection conn, NpgsqlTransaction tx, string orderId, string newStatus)
    {
        using var cmd = new NpgsqlCommand(
            "UPDATE orders SET status = @status WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("status", newStatus);
        cmd.Parameters.AddWithValue("id", orderId);
        cmd.ExecuteNonQuery();
    }

    public Order? GetByIdInTransaction(NpgsqlConnection conn, NpgsqlTransaction tx, string id)
    {
        Order? order;
        using (var cmd = new NpgsqlCommand(
            "SELECT id, subtotal, discount, total, status, created_at FROM orders WHERE id = @id FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue("id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;
            order = MapOrderRow(reader);
        }

        using (var cmd = new NpgsqlCommand(
            "SELECT product_id, quantity, unit_price FROM order_items WHERE order_id = @orderId", conn, tx))
        {
            cmd.Parameters.AddWithValue("orderId", id);
            using var reader = cmd.ExecuteReader();
            var items = new List<OrderItem>();
            while (reader.Read())
            {
                items.Add(new OrderItem
                {
                    ProductId = reader.GetString(0),
                    Quantity = reader.GetInt32(1),
                    UnitPrice = reader.GetDecimal(2),
                });
            }
            order.Items = items;
        }

        return order;
    }

    public NpgsqlConnection OpenConnection() => _connectionFactory.Create();

    private static Order MapOrderRow(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Subtotal = reader.GetDecimal(1),
        Discount = reader.GetDecimal(2),
        Total = reader.GetDecimal(3),
        Status = reader.GetString(4),
        CreatedAt = reader.GetDateTime(5).ToString("o"),
    };
}
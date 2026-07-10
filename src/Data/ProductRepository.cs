using Npgsql;
using StockWaveApi.Models;

namespace StockWaveApi.Data;

public class ProductRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ProductRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public List<Product> GetAll()
    {
        using var conn = _connectionFactory.Create();
        using var cmd = new NpgsqlCommand("SELECT id, name, price, stock, category FROM products ORDER BY id", conn);
        using var reader = cmd.ExecuteReader();

        var products = new List<Product>();
        while (reader.Read())
        {
            products.Add(MapRow(reader));
        }
        return products;
    }

    public Product? GetById(string id)
    {
        using var conn = _connectionFactory.Create();
        using var cmd = new NpgsqlCommand(
            "SELECT id, name, price, stock, category FROM products WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRow(reader) : null;
    }

    // Descuenta stock dentro de una transacción ya abierta, para que la
    // creación de pedido sea atómica (ver OrderRepository.InsertOrder).
    public void DecrementStock(NpgsqlConnection conn, NpgsqlTransaction tx, string productId, int quantity)
    {
        using var cmd = new NpgsqlCommand(
            "UPDATE products SET stock = stock - @qty WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("qty", quantity);
        cmd.Parameters.AddWithValue("id", productId);
        cmd.ExecuteNonQuery();
    }

    public void IncrementStock(NpgsqlConnection conn, NpgsqlTransaction tx, string productId, int quantity)
    {
        using var cmd = new NpgsqlCommand(
            "UPDATE products SET stock = stock + @qty WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("qty", quantity);
        cmd.Parameters.AddWithValue("id", productId);
        cmd.ExecuteNonQuery();
    }

    // FOR UPDATE bloquea la fila durante la transacción, para que dos pedidos
    // concurrentes no lean el mismo stock "libre" y ambos lo descuenten de más.
    public Product? GetByIdInTransaction(NpgsqlConnection conn, NpgsqlTransaction tx, string id)
    {
        using var cmd = new NpgsqlCommand(
            "SELECT id, name, price, stock, category FROM products WHERE id = @id FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue("id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? MapRow(reader) : null;
    }

    public NpgsqlConnection OpenConnection() => _connectionFactory.Create();

    private static Product MapRow(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetString(0),
        Name = reader.GetString(1),
        Price = reader.GetDecimal(2),
        Stock = reader.GetInt32(3),
        Category = reader.GetString(4),
    };
}
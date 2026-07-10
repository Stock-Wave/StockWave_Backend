using Npgsql;

namespace StockWaveApi.Data;

// Única responsabilidad: abrir conexiones a Postgres.
// Si el día de mañana cambia el motor de base de datos o el proveedor,
// esta es la ÚNICA clase que hay que tocar; ni los repositorios ni los
// servicios de negocio se enteran del cambio.
public class DbConnectionFactory
{
    public NpgsqlConnection Create()
    {
        var connectionString = Environment.GetEnvironmentVariable("STOCKWAVE_DB_CONNECTION")
            ?? throw new InvalidOperationException("Falta la variable de entorno STOCKWAVE_DB_CONNECTION.");

        var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        return connection;
    }
}
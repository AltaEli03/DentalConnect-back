using Npgsql;

namespace DentalConnect.Tests;

public sealed class AppointmentConcurrencyTests
{
    [Fact]
    public async Task ConcurrentOverlappingConfirmationsAllowOneAndRejectTheOther()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(connectionString)) return; // CI supplies an isolated PostgreSQL service.
        var table = ((char)34) + "appt_concurrency_" + Guid.NewGuid().ToString("N") + ((char)34);
        await using (var setup = new NpgsqlConnection(connectionString))
        {
            await setup.OpenAsync();
            await using var create = setup.CreateCommand();
            create.CommandText = $"CREATE EXTENSION IF NOT EXISTS btree_gist; CREATE TABLE {table} (id text PRIMARY KEY, dentist_id text NOT NULL, patient_id text NOT NULL, idem_key text NOT NULL, starts_at timestamptz NOT NULL, ends_at timestamptz NOT NULL, status text NOT NULL, UNIQUE(patient_id, idem_key), EXCLUDE USING gist (dentist_id WITH =, tstzrange(starts_at, ends_at, '[)') WITH &&) WHERE (status = 'CONFIRMED')); INSERT INTO {table} VALUES ('a','d','p1','k1','2030-01-01T10:00:00Z','2030-01-01T11:00:00Z','PENDING'), ('b','d','p2','k2','2030-01-01T10:30:00Z','2030-01-01T11:30:00Z','PENDING');";
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            async Task<bool> Confirm(string id)
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var transaction = await connection.BeginTransactionAsync();
                try
                {
                    await using var update = connection.CreateCommand();
                    update.Transaction = transaction;
                    update.CommandText = $"UPDATE {table} SET status='CONFIRMED' WHERE id=@id";
                    update.Parameters.AddWithValue("id", id);
                    await update.ExecuteNonQueryAsync();
                    await Task.Delay(150);
                    await transaction.CommitAsync();
                    return true;
                }
                catch (PostgresException error) when (error.SqlState == "23P01")
                {
                    await transaction.RollbackAsync();
                    return false;
                }
            }

            var results = await Task.WhenAll(Confirm("a"), Confirm("b"));
            Assert.Single(results.Where(result => result));
            Assert.Single(results.Where(result => !result));

            await using var verify = new NpgsqlConnection(connectionString);
            await verify.OpenAsync();
            await using var read = verify.CreateCommand();
            read.CommandText = $"SELECT count(*) FROM {table} WHERE status='CONFIRMED'";
            Assert.Equal(1L, (long)(await read.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var cleanup = new NpgsqlConnection(connectionString);
            await cleanup.OpenAsync();
            await using var drop = cleanup.CreateCommand();
            drop.CommandText = $"DROP TABLE IF EXISTS {table}";
            await drop.ExecuteNonQueryAsync();
        }
    }
}

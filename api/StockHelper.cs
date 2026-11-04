using System.Data;
using Dapper;

namespace Api;

public static class StockHelper
{
    // Adds stock to one warehouse and records it as an IN movement. Returns the movement id.
    // Runs inside the caller's transaction, so nothing is saved unless the caller commits.
    public static async Task<int> AddStockAsync(IDbConnection conn, IDbTransaction tx, int productId, int warehouseId,
        int qty, int userId, string? note, int? purchaseOrderId)
    {
        var args = new { productId, warehouseId, qty, userId, note, purchaseOrderId };

        // Lock the stock row until commit. HOLDLOCK also locks a row that doesn't exist yet,
        // so two requests can't both create it.
        var current = await conn.QuerySingleOrDefaultAsync<int?>("""
            SELECT Quantity FROM Stock WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
            WHERE ProductId = @productId AND WarehouseId = @warehouseId
            """, args, tx);

        await conn.ExecuteAsync(current == null
            ? "INSERT INTO Stock (ProductId, WarehouseId, Quantity) VALUES (@productId, @warehouseId, @qty)"
            : "UPDATE Stock SET Quantity = Quantity + @qty WHERE ProductId = @productId AND WarehouseId = @warehouseId",
            args, tx);

        return await conn.ExecuteScalarAsync<int>("""
            INSERT INTO StockMovements (Type, ProductId, ToWarehouseId, Quantity, Note, UserId, PurchaseOrderId)
            OUTPUT INSERTED.Id
            VALUES ('IN', @productId, @warehouseId, @qty, @note, @userId, @purchaseOrderId)
            """, args, tx);
    }
}

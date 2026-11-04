using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/stock")]
public class StockController(Db db) : ControllerBase
{
    // Starts from Products so products with no stock at all still show up (quantity 0)
    private const string Joins = """
        FROM Products p
        LEFT JOIN Stock s ON s.ProductId = p.Id
        LEFT JOIN Warehouses w ON w.Id = s.WarehouseId
        CROSS APPLY (SELECT ISNULL(SUM(Quantity), 0) AS Total FROM Stock WHERE ProductId = p.Id) t
        """;
    private const string Where = """
        WHERE (@Term IS NULL OR p.Name LIKE '%' + @Term + '%' OR p.SKU LIKE '%' + @Term + '%')
          AND (@WarehouseId IS NULL OR s.WarehouseId = @WarehouseId)
          AND (@LowOnly IS NULL OR @LowOnly = 0 OR t.Total <= p.MinStock)
        """;

    [HttpGet]
    public async Task<PagedResult<StockRow>> List([FromQuery] StockQuery q)
    {
        using var conn = db.Connect();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) {Joins} {Where}", q);
        // A product without stock rows gets a negative Id so every row still has a unique key
        var items = await conn.QueryAsync<StockRow>($"""
            SELECT ISNULL(s.Id, -p.Id) AS Id, p.Id AS ProductId, p.Name AS ProductName, p.SKU,
                   s.WarehouseId, w.Name AS WarehouseName, ISNULL(s.Quantity, 0) AS Quantity,
                   t.Total AS TotalQuantity, p.MinStock,
                   CAST(CASE WHEN t.Total <= p.MinStock THEN 1 ELSE 0 END AS BIT) AS IsLow
            {Joins} {Where}
            ORDER BY p.Name, w.Name
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY
            """, q);
        return new(items, total);
    }

    [HttpGet("summary")]
    public async Task<StockSummary> Summary()
    {
        using var conn = db.Connect();
        return await conn.QuerySingleAsync<StockSummary>("""
            SELECT
                (SELECT ISNULL(SUM(s.Quantity * p.CostPrice), 0)
                 FROM Stock s JOIN Products p ON p.Id = s.ProductId) AS TotalStockValue,
                (SELECT COUNT(*) FROM Products p
                 WHERE (SELECT ISNULL(SUM(Quantity), 0) FROM Stock WHERE ProductId = p.Id) <= p.MinStock) AS LowStockCount,
                (SELECT COUNT(*) FROM StockMovements WHERE CreatedAt >= CAST(SYSDATETIME() AS DATE)) AS MovementsToday
            """);
    }
}

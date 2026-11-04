using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// Stock movements are a history log: they can be listed and created, never edited or deleted.
[ApiController]
[Route("api/movements")]
public class MovementsController(Db db) : ControllerBase
{
    private const string Joins = """
        FROM StockMovements m
        JOIN Products p ON p.Id = m.ProductId
        JOIN Users u ON u.Id = m.UserId
        LEFT JOIN Warehouses fw ON fw.Id = m.FromWarehouseId
        LEFT JOIN Warehouses tw ON tw.Id = m.ToWarehouseId
        LEFT JOIN PurchaseOrders po ON po.Id = m.PurchaseOrderId
        """;
    private const string Select = $"""
        SELECT m.Id, m.Type, m.ProductId, p.Name AS ProductName, p.SKU,
               m.FromWarehouseId, fw.Name AS FromWarehouseName,
               m.ToWarehouseId, tw.Name AS ToWarehouseName,
               m.Quantity, m.Note, u.FullName AS UserName, m.CreatedAt,
               m.PurchaseOrderId, po.Number AS PurchaseOrderNumber
        {Joins}
        """;
    private const string Where = """
        WHERE (@Term IS NULL OR p.Name LIKE '%' + @Term + '%' OR p.SKU LIKE '%' + @Term + '%' OR m.Note LIKE '%' + @Term + '%')
          AND (@Type IS NULL OR m.Type = @Type)
          AND (@WarehouseId IS NULL OR m.FromWarehouseId = @WarehouseId OR m.ToWarehouseId = @WarehouseId)
          AND (@From IS NULL OR m.CreatedAt >= @From)
          AND (@To IS NULL OR m.CreatedAt < DATEADD(DAY, 1, @To))
        """;

    [HttpGet]
    public async Task<PagedResult<Movement>> List([FromQuery] MovementQuery q)
    {
        using var conn = db.Connect();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) {Joins} {Where}", q);
        var items = await conn.QueryAsync<Movement>(
            $"{Select} {Where} ORDER BY m.CreatedAt DESC, m.Id DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", q);
        return new(items, total);
    }

    [HttpPost]
    public async Task<IActionResult> Create(MovementRequest r)
    {
        var type = r.Type.Trim().ToUpperInvariant();
        if (type == "ADJUST" && !User.IsInRole("Admin"))
            return StatusCode(403, new { message = "Only Admin can adjust stock." });

        // Keep only the warehouses this type uses
        int? from = type is "OUT" or "TRANSFER" ? r.FromWarehouseId : null;
        int? to = type is "IN" or "TRANSFER" or "ADJUST" ? r.ToWarehouseId : null;

        var error = type switch
        {
            not ("IN" or "OUT" or "TRANSFER" or "ADJUST") => "Type must be IN, OUT, TRANSFER or ADJUST.",
            "OUT" or "TRANSFER" when from == null => "Please select the From warehouse.",
            "IN" or "TRANSFER" when to == null => "Please select the To warehouse.",
            "ADJUST" when to == null => "Please select the warehouse.",
            "TRANSFER" when from == to => "From and To warehouses must be different.",
            not "ADJUST" when r.Quantity <= 0 => "Quantity must be greater than 0.",
            _ => null
        };
        if (error != null) return BadRequest(new { message = error });

        var userId = int.Parse(User.FindFirstValue("sub")!);
        var warehouseIds = new[] { from, to }.OfType<int>().Distinct().ToArray();
        var quantity = r.Quantity;
        var note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        int id;

        using var conn = db.Connect();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products WHERE Id = @ProductId", r, tx) == 0)
                return Fail("Product not found.");
            if (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Warehouses WHERE Id IN @warehouseIds", new { warehouseIds }, tx) != warehouseIds.Length)
                return Fail("Warehouse not found.");

            if (type == "IN")
            {
                // Shared with purchase order receiving
                id = await StockHelper.AddStockAsync(conn, tx, r.ProductId, to!.Value, quantity, userId, note, null);
            }
            else
            {
                // Lock this product's stock rows until commit, so two requests can't both spend the same stock.
                // HOLDLOCK also locks a row that doesn't exist yet, so two requests can't both create it.
                var stock = (await conn.QueryAsync<(int WarehouseId, int Quantity)>("""
                    SELECT WarehouseId, Quantity FROM Stock WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE ProductId = @ProductId AND WarehouseId IN @warehouseIds
                    """, new { r.ProductId, warehouseIds }, tx)).ToDictionary(s => s.WarehouseId, s => s.Quantity);

                int Available(int warehouseId) => stock.GetValueOrDefault(warehouseId);

                Task SetStock(int warehouseId, int qty) => conn.ExecuteAsync(
                    stock.ContainsKey(warehouseId)
                        ? "UPDATE Stock SET Quantity = @qty WHERE ProductId = @ProductId AND WarehouseId = @warehouseId"
                        : "INSERT INTO Stock (ProductId, WarehouseId, Quantity) VALUES (@ProductId, @warehouseId, @qty)",
                    new { r.ProductId, warehouseId, qty }, tx);

                if (type == "ADJUST")
                {
                    var current = Available(to!.Value);
                    var diff = r.Quantity - current;
                    if (diff == 0) return Fail($"Stock is already {current}.");

                    await SetStock(to.Value, r.Quantity);
                    quantity = Math.Abs(diff);
                    note = $"counted {r.Quantity}" + (note == null ? "" : $" - {note}");
                    if (diff < 0) // stock went down: record it as leaving the warehouse
                    {
                        from = to;
                        to = null;
                    }
                }
                else // OUT or TRANSFER
                {
                    if (Available(from!.Value) < quantity)
                        return Fail($"Not enough stock. Available: {Available(from.Value)}");

                    await SetStock(from.Value, Available(from.Value) - quantity);
                    if (to != null) await SetStock(to.Value, Available(to.Value) + quantity);
                }

                id = await conn.ExecuteScalarAsync<int>("""
                    INSERT INTO StockMovements (Type, ProductId, FromWarehouseId, ToWarehouseId, Quantity, Note, UserId)
                    OUTPUT INSERTED.Id
                    VALUES (@type, @ProductId, @from, @to, @quantity, @note, @userId)
                    """, new { type, r.ProductId, from, to, quantity, note, userId }, tx);
            }

            tx.Commit();
        }
        catch
        {
            // Connection is null when SQL Server already rolled the transaction back (e.g. deadlock)
            if (tx.Connection != null) tx.Rollback();
            throw;
        }

        return Ok(await conn.QuerySingleAsync<Movement>($"{Select} WHERE m.Id = @id", new { id }));

        IActionResult Fail(string message)
        {
            tx.Rollback();
            return BadRequest(new { message });
        }
    }
}

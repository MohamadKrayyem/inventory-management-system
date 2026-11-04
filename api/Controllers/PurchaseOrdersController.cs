using System.Data;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

// Status flow: DRAFT -> ORDERED -> PARTIAL -> RECEIVED, or DRAFT/ORDERED -> CANCELLED
[ApiController]
[Route("api/purchase-orders")]
public class PurchaseOrdersController(Db db) : ControllerBase
{
    private const string Joins = """
        FROM PurchaseOrders po
        JOIN Suppliers s ON s.Id = po.SupplierId
        JOIN Warehouses w ON w.Id = po.WarehouseId
        JOIN Users u ON u.Id = po.CreatedBy
        """;
    private const string Select = $"""
        SELECT po.Id, po.Number, po.SupplierId, s.Name AS SupplierName, po.WarehouseId, w.Name AS WarehouseName,
               po.Status, po.Note, u.FullName AS CreatedByName, po.CreatedAt, po.OrderedAt, po.ReceivedAt,
               ISNULL(t.TotalCost, 0) AS TotalCost, ISNULL(t.ItemCount, 0) AS ItemCount
        {Joins}
        OUTER APPLY (SELECT SUM(QuantityOrdered * UnitCost) AS TotalCost, COUNT(*) AS ItemCount
                     FROM PurchaseOrderItems WHERE PurchaseOrderId = po.Id) t
        """;
    private const string Where = """
        WHERE (@Term IS NULL OR po.Number LIKE '%' + @Term + '%' OR s.Name LIKE '%' + @Term + '%')
          AND (@Status IS NULL OR po.Status = @Status OR (@Status = 'OPEN' AND po.Status IN ('ORDERED', 'PARTIAL')))
          AND (@SupplierId IS NULL OR po.SupplierId = @SupplierId)
        """;

    private int UserId => int.Parse(User.FindFirstValue("sub")!);

    [HttpGet]
    public async Task<PagedResult<PurchaseOrderRow>> List([FromQuery] PurchaseOrderQuery q)
    {
        using var conn = db.Connect();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) {Joins} {Where}", q);
        var items = await conn.QueryAsync<PurchaseOrderRow>(
            $"{Select} {Where} ORDER BY po.Id DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", q);
        return new(items, total);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        using var conn = db.Connect();
        var po = await conn.QuerySingleOrDefaultAsync<PurchaseOrderDetail>($"{Select} WHERE po.Id = @id", new { id });
        if (po == null) return NotFound(new { message = "Purchase order not found." });

        po.Items = (await conn.QueryAsync<PurchaseOrderItem>("""
            SELECT i.Id, i.ProductId, p.Name AS ProductName, p.SKU, i.QuantityOrdered, i.QuantityReceived,
                   i.QuantityOrdered - i.QuantityReceived AS Remaining, i.UnitCost,
                   i.QuantityOrdered * i.UnitCost AS LineTotal
            FROM PurchaseOrderItems i
            JOIN Products p ON p.Id = i.ProductId
            WHERE i.PurchaseOrderId = @id
            ORDER BY p.Name
            """, new { id })).ToList();
        return Ok(po);
    }

    [HttpPost]
    public async Task<IActionResult> Create(PurchaseOrderRequest r)
    {
        using var conn = db.Connect();
        conn.Open();
        using var tx = conn.BeginTransaction();

        if (await CheckRequest(conn, tx, r) is string error) return BadRequest(new { message = error });

        var id = await conn.ExecuteScalarAsync<int>("""
            INSERT INTO PurchaseOrders (SupplierId, WarehouseId, Note, CreatedBy)
            OUTPUT INSERTED.Id
            VALUES (@SupplierId, @WarehouseId, @Note, @userId)
            """, new { r.SupplierId, r.WarehouseId, Note = Clean(r.Note), userId = UserId }, tx);
        await InsertItems(conn, tx, id, r.Items);

        tx.Commit();
        return await Get(id);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, PurchaseOrderRequest r)
    {
        using var conn = db.Connect();
        conn.Open();
        using var tx = conn.BeginTransaction();

        var status = await LockStatus(conn, tx, id);
        if (status == null) return NotFound(new { message = "Purchase order not found." });
        if (status != "DRAFT") return BadRequest(new { message = $"Only DRAFT purchase orders can be edited (this one is {status})." });
        if (await CheckRequest(conn, tx, r) is string error) return BadRequest(new { message = error });

        await conn.ExecuteAsync(
            "UPDATE PurchaseOrders SET SupplierId = @SupplierId, WarehouseId = @WarehouseId, Note = @Note WHERE Id = @id",
            new { id, r.SupplierId, r.WarehouseId, Note = Clean(r.Note) }, tx);
        // Simplest way to edit the lines of a draft: replace them all
        await conn.ExecuteAsync("DELETE FROM PurchaseOrderItems WHERE PurchaseOrderId = @id", new { id }, tx);
        await InsertItems(conn, tx, id, r.Items);

        tx.Commit();
        return await Get(id);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:int}/order")]
    public async Task<IActionResult> Order(int id)
    {
        using var conn = db.Connect();
        var rows = await conn.ExecuteAsync(
            "UPDATE PurchaseOrders SET Status = 'ORDERED', OrderedAt = SYSDATETIME() WHERE Id = @id AND Status = 'DRAFT'",
            new { id });
        if (rows == 1) return await Get(id);

        var status = await conn.ExecuteScalarAsync<string?>("SELECT Status FROM PurchaseOrders WHERE Id = @id", new { id });
        return status == null
            ? NotFound(new { message = "Purchase order not found." })
            : BadRequest(new { message = $"Only DRAFT purchase orders can be ordered (this one is {status})." });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        using var conn = db.Connect();
        // One statement, so a receive running at the same time can't slip in between the check and the update
        var rows = await conn.ExecuteAsync("""
            UPDATE PurchaseOrders SET Status = 'CANCELLED'
            WHERE Id = @id AND Status IN ('DRAFT', 'ORDERED')
              AND NOT EXISTS (SELECT 1 FROM PurchaseOrderItems WHERE PurchaseOrderId = @id AND QuantityReceived > 0)
            """, new { id });
        if (rows == 1) return await Get(id);

        var status = await conn.ExecuteScalarAsync<string?>("SELECT Status FROM PurchaseOrders WHERE Id = @id", new { id });
        return status switch
        {
            null => NotFound(new { message = "Purchase order not found." }),
            "CANCELLED" => BadRequest(new { message = "This purchase order is already cancelled." }),
            _ => BadRequest(new { message = "Cannot cancel: items have already been received." })
        };
    }

    [HttpPost("{id:int}/receive")]
    public async Task<IActionResult> Receive(int id, List<ReceiveLine> lines)
    {
        // Same item listed twice counts as one line
        var wanted = lines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (wanted.Count == 0) return BadRequest(new { message = "Nothing to receive." });

        var userId = UserId;
        using var conn = db.Connect();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            // Lock the PO row until commit, so two receives at the same time run one after the other
            var po = await conn.QuerySingleOrDefaultAsync<PurchaseOrderRow>(
                "SELECT Id, Number, Status, WarehouseId FROM PurchaseOrders WITH (UPDLOCK, ROWLOCK) WHERE Id = @id",
                new { id }, tx);
            if (po == null) return NotFound(new { message = "Purchase order not found." });
            if (po.Status is not ("ORDERED" or "PARTIAL"))
                return Fail($"Only ORDERED or PARTIAL purchase orders can be received (this one is {po.Status}).");

            var items = (await conn.QueryAsync<PurchaseOrderItem>("""
                SELECT i.Id, i.ProductId, p.Name AS ProductName, i.QuantityOrdered, i.QuantityReceived
                FROM PurchaseOrderItems i JOIN Products p ON p.Id = i.ProductId
                WHERE i.PurchaseOrderId = @id
                """, new { id }, tx)).ToDictionary(i => i.Id);

            // Check every line before changing anything
            foreach (var (itemId, qty) in wanted)
            {
                if (!items.TryGetValue(itemId, out var item))
                    return Fail($"Item {itemId} is not on this purchase order.");
                var remaining = item.QuantityOrdered - item.QuantityReceived;
                if (qty > remaining)
                    return Fail($"Cannot receive {qty} of {item.ProductName}: only {remaining} remaining.");
            }

            foreach (var (itemId, qty) in wanted)
            {
                var item = items[itemId];
                item.QuantityReceived += qty;
                await conn.ExecuteAsync(
                    "UPDATE PurchaseOrderItems SET QuantityReceived = QuantityReceived + @qty WHERE Id = @itemId",
                    new { itemId, qty }, tx);
                await StockHelper.AddStockAsync(conn, tx, item.ProductId, po.WarehouseId, qty, userId, $"Received {po.Number}", id);
            }

            var status = items.Values.All(i => i.QuantityReceived == i.QuantityOrdered) ? "RECEIVED" : "PARTIAL";
            await conn.ExecuteAsync("""
                UPDATE PurchaseOrders
                SET Status = @status, ReceivedAt = CASE WHEN @status = 'RECEIVED' THEN SYSDATETIME() END
                WHERE Id = @id
                """, new { id, status }, tx);

            tx.Commit();
        }
        catch
        {
            // Connection is null when SQL Server already rolled the transaction back (e.g. deadlock)
            if (tx.Connection != null) tx.Rollback();
            throw;
        }

        return await Get(id);

        IActionResult Fail(string message)
        {
            tx.Rollback();
            return BadRequest(new { message });
        }
    }

    // ---------- helpers ----------

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static Task<string?> LockStatus(IDbConnection conn, IDbTransaction tx, int id) =>
        conn.ExecuteScalarAsync<string?>("SELECT Status FROM PurchaseOrders WITH (UPDLOCK, ROWLOCK) WHERE Id = @id", new { id }, tx);

    // Returns an error message, or null if the supplier, warehouse and products are valid
    private static async Task<string?> CheckRequest(IDbConnection conn, IDbTransaction tx, PurchaseOrderRequest r)
    {
        var productIds = r.Items.Select(i => i.ProductId).ToArray();
        if (productIds.Distinct().Count() != productIds.Length) return "Each product can only be added once.";

        var (suppliers, warehouses, products) = await conn.QuerySingleAsync<(int, int, int)>("""
            SELECT (SELECT COUNT(*) FROM Suppliers WHERE Id = @SupplierId),
                   (SELECT COUNT(*) FROM Warehouses WHERE Id = @WarehouseId),
                   (SELECT COUNT(*) FROM Products WHERE Id IN @productIds)
            """, new { r.SupplierId, r.WarehouseId, productIds }, tx);

        if (suppliers == 0) return "Supplier not found.";
        if (warehouses == 0) return "Warehouse not found.";
        if (products != productIds.Length) return "Product not found.";
        return null;
    }

    private static Task InsertItems(IDbConnection conn, IDbTransaction tx, int id, List<PurchaseOrderItemRequest> items) =>
        conn.ExecuteAsync("""
            INSERT INTO PurchaseOrderItems (PurchaseOrderId, ProductId, QuantityOrdered, UnitCost)
            VALUES (@id, @ProductId, @Quantity, @UnitCost)
            """, items.Select(i => new { id, i.ProductId, i.Quantity, i.UnitCost }), tx);
}

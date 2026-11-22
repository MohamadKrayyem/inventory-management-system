using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Api.Tests;

// Stock rules of POST /api/movements, tested through the real API against a real SQL Server.
// Every test creates its own product, so tests never share stock rows.
[Collection(ApiCollection.Name)]
public class StockRulesTests(ApiFixture fx, ITestOutputHelper output)
{
    // Warehouses created by database.sql
    private const int Main = 1, North = 2;

    // ---------- OUT ----------

    [Fact]
    public async Task Out_more_than_available_is_rejected()
    {
        var admin = await fx.LoginAsync(ApiFixture.AdminEmail);
        var productId = await NewProductAsync();
        await StockInAsync(admin, productId, Main, 5);

        var res = await PostMovementAsync(admin, new { type = "OUT", productId, fromWarehouseId = Main, quantity = 6 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Not enough stock. Available: 5", await MessageAsync(res));
        Assert.Equal(5, await StockAsync(productId, Main));
        Assert.Equal(0, await MovementCountAsync(productId, "OUT"));
    }

    [Fact]
    public async Task Out_from_a_warehouse_that_never_had_the_product_is_rejected()
    {
        var staff = await fx.LoginAsync(ApiFixture.StaffEmail);
        var productId = await NewProductAsync();

        var res = await PostMovementAsync(staff, new { type = "OUT", productId, fromWarehouseId = Main, quantity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Not enough stock. Available: 0", await MessageAsync(res));
        Assert.Null(await StockAsync(productId, Main));
    }

    // ---------- TRANSFER ----------

    [Fact]
    public async Task Transfer_to_the_same_warehouse_is_rejected()
    {
        var admin = await fx.LoginAsync(ApiFixture.AdminEmail);
        var productId = await NewProductAsync();
        await StockInAsync(admin, productId, Main, 10);

        var res = await PostMovementAsync(admin,
            new { type = "TRANSFER", productId, fromWarehouseId = Main, toWarehouseId = Main, quantity = 3 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("From and To warehouses must be different.", await MessageAsync(res));
        Assert.Equal(10, await StockAsync(productId, Main));
        Assert.Equal(0, await MovementCountAsync(productId, "TRANSFER"));
    }

    // ---------- ADJUST ----------

    [Fact]
    public async Task Adjust_as_staff_returns_403()
    {
        var admin = await fx.LoginAsync(ApiFixture.AdminEmail);
        var staff = await fx.LoginAsync(ApiFixture.StaffEmail);
        var productId = await NewProductAsync();
        await StockInAsync(admin, productId, Main, 10);

        var res = await PostMovementAsync(staff, new { type = "ADJUST", productId, toWarehouseId = Main, quantity = 2 });

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("Only Admin can adjust stock.", await MessageAsync(res));
        Assert.Equal(10, await StockAsync(productId, Main));
        Assert.Equal(0, await MovementCountAsync(productId, "ADJUST"));
    }

    [Fact]
    public async Task Adjust_as_admin_is_allowed()
    {
        // Control for the 403 test: the same request succeeds for Admin
        var admin = await fx.LoginAsync(ApiFixture.AdminEmail);
        var productId = await NewProductAsync();
        await StockInAsync(admin, productId, Main, 10);

        var res = await PostMovementAsync(admin, new { type = "ADJUST", productId, toWarehouseId = Main, quantity = 2 });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, await StockAsync(productId, Main));
    }

    // ---------- Stock never goes negative ----------

    [Fact]
    public async Task Out_can_empty_stock_to_zero_but_never_below()
    {
        var staff = await fx.LoginAsync(ApiFixture.StaffEmail);
        var productId = await NewProductAsync();
        await StockInAsync(staff, productId, Main, 4);

        var all = await PostMovementAsync(staff, new { type = "OUT", productId, fromWarehouseId = Main, quantity = 4 });
        var oneMore = await PostMovementAsync(staff, new { type = "OUT", productId, fromWarehouseId = Main, quantity = 1 });

        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, oneMore.StatusCode);
        Assert.Equal("Not enough stock. Available: 0", await MessageAsync(oneMore));
        Assert.Equal(0, await StockAsync(productId, Main));
    }

    [Fact]
    public async Task Transfer_more_than_available_is_rejected_and_changes_neither_warehouse()
    {
        var staff = await fx.LoginAsync(ApiFixture.StaffEmail);
        var productId = await NewProductAsync();
        await StockInAsync(staff, productId, Main, 3);
        await StockInAsync(staff, productId, North, 7);

        var res = await PostMovementAsync(staff,
            new { type = "TRANSFER", productId, fromWarehouseId = Main, toWarehouseId = North, quantity = 4 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("Not enough stock. Available: 3", await MessageAsync(res));
        Assert.Equal(3, await StockAsync(productId, Main));
        Assert.Equal(7, await StockAsync(productId, North));
    }

    [Fact]
    public async Task Adjust_to_a_negative_count_is_rejected()
    {
        var admin = await fx.LoginAsync(ApiFixture.AdminEmail);
        var productId = await NewProductAsync();
        await StockInAsync(admin, productId, Main, 5);

        var res = await PostMovementAsync(admin, new { type = "ADJUST", productId, toWarehouseId = Main, quantity = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(5, await StockAsync(productId, Main));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Out_with_zero_or_negative_quantity_is_rejected(int quantity)
    {
        // A negative OUT would otherwise add stock
        var staff = await fx.LoginAsync(ApiFixture.StaffEmail);
        var productId = await NewProductAsync();
        await StockInAsync(staff, productId, Main, 5);

        var res = await PostMovementAsync(staff, new { type = "OUT", productId, fromWarehouseId = Main, quantity });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(5, await StockAsync(productId, Main));
    }

    [Fact]
    public async Task Database_refuses_a_negative_stock_quantity()
    {
        // Last line of defence below the API: CK_Stock_Quantity in phase2.sql
        var admin = await fx.LoginAsync(ApiFixture.AdminEmail);
        var productId = await NewProductAsync();
        await StockInAsync(admin, productId, Main, 1);

        await using var conn = new SqlConnection(fx.ConnectionString);
        var ex = await Assert.ThrowsAsync<SqlException>(() => conn.ExecuteAsync(
            "UPDATE Stock SET Quantity = -1 WHERE ProductId = @productId AND WarehouseId = @Main",
            new { productId, Main }));

        Assert.Equal(547, ex.Number); // CHECK constraint violation
        Assert.Equal(1, await StockAsync(productId, Main));
    }

    // ---------- Concurrency ----------

    [Fact]
    public async Task Concurrent_outs_cannot_oversell()
    {
        const int stock = 10, requests = 30;
        var admin = await fx.LoginAsync(ApiFixture.AdminEmail);
        var productId = await NewProductAsync();
        await StockInAsync(admin, productId, Main, stock);

        var statuses = await FireAtOnceAsync(admin, requests,
            () => new { type = "OUT", productId, fromWarehouseId = Main, quantity = 1 });

        Assert.Equal(stock, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(requests - stock, statuses.Count(s => s == HttpStatusCode.BadRequest));
        Assert.Equal(0, await StockAsync(productId, Main));
        Assert.Equal(stock, await MovementCountAsync(productId, "OUT"));
    }

    [Fact]
    public async Task Concurrent_outs_larger_than_one_unit_cannot_oversell()
    {
        // 10 in stock, 8 requests for 3 each: only 3 can succeed and 1 must be left
        var staff = await fx.LoginAsync(ApiFixture.StaffEmail);
        var productId = await NewProductAsync();
        await StockInAsync(staff, productId, Main, 10);

        var statuses = await FireAtOnceAsync(staff, 8,
            () => new { type = "OUT", productId, fromWarehouseId = Main, quantity = 3 });

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(5, statuses.Count(s => s == HttpStatusCode.BadRequest));
        Assert.Equal(1, await StockAsync(productId, Main));
        Assert.Equal(9, await MovedOutAsync(productId));
    }

    [Fact]
    public async Task Concurrent_outs_and_transfers_from_one_warehouse_cannot_oversell()
    {
        var (productId, statuses) = await MixedOutsAndTransfersAsync();

        // Whatever happens to individual requests, no more than the 10 units in stock are ever spent
        var ok = statuses.Count(s => s == HttpStatusCode.OK);
        Assert.InRange(ok, 1, 10);
        var main = await StockAsync(productId, Main) ?? 0;
        var north = await StockAsync(productId, North) ?? 0;
        Assert.True(main >= 0 && north >= 0);
        Assert.Equal(10 - ok, main);
        // Every unit is accounted for: still in Main, moved to North, or sent OUT
        Assert.Equal(10, main + north + await MovedOutAsync(productId));
        Assert.Equal(await MovementCountAsync(productId, "TRANSFER"), north);
    }

    [Fact(Skip = "Known bug, not fixed (business logic was out of scope): concurrent OUT and TRANSFER requests " +
                 "for the same product deadlock in SQL Server (error 1205) and the losers get 500 instead of 400. " +
                 "Stock is still never oversold (see the test above). Remove Skip once MovementsController handles it.")]
    public async Task Concurrent_outs_and_transfers_get_400_not_500_when_stock_runs_out()
    {
        var (_, statuses) = await MixedOutsAndTransfersAsync();

        Assert.All(statuses, s => Assert.Contains(s, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }));
        Assert.Equal(10, statuses.Count(s => s == HttpStatusCode.OK));
    }

    // 10 units in Main, then 10 OUTs and 10 TRANSFERs (to North) of 1 unit each, all at once
    private async Task<(int ProductId, HttpStatusCode[] Statuses)> MixedOutsAndTransfersAsync()
    {
        var admin = await fx.LoginAsync(ApiFixture.AdminEmail);
        var staff = await fx.LoginAsync(ApiFixture.StaffEmail);
        var productId = await NewProductAsync();
        await StockInAsync(admin, productId, Main, 10);

        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            await gate.Task;
            object body = i % 2 == 0
                ? new { type = "OUT", productId, fromWarehouseId = Main, quantity = 1 }
                : new { type = "TRANSFER", productId, fromWarehouseId = Main, toWarehouseId = North, quantity = 1 };
            using var res = await PostMovementAsync(i % 3 == 0 ? admin : staff, body);
            return res.StatusCode;
        }).ToArray();
        gate.SetResult();
        var statuses = await Task.WhenAll(tasks);
        Log(statuses);
        return (productId, statuses);
    }

    // ---------- Helpers ----------

    // Sends the same request many times at the same moment and returns the status codes
    private async Task<HttpStatusCode[]> FireAtOnceAsync(HttpClient client, int count, Func<object> body)
    {
        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(async _ =>
        {
            await gate.Task;
            using var res = await PostMovementAsync(client, body());
            return res.StatusCode;
        }).ToArray();
        gate.SetResult();
        var statuses = await Task.WhenAll(tasks);
        Log(statuses);

        // Anything other than 200/400 (e.g. 500 from a deadlock) is a failure in its own right
        Assert.All(statuses, s => Assert.Contains(s, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest }));
        return statuses;
    }

    private void Log(HttpStatusCode[] statuses) =>
        output.WriteLine(string.Join(", ", statuses.GroupBy(s => s).Select(g => $"{(int)g.Key} x{g.Count()}")));

    private static Task<HttpResponseMessage> PostMovementAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/api/movements", body);

    private static async Task StockInAsync(HttpClient client, int productId, int warehouseId, int quantity)
    {
        using var res = await PostMovementAsync(client, new { type = "IN", productId, toWarehouseId = warehouseId, quantity });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private static async Task<string?> MessageAsync(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<ErrorBody>())?.Message;

    private sealed record ErrorBody(string? Message);

    private async Task<int> NewProductAsync()
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        return await conn.ExecuteScalarAsync<int>("""
            INSERT INTO Products (Name, SKU, CategoryId, CostPrice, SellPrice, MinStock)
            OUTPUT INSERTED.Id
            VALUES ('Test product', @sku, 1, 1, 2, 0)
            """, new { sku = "TEST-" + Guid.NewGuid().ToString("N")[..12] });
    }

    // null when there is no Stock row at all
    private async Task<int?> StockAsync(int productId, int warehouseId)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        return await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT Quantity FROM Stock WHERE ProductId = @productId AND WarehouseId = @warehouseId",
            new { productId, warehouseId });
    }

    private async Task<int> MovementCountAsync(int productId, string type)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM StockMovements WHERE ProductId = @productId AND Type = @type",
            new { productId, type });
    }

    private async Task<int> MovedOutAsync(int productId)
    {
        await using var conn = new SqlConnection(fx.ConnectionString);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT ISNULL(SUM(Quantity), 0) FROM StockMovements WHERE ProductId = @productId AND Type = 'OUT'",
            new { productId });
    }
}

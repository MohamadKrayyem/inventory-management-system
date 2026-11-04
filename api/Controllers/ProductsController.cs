using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController(Db db) : ControllerBase
{
    private const string Select = """
        SELECT p.Id, p.Name, p.SKU, p.CategoryId, c.Name AS CategoryName,
               p.CostPrice, p.SellPrice, p.MinStock, p.IsActive, p.CreatedAt
        FROM Products p
        JOIN Categories c ON c.Id = p.CategoryId
        """;
    private const string Where = "WHERE @Term IS NULL OR p.Name LIKE '%' + @Term + '%' OR p.SKU LIKE '%' + @Term + '%'";

    [HttpGet]
    public async Task<PagedResult<Product>> List([FromQuery] ListQuery q)
    {
        using var conn = db.Connect();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Products p {Where}", q);
        var items = await conn.QueryAsync<Product>(
            $"{Select} {Where} ORDER BY p.Name OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", q);
        return new(items, total);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        using var conn = db.Connect();
        var item = await conn.QuerySingleOrDefaultAsync<Product>($"{Select} WHERE p.Id = @id", new { id });
        return item == null ? NotFound(new { message = "Product not found." }) : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Product p)
    {
        using var conn = db.Connect();
        if (!await CategoryExists(conn, p.CategoryId))
            return BadRequest(new { message = "Category not found." });

        var id = await conn.ExecuteScalarAsync<int>("""
            INSERT INTO Products (Name, SKU, CategoryId, CostPrice, SellPrice, MinStock, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@Name, @SKU, @CategoryId, @CostPrice, @SellPrice, @MinStock, @IsActive)
            """, p);
        return await Get(id);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Product p)
    {
        p.Id = id;
        using var conn = db.Connect();
        if (!await CategoryExists(conn, p.CategoryId))
            return BadRequest(new { message = "Category not found." });

        var rows = await conn.ExecuteAsync("""
            UPDATE Products
            SET Name = @Name, SKU = @SKU, CategoryId = @CategoryId, CostPrice = @CostPrice,
                SellPrice = @SellPrice, MinStock = @MinStock, IsActive = @IsActive
            WHERE Id = @Id
            """, p);
        return rows == 0 ? NotFound(new { message = "Product not found." }) : await Get(id);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        using var conn = db.Connect();
        var rows = await conn.ExecuteAsync("DELETE FROM Products WHERE Id = @id", new { id });
        return rows == 0 ? NotFound(new { message = "Product not found." }) : NoContent();
    }

    private static Task<bool> CategoryExists(System.Data.IDbConnection conn, int categoryId) =>
        conn.ExecuteScalarAsync<bool>("SELECT COUNT(1) FROM Categories WHERE Id = @categoryId", new { categoryId });
}

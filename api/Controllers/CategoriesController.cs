using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/categories")]
public class CategoriesController(Db db) : ControllerBase
{
    private const string Where = "WHERE @Term IS NULL OR Name LIKE '%' + @Term + '%'";

    [HttpGet]
    public async Task<PagedResult<Category>> List([FromQuery] ListQuery q)
    {
        using var conn = db.Connect();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Categories {Where}", q);
        var items = await conn.QueryAsync<Category>(
            $"SELECT Id, Name FROM Categories {Where} ORDER BY Name OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", q);
        return new(items, total);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        using var conn = db.Connect();
        var item = await conn.QuerySingleOrDefaultAsync<Category>("SELECT Id, Name FROM Categories WHERE Id = @id", new { id });
        return item == null ? NotFound(new { message = "Category not found." }) : Ok(item);
    }

    [HttpPost]
    public async Task<Category> Create(Category c)
    {
        using var conn = db.Connect();
        c.Id = await conn.ExecuteScalarAsync<int>("INSERT INTO Categories (Name) OUTPUT INSERTED.Id VALUES (@Name)", c);
        return c;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Category c)
    {
        c.Id = id;
        using var conn = db.Connect();
        var rows = await conn.ExecuteAsync("UPDATE Categories SET Name = @Name WHERE Id = @Id", c);
        return rows == 0 ? NotFound(new { message = "Category not found." }) : Ok(c);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        using var conn = db.Connect();
        var rows = await conn.ExecuteAsync("DELETE FROM Categories WHERE Id = @id", new { id });
        return rows == 0 ? NotFound(new { message = "Category not found." }) : NoContent();
    }
}

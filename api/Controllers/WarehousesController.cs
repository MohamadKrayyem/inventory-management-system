using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/warehouses")]
public class WarehousesController(Db db) : ControllerBase
{
    private const string Where = "WHERE @Term IS NULL OR Name LIKE '%' + @Term + '%' OR Location LIKE '%' + @Term + '%'";

    [HttpGet]
    public async Task<PagedResult<Warehouse>> List([FromQuery] ListQuery q)
    {
        using var conn = db.Connect();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Warehouses {Where}", q);
        var items = await conn.QueryAsync<Warehouse>(
            $"SELECT Id, Name, Location FROM Warehouses {Where} ORDER BY Name OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", q);
        return new(items, total);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        using var conn = db.Connect();
        var item = await conn.QuerySingleOrDefaultAsync<Warehouse>("SELECT Id, Name, Location FROM Warehouses WHERE Id = @id", new { id });
        return item == null ? NotFound(new { message = "Warehouse not found." }) : Ok(item);
    }

    [HttpPost]
    public async Task<Warehouse> Create(Warehouse w)
    {
        using var conn = db.Connect();
        w.Id = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO Warehouses (Name, Location) OUTPUT INSERTED.Id VALUES (@Name, @Location)", w);
        return w;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Warehouse w)
    {
        w.Id = id;
        using var conn = db.Connect();
        var rows = await conn.ExecuteAsync("UPDATE Warehouses SET Name = @Name, Location = @Location WHERE Id = @Id", w);
        return rows == 0 ? NotFound(new { message = "Warehouse not found." }) : Ok(w);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        using var conn = db.Connect();
        var rows = await conn.ExecuteAsync("DELETE FROM Warehouses WHERE Id = @id", new { id });
        return rows == 0 ? NotFound(new { message = "Warehouse not found." }) : NoContent();
    }
}

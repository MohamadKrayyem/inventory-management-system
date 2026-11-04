using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController(Db db) : ControllerBase
{
    private const string Columns = "Id, Name, Phone, Email, Address";
    private const string Where =
        "WHERE @Term IS NULL OR Name LIKE '%' + @Term + '%' OR Phone LIKE '%' + @Term + '%' OR Email LIKE '%' + @Term + '%'";

    [HttpGet]
    public async Task<PagedResult<Supplier>> List([FromQuery] ListQuery q)
    {
        using var conn = db.Connect();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM Suppliers {Where}", q);
        var items = await conn.QueryAsync<Supplier>(
            $"SELECT {Columns} FROM Suppliers {Where} ORDER BY Name OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", q);
        return new(items, total);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        using var conn = db.Connect();
        var item = await conn.QuerySingleOrDefaultAsync<Supplier>($"SELECT {Columns} FROM Suppliers WHERE Id = @id", new { id });
        return item == null ? NotFound(new { message = "Supplier not found." }) : Ok(item);
    }

    [HttpPost]
    public async Task<Supplier> Create(Supplier s)
    {
        using var conn = db.Connect();
        s.Id = await conn.ExecuteScalarAsync<int>(
            "INSERT INTO Suppliers (Name, Phone, Email, Address) OUTPUT INSERTED.Id VALUES (@Name, @Phone, @Email, @Address)", s);
        return s;
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Supplier s)
    {
        s.Id = id;
        using var conn = db.Connect();
        var rows = await conn.ExecuteAsync(
            "UPDATE Suppliers SET Name = @Name, Phone = @Phone, Email = @Email, Address = @Address WHERE Id = @Id", s);
        return rows == 0 ? NotFound(new { message = "Supplier not found." }) : Ok(s);
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        using var conn = db.Connect();
        var rows = await conn.ExecuteAsync("DELETE FROM Suppliers WHERE Id = @id", new { id });
        return rows == 0 ? NotFound(new { message = "Supplier not found." }) : NoContent();
    }
}

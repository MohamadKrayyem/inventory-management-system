using System.ComponentModel.DataAnnotations;

namespace Api;

// ---------- Tables ----------

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Staff";
    public DateTime CreatedAt { get; set; }
}

public class Category
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";
}

public class Supplier
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = "";

    [StringLength(30)]
    public string? Phone { get; set; }

    [EmailAddress, StringLength(150)]
    public string? Email { get; set; }

    [StringLength(250)]
    public string? Address { get; set; }
}

public class Warehouse
{
    public int Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [StringLength(250)]
    public string? Location { get; set; }
}

public class Product
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Name { get; set; } = "";

    [Required, StringLength(50)]
    public string SKU { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Please select a category.")]
    public int CategoryId { get; set; }

    public string? CategoryName { get; set; } // read only, filled by the list query

    [Range(0, 1_000_000_000)]
    public decimal CostPrice { get; set; }

    [Range(0, 1_000_000_000)]
    public decimal SellPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int MinStock { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

// ---------- Stock (phase 2) ----------

// One row per product per warehouse. A product with no stock rows shows once with no warehouse.
public class StockRow
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string SKU { get; set; } = "";
    public int? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public int Quantity { get; set; }
    public int TotalQuantity { get; set; } // across all warehouses
    public int MinStock { get; set; }
    public bool IsLow { get; set; }        // TotalQuantity <= MinStock
}

public class StockSummary
{
    public decimal TotalStockValue { get; set; }
    public int LowStockCount { get; set; }
    public int MovementsToday { get; set; }
}

public class Movement
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string SKU { get; set; } = "";
    public int? FromWarehouseId { get; set; }
    public string? FromWarehouseName { get; set; }
    public int? ToWarehouseId { get; set; }
    public string? ToWarehouseName { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public string UserName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? PurchaseOrderId { get; set; }        // set when the movement came from receiving a PO
    public string? PurchaseOrderNumber { get; set; }
}

public class MovementRequest
{
    [Required]
    public string Type { get; set; } = ""; // IN, OUT, TRANSFER, ADJUST

    [Range(1, int.MaxValue, ErrorMessage = "Please select a product.")]
    public int ProductId { get; set; }

    public int? FromWarehouseId { get; set; } // OUT, TRANSFER
    public int? ToWarehouseId { get; set; }   // IN, TRANSFER, and the counted warehouse for ADJUST

    [Range(0, 1_000_000)]
    public int Quantity { get; set; }         // for ADJUST: the counted quantity

    [StringLength(200)]
    public string? Note { get; set; }
}

// ---------- Purchase orders (phase 3) ----------

public class PurchaseOrderRow
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public string Status { get; set; } = ""; // DRAFT, ORDERED, PARTIAL, RECEIVED, CANCELLED
    public string? Note { get; set; }
    public decimal TotalCost { get; set; }
    public int ItemCount { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? OrderedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
}

public class PurchaseOrderDetail : PurchaseOrderRow
{
    public List<PurchaseOrderItem> Items { get; set; } = [];
}

public class PurchaseOrderItem
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string SKU { get; set; } = "";
    public int QuantityOrdered { get; set; }
    public int QuantityReceived { get; set; }
    public int Remaining { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}

public class PurchaseOrderRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Please select a supplier.")]
    public int SupplierId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Please select a warehouse.")]
    public int WarehouseId { get; set; }

    [StringLength(250)]
    public string? Note { get; set; }

    [MinLength(1, ErrorMessage = "Add at least one item.")]
    public List<PurchaseOrderItemRequest> Items { get; set; } = [];
}

public class PurchaseOrderItemRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Please select a product for every item.")]
    public int ProductId { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "Item quantity must be greater than 0.")]
    public int Quantity { get; set; }

    [Range(0, 1_000_000_000, ErrorMessage = "Unit cost cannot be negative.")]
    public decimal UnitCost { get; set; }
}

public class ReceiveLine
{
    public int ItemId { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "Receive quantity must be greater than 0.")]
    public int Quantity { get; set; }
}

// ---------- DTOs ----------

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
}

public record UserDto(int Id, string FullName, string Email, string Role);

public record LoginResponse(string Token, UserDto User);

public record PagedResult<T>(IEnumerable<T> Items, int Total);

// ?search=&page=&pageSize= for every GET list endpoint
public class ListQuery
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    // Cleaned values used as SQL parameters
    public string? Term => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
    public int Take => Math.Clamp(PageSize, 1, 500);
    public int Skip => (Math.Max(Page, 1) - 1) * Take;
}

public class StockQuery : ListQuery
{
    public int? WarehouseId { get; set; }
    public bool? LowOnly { get; set; }
}

public class MovementQuery : ListQuery
{
    public string? Type { get; set; }
    public int? WarehouseId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; } // inclusive: the whole "to" day is included
}

public class PurchaseOrderQuery : ListQuery
{
    public string? Status { get; set; } // a status, or OPEN for ORDERED + PARTIAL
    public int? SupplierId { get; set; }
}

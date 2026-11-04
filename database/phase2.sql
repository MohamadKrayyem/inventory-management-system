-- Inventory Management System - Phase 2: stock and stock movements
-- Run after database.sql. Only creates new tables and adds starting stock.
-- It does not change or delete existing data and is safe to run more than once.

USE InventoryDb;
GO

-- ---------- Tables ----------

IF OBJECT_ID('Stock') IS NULL
CREATE TABLE Stock (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    ProductId   INT NOT NULL CONSTRAINT FK_Stock_Products   REFERENCES Products(Id),
    WarehouseId INT NOT NULL CONSTRAINT FK_Stock_Warehouses REFERENCES Warehouses(Id),
    Quantity    INT NOT NULL CONSTRAINT DF_Stock_Quantity DEFAULT 0,
    CONSTRAINT UQ_Stock_Product_Warehouse UNIQUE (ProductId, WarehouseId),
    CONSTRAINT CK_Stock_Quantity CHECK (Quantity >= 0)
);
GO

IF OBJECT_ID('StockMovements') IS NULL
BEGIN
    CREATE TABLE StockMovements (
        Id              INT IDENTITY(1,1) PRIMARY KEY,
        Type            NVARCHAR(10)  NOT NULL CONSTRAINT CK_StockMovements_Type
                            CHECK (Type IN ('IN', 'OUT', 'TRANSFER', 'ADJUST')),
        ProductId       INT           NOT NULL CONSTRAINT FK_StockMovements_Products REFERENCES Products(Id),
        FromWarehouseId INT           NULL     CONSTRAINT FK_StockMovements_FromWarehouse REFERENCES Warehouses(Id),
        ToWarehouseId   INT           NULL     CONSTRAINT FK_StockMovements_ToWarehouse REFERENCES Warehouses(Id),
        Quantity        INT           NOT NULL CONSTRAINT CK_StockMovements_Quantity CHECK (Quantity > 0),
        Note            NVARCHAR(250) NULL,
        UserId          INT           NOT NULL CONSTRAINT FK_StockMovements_Users REFERENCES Users(Id),
        -- Server local time, so "today" and date filters match the business day
        CreatedAt       DATETIME2     NOT NULL CONSTRAINT DF_StockMovements_CreatedAt DEFAULT SYSDATETIME()
    );

    CREATE INDEX IX_StockMovements_CreatedAt ON StockMovements (CreatedAt DESC);
END
GO

-- ---------- Starting stock (only if no movements exist yet) ----------

DECLARE @AdminId INT = (SELECT TOP 1 Id FROM Users WHERE Role = 'Admin' ORDER BY Id);

IF @AdminId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM StockMovements)
BEGIN
    INSERT INTO Stock (ProductId, WarehouseId, Quantity)
    SELECT p.Id, w.Id, v.Quantity
    FROM (VALUES
        ('ELEC-001', 'Main Warehouse', 40),
        ('ELEC-001', 'North Depot',    15),
        ('ELEC-002', 'Main Warehouse', 25),
        ('ELEC-003', 'Main Warehouse',  4),   -- low (min 5)
        ('OFF-001',  'Main Warehouse', 120),
        ('OFF-001',  'South Storage',  60),
        ('OFF-002',  'North Depot',    30),   -- low (min 40)
        ('OFF-003',  'Main Warehouse', 18),
        ('FURN-001', 'South Storage',   3),   -- low (min 4)
        ('FURN-002', 'South Storage',   5),
        ('CLN-001',  'North Depot',    45),
        ('PKG-001',  'Main Warehouse', 300),
        ('PKG-001',  'South Storage',  150)
    ) AS v (SKU, Warehouse, Quantity)
    JOIN Products p ON p.SKU = v.SKU
    JOIN Warehouses w ON w.Id = (SELECT MIN(Id) FROM Warehouses WHERE Name = v.Warehouse)
    WHERE NOT EXISTS (SELECT 1 FROM Stock s WHERE s.ProductId = p.Id AND s.WarehouseId = w.Id);

    -- Record the starting stock as IN movements so the history matches the stock
    INSERT INTO StockMovements (Type, ProductId, ToWarehouseId, Quantity, Note, UserId)
    SELECT 'IN', ProductId, WarehouseId, Quantity, 'Opening stock', @AdminId
    FROM Stock
    WHERE Quantity > 0;
END
GO

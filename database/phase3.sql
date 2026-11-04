-- Inventory Management System - Phase 3: purchase orders
-- Run after database.sql and phase2.sql. Only creates new tables/columns and adds 2 demo POs.
-- It does not change or delete existing data and is safe to run more than once.

-- Needed for the unique index on the computed Number column (sqlcmd defaults to OFF)
SET QUOTED_IDENTIFIER ON;
GO

USE InventoryDb;
GO

-- ---------- Tables ----------

IF OBJECT_ID('PurchaseOrders') IS NULL
CREATE TABLE PurchaseOrders (
    Id          INT IDENTITY(1,1) PRIMARY KEY,
    -- PO-0001, PO-0002, ... built from Id, so two users can never get the same number
    Number      AS ('PO-' + CASE WHEN Id < 10000 THEN RIGHT('000' + CAST(Id AS VARCHAR(10)), 4)
                                 ELSE CAST(Id AS VARCHAR(10)) END) PERSISTED
                CONSTRAINT UQ_PurchaseOrders_Number UNIQUE,
    SupplierId  INT           NOT NULL CONSTRAINT FK_PurchaseOrders_Suppliers  REFERENCES Suppliers(Id),
    WarehouseId INT           NOT NULL CONSTRAINT FK_PurchaseOrders_Warehouses REFERENCES Warehouses(Id), -- where the goods arrive
    Status      NVARCHAR(10)  NOT NULL CONSTRAINT DF_PurchaseOrders_Status DEFAULT 'DRAFT'
                    CONSTRAINT CK_PurchaseOrders_Status
                    CHECK (Status IN ('DRAFT', 'ORDERED', 'PARTIAL', 'RECEIVED', 'CANCELLED')),
    Note        NVARCHAR(250) NULL,
    CreatedBy   INT           NOT NULL CONSTRAINT FK_PurchaseOrders_Users REFERENCES Users(Id),
    CreatedAt   DATETIME2     NOT NULL CONSTRAINT DF_PurchaseOrders_CreatedAt DEFAULT SYSDATETIME(),
    OrderedAt   DATETIME2     NULL,
    ReceivedAt  DATETIME2     NULL
);
GO

IF OBJECT_ID('PurchaseOrderItems') IS NULL
CREATE TABLE PurchaseOrderItems (
    Id               INT IDENTITY(1,1) PRIMARY KEY,
    PurchaseOrderId  INT           NOT NULL CONSTRAINT FK_PurchaseOrderItems_PurchaseOrders REFERENCES PurchaseOrders(Id),
    ProductId        INT           NOT NULL CONSTRAINT FK_PurchaseOrderItems_Products REFERENCES Products(Id),
    QuantityOrdered  INT           NOT NULL CONSTRAINT CK_PurchaseOrderItems_Ordered CHECK (QuantityOrdered > 0),
    QuantityReceived INT           NOT NULL CONSTRAINT DF_PurchaseOrderItems_Received DEFAULT 0
                         CONSTRAINT CK_PurchaseOrderItems_ReceivedMin CHECK (QuantityReceived >= 0),
    UnitCost         DECIMAL(18,2) NOT NULL CONSTRAINT CK_PurchaseOrderItems_UnitCost CHECK (UnitCost >= 0),
    CONSTRAINT UQ_PurchaseOrderItems_Product UNIQUE (PurchaseOrderId, ProductId),
    CONSTRAINT CK_PurchaseOrderItems_ReceivedMax CHECK (QuantityReceived <= QuantityOrdered)
);
GO

-- Movements created by receiving a PO point back to it
IF COL_LENGTH('StockMovements', 'PurchaseOrderId') IS NULL
    ALTER TABLE StockMovements ADD PurchaseOrderId INT NULL
        CONSTRAINT FK_StockMovements_PurchaseOrders REFERENCES PurchaseOrders(Id);
GO

-- ---------- Demo purchase orders (only if there are none yet) ----------

SET XACT_ABORT ON; -- any error rolls back the whole seed

DECLARE @AdminId INT = (SELECT TOP 1 Id FROM Users WHERE Role = 'Admin' ORDER BY Id);
DECLARE @Main    INT = (SELECT MIN(Id) FROM Warehouses WHERE Name = 'Main Warehouse');
DECLARE @North   INT = (SELECT MIN(Id) FROM Warehouses WHERE Name = 'North Depot');
DECLARE @Tech    INT = (SELECT MIN(Id) FROM Suppliers WHERE Name = 'TechSource Ltd');
DECLARE @Office  INT = (SELECT MIN(Id) FROM Suppliers WHERE Name = 'OfficePro Supplies');
DECLARE @Po      INT;

IF NOT EXISTS (SELECT 1 FROM PurchaseOrders)
   AND @AdminId IS NOT NULL AND @Main IS NOT NULL AND @North IS NOT NULL AND @Tech IS NOT NULL AND @Office IS NOT NULL
BEGIN
    BEGIN TRANSACTION;

    -- 1) A DRAFT order, not sent yet
    INSERT INTO PurchaseOrders (SupplierId, WarehouseId, Status, Note, CreatedBy)
    VALUES (@Tech, @Main, 'DRAFT', 'Restock electronics', @AdminId);
    SET @Po = SCOPE_IDENTITY();

    INSERT INTO PurchaseOrderItems (PurchaseOrderId, ProductId, QuantityOrdered, UnitCost)
    SELECT @Po, p.Id, v.Quantity, p.CostPrice
    FROM (VALUES ('ELEC-001', 50), ('ELEC-003', 10)) AS v (SKU, Quantity)
    JOIN Products p ON p.SKU = v.SKU;

    -- 2) A RECEIVED order, delivered to North Depot
    INSERT INTO PurchaseOrders (SupplierId, WarehouseId, Status, Note, CreatedBy, CreatedAt, OrderedAt, ReceivedAt)
    VALUES (@Office, @North, 'RECEIVED', 'Monthly office supplies', @AdminId,
            DATEADD(DAY, -3, SYSDATETIME()), DATEADD(DAY, -3, SYSDATETIME()), SYSDATETIME());
    SET @Po = SCOPE_IDENTITY();

    INSERT INTO PurchaseOrderItems (PurchaseOrderId, ProductId, QuantityOrdered, QuantityReceived, UnitCost)
    SELECT @Po, p.Id, v.Quantity, v.Quantity, p.CostPrice
    FROM (VALUES ('OFF-001', 100), ('CLN-001', 24)) AS v (SKU, Quantity)
    JOIN Products p ON p.SKU = v.SKU;

    -- Add the received goods to stock (update existing rows, create missing ones) ...
    UPDATE s SET Quantity = s.Quantity + i.QuantityReceived
    FROM Stock s
    JOIN PurchaseOrderItems i ON i.ProductId = s.ProductId
    WHERE i.PurchaseOrderId = @Po AND s.WarehouseId = @North;

    INSERT INTO Stock (ProductId, WarehouseId, Quantity)
    SELECT i.ProductId, @North, i.QuantityReceived
    FROM PurchaseOrderItems i
    WHERE i.PurchaseOrderId = @Po
      AND NOT EXISTS (SELECT 1 FROM Stock s WHERE s.ProductId = i.ProductId AND s.WarehouseId = @North);

    -- ... and record them as IN movements, like the API does
    INSERT INTO StockMovements (Type, ProductId, ToWarehouseId, Quantity, Note, UserId, PurchaseOrderId)
    SELECT 'IN', i.ProductId, @North, i.QuantityReceived, 'Received ' + po.Number, @AdminId, @Po
    FROM PurchaseOrderItems i
    JOIN PurchaseOrders po ON po.Id = i.PurchaseOrderId
    WHERE i.PurchaseOrderId = @Po;

    COMMIT;
END
GO

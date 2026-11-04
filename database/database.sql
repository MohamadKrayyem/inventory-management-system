-- Inventory Management System - Phase 1
-- Creates the database, the tables and demo data.
-- WARNING: running this again drops the tables and all their data
-- (including the Phase 2 and 3 tables; run phase2.sql and phase3.sql again afterwards).

IF DB_ID('InventoryDb') IS NULL
    CREATE DATABASE InventoryDb;
GO

USE InventoryDb;
GO

-- Later-phase tables first, because they reference the Phase 1 tables
DROP TABLE IF EXISTS StockMovements;
DROP TABLE IF EXISTS PurchaseOrderItems;
DROP TABLE IF EXISTS PurchaseOrders;
DROP TABLE IF EXISTS Stock;
DROP TABLE IF EXISTS Products;
DROP TABLE IF EXISTS Categories;
DROP TABLE IF EXISTS Suppliers;
DROP TABLE IF EXISTS Warehouses;
DROP TABLE IF EXISTS Users;
GO

-- ---------- Tables ----------

CREATE TABLE Users (
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    FullName     NVARCHAR(100) NOT NULL,
    Email        NVARCHAR(150) NOT NULL CONSTRAINT UQ_Users_Email UNIQUE,
    PasswordHash NVARCHAR(100) NOT NULL,
    Role         NVARCHAR(10)  NOT NULL CONSTRAINT CK_Users_Role CHECK (Role IN ('Admin', 'Staff')),
    CreatedAt    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE Categories (
    Id   INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL CONSTRAINT UQ_Categories_Name UNIQUE
);

CREATE TABLE Suppliers (
    Id      INT IDENTITY(1,1) PRIMARY KEY,
    Name    NVARCHAR(150) NOT NULL,
    Phone   NVARCHAR(30)  NULL,
    Email   NVARCHAR(150) NULL,
    Address NVARCHAR(250) NULL
);

CREATE TABLE Warehouses (
    Id       INT IDENTITY(1,1) PRIMARY KEY,
    Name     NVARCHAR(100) NOT NULL,
    Location NVARCHAR(250) NULL
);

CREATE TABLE Products (
    Id         INT IDENTITY(1,1) PRIMARY KEY,
    Name       NVARCHAR(150)  NOT NULL,
    SKU        NVARCHAR(50)   NOT NULL CONSTRAINT UQ_Products_SKU UNIQUE,
    CategoryId INT            NOT NULL CONSTRAINT FK_Products_Categories REFERENCES Categories(Id),
    CostPrice  DECIMAL(18,2)  NOT NULL DEFAULT 0,
    SellPrice  DECIMAL(18,2)  NOT NULL DEFAULT 0,
    MinStock   INT            NOT NULL DEFAULT 0,
    IsActive   BIT            NOT NULL DEFAULT 1,
    CreatedAt  DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
GO

-- ---------- Seed data ----------

-- Both users have the password: Demo@123 (BCrypt hash)
INSERT INTO Users (FullName, Email, PasswordHash, Role) VALUES
('Admin User', 'admin@demo.com', '$2a$11$UrifNx5bT3p6Skr721OiwuSFAC37xHHiiKmoXz97GXM94CguELpKq', 'Admin'),
('Staff User', 'staff@demo.com', '$2a$11$UrifNx5bT3p6Skr721OiwuSFAC37xHHiiKmoXz97GXM94CguELpKq', 'Staff');

INSERT INTO Categories (Name) VALUES
('Electronics'),
('Office Supplies'),
('Furniture'),
('Cleaning'),
('Packaging');

INSERT INTO Suppliers (Name, Phone, Email, Address) VALUES
('TechSource Ltd',      '+1 555 0101', 'sales@techsource.example',   '12 Market St, Springfield'),
('OfficePro Supplies',  '+1 555 0102', 'orders@officepro.example',   '88 Commerce Ave, Riverside'),
('HomeBase Furniture',  '+1 555 0103', 'contact@homebase.example',   '5 Industrial Rd, Lakeside'),
('CleanCo Distribution','+1 555 0104', 'hello@cleanco.example',      '41 Harbor Blvd, Bayview');

INSERT INTO Warehouses (Name, Location) VALUES
('Main Warehouse',  'Springfield'),
('North Depot',     'Riverside'),
('South Storage',   'Lakeside');

INSERT INTO Products (Name, SKU, CategoryId, CostPrice, SellPrice, MinStock, IsActive) VALUES
('Wireless Mouse',          'ELEC-001', 1,  8.50,  15.99, 20, 1),
('USB-C Charger 65W',       'ELEC-002', 1, 18.00,  34.99, 15, 1),
('27" LED Monitor',         'ELEC-003', 1, 120.00, 189.00, 5, 1),
('A4 Copy Paper (500)',     'OFF-001',  2,  3.20,   5.99, 50, 1),
('Ballpoint Pens (12 pack)','OFF-002',  2,  1.80,   3.49, 40, 1),
('Stapler',                 'OFF-003',  2,  4.00,   7.99, 10, 1),
('Office Chair',            'FURN-001', 3, 65.00, 119.00,  4, 1),
('Standing Desk',           'FURN-002', 3, 180.00, 299.00, 2, 0),
('Glass Cleaner 1L',        'CLN-001',  4,  1.50,   3.99, 30, 1),
('Shipping Box Medium',     'PKG-001',  5,  0.60,   1.29, 100, 1);
GO

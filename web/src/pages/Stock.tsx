import { useEffect, useState, type ReactNode } from "react";
import api from "../api";
import Table, { usePagedList, type Column } from "../components/Table";

type StockRow = {
  id: number;
  productName: string;
  sku: string;
  warehouseName: string | null;
  quantity: number;
  totalQuantity: number;
  minStock: number;
  isLow: boolean;
};

type Warehouse = { id: number; name: string };

// Low rows are shown in red
const cell = (row: StockRow, value: ReactNode) => <span className={row.isLow ? "low-text" : undefined}>{value}</span>;

const columns: Column<StockRow>[] = [
  { label: "Product", render: (r) => cell(r, r.productName) },
  { label: "SKU", render: (r) => cell(r, r.sku) },
  { label: "Warehouse", render: (r) => cell(r, r.warehouseName ?? "No stock") },
  { label: "Quantity", render: (r) => cell(r, r.quantity) },
  { label: "Total (all)", render: (r) => cell(r, r.totalQuantity) },
  { label: "Min Stock", render: (r) => cell(r, r.minStock) },
  {
    label: "Status",
    render: (r) =>
      r.isLow ? (
        <span className="badge badge-low">Low</span>
      ) : (
        <span className="badge green">OK</span>
      ),
  },
];

export default function Stock() {
  const [warehouses, setWarehouses] = useState<Warehouse[]>([]);
  const [warehouseId, setWarehouseId] = useState("");
  const [lowOnly, setLowOnly] = useState(false);

  // Filters go in the URL; usePagedList adds search and paging
  const filters = new URLSearchParams();
  if (warehouseId) filters.set("warehouseId", warehouseId);
  if (lowOnly) filters.set("lowOnly", "true");
  const list = usePagedList<StockRow>(`/stock?${filters}`);

  useEffect(() => {
    api
      .get<{ items: Warehouse[] }>("/warehouses", { params: { pageSize: 500 } })
      .then((r) => setWarehouses(r.data.items))
      .catch(() => {}); // the table already shows API errors
  }, []);

  return (
    <>
      <h1>Stock</h1>

      <Table
        list={list}
        columns={columns}
        toolbar={
          <div className="filters">
            <select
              value={warehouseId}
              onChange={(e) => {
                setWarehouseId(e.target.value);
                list.setPage(1);
              }}
            >
              <option value="">All warehouses</option>
              {warehouses.map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name}
                </option>
              ))}
            </select>
            <label className="checkbox">
              <input
                type="checkbox"
                checked={lowOnly}
                onChange={(e) => {
                  setLowOnly(e.target.checked);
                  list.setPage(1);
                }}
              />
              Low stock only
            </label>
          </div>
        }
      />
    </>
  );
}

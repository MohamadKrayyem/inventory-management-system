import { useEffect, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router";
import api, { errorMessage } from "../api";
import Table, { usePagedList, type Column } from "../components/Table";
import Modal from "../components/Modal";

// ---------- Shared with PurchaseOrder.tsx ----------

export type PoStatus = "DRAFT" | "ORDERED" | "PARTIAL" | "RECEIVED" | "CANCELLED";

export type PurchaseOrderRow = {
  id: number;
  number: string;
  supplierId: number;
  supplierName: string;
  warehouseId: number;
  warehouseName: string;
  status: PoStatus;
  note: string | null;
  totalCost: number;
  itemCount: number;
  createdByName: string;
  createdAt: string;
  orderedAt: string | null;
  receivedAt: string | null;
};

export type PoItem = {
  id: number;
  productId: number;
  productName: string;
  sku: string;
  quantityOrdered: number;
  quantityReceived: number;
  remaining: number;
  unitCost: number;
  lineTotal: number;
};

export type PurchaseOrderDetail = PurchaseOrderRow & { items: PoItem[] };

export const statusBadge: Record<PoStatus, string> = {
  DRAFT: "badge gray",
  ORDERED: "badge",
  PARTIAL: "badge amber",
  RECEIVED: "badge green",
  CANCELLED: "badge red",
};

export const money = (n: number) => n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });

type Option = { id: number; name: string };
type Product = Option & { costPrice: number };
type ItemForm = { productId: string; quantity: string; unitCost: string };

const emptyItem: ItemForm = { productId: "", quantity: "1", unitCost: "" };

// Create / edit modal. Pass `po` to edit an existing DRAFT.
export function PurchaseOrderForm({
  po,
  onClose,
  onSaved,
}: {
  po?: PurchaseOrderDetail;
  onClose: () => void;
  onSaved: (id: number) => void;
}) {
  const [suppliers, setSuppliers] = useState<Option[]>([]);
  const [warehouses, setWarehouses] = useState<Option[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [supplierId, setSupplierId] = useState(po ? String(po.supplierId) : "");
  const [warehouseId, setWarehouseId] = useState(po ? String(po.warehouseId) : "");
  const [note, setNote] = useState(po?.note ?? "");
  const [items, setItems] = useState<ItemForm[]>(
    po
      ? po.items.map((i) => ({ productId: String(i.productId), quantity: String(i.quantityOrdered), unitCost: String(i.unitCost) }))
      : [emptyItem]
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    const params = { params: { pageSize: 500 } };
    Promise.all([
      api.get<{ items: Option[] }>("/suppliers", params),
      api.get<{ items: Option[] }>("/warehouses", params),
      api.get<{ items: Product[] }>("/products", params),
    ])
      .then(([s, w, p]) => {
        setSuppliers(s.data.items);
        setWarehouses(w.data.items);
        setProducts(p.data.items);
      })
      .catch((err) => setError(errorMessage(err)));
  }, []);

  function setItem(index: number, changes: Partial<ItemForm>) {
    setItems((list) => list.map((item, i) => (i === index ? { ...item, ...changes } : item)));
  }

  // Picking a product fills in its cost price
  function pickProduct(index: number, productId: string) {
    const product = products.find((p) => String(p.id) === productId);
    setItem(index, { productId, unitCost: product ? String(product.costPrice) : "" });
  }

  const total = items.reduce((sum, i) => sum + Number(i.quantity) * Number(i.unitCost), 0);

  async function save(e: FormEvent) {
    e.preventDefault();
    setSaving(true);
    setError("");
    const body = {
      supplierId: Number(supplierId),
      warehouseId: Number(warehouseId),
      note: note || null,
      items: items.map((i) => ({ productId: Number(i.productId), quantity: Number(i.quantity), unitCost: Number(i.unitCost) })),
    };
    try {
      const { data } = po
        ? await api.put<PurchaseOrderDetail>(`/purchase-orders/${po.id}`, body)
        : await api.post<PurchaseOrderDetail>("/purchase-orders", body);
      onSaved(data.id);
    } catch (err) {
      setError(errorMessage(err));
      setSaving(false);
    }
  }

  return (
    <Modal title={po ? `Edit ${po.number}` : "New Purchase Order"} onClose={onClose}>
      <form onSubmit={save}>
        {error && <div className="alert">{error}</div>}

        <div className="form-grid">
          <label>
            Supplier
            <select value={supplierId} onChange={(e) => setSupplierId(e.target.value)} required>
              <option value="">Select a supplier</option>
              {suppliers.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Deliver to warehouse
            <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} required>
              <option value="">Select a warehouse</option>
              {warehouses.map((w) => (
                <option key={w.id} value={w.id}>
                  {w.name}
                </option>
              ))}
            </select>
          </label>
        </div>

        <label>
          Note
          <input value={note} onChange={(e) => setNote(e.target.value)} maxLength={250} />
        </label>

        <div className="item-row item-head">
          <span>Product</span>
          <span>Qty</span>
          <span>Unit cost</span>
          <span />
        </div>
        {items.map((item, i) => (
          <div className="item-row" key={i}>
            <select aria-label="Product" value={item.productId} onChange={(e) => pickProduct(i, e.target.value)} required>
              <option value="">Select a product</option>
              {products.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
            <input
              aria-label="Quantity"
              type="number"
              min={1}
              step={1}
              value={item.quantity}
              onChange={(e) => setItem(i, { quantity: e.target.value })}
              required
            />
            <input
              aria-label="Unit cost"
              type="number"
              min={0}
              step="0.01"
              value={item.unitCost}
              onChange={(e) => setItem(i, { unitCost: e.target.value })}
              required
            />
            <button
              type="button"
              className="btn small danger"
              onClick={() => setItems(items.filter((_, j) => j !== i))}
              disabled={items.length === 1}
              aria-label="Remove item"
            >
              ×
            </button>
          </div>
        ))}

        <div className="po-total">
          <button type="button" className="btn small" onClick={() => setItems([...items, emptyItem])}>
            + Add item
          </button>
          <span>Total: {money(total)}</span>
        </div>

        <div className="modal-actions">
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn primary" disabled={saving}>
            {saving ? "Saving..." : "Save"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

// ---------- List page ----------

const columns: Column<PurchaseOrderRow>[] = [
  {
    label: "Number",
    render: (po) => (
      <Link className="link" to={`/purchase-orders/${po.id}`}>
        {po.number}
      </Link>
    ),
  },
  { label: "Supplier", render: (po) => po.supplierName },
  { label: "Warehouse", render: (po) => po.warehouseName },
  { label: "Status", render: (po) => <span className={statusBadge[po.status]}>{po.status}</span> },
  { label: "Items", render: (po) => po.itemCount },
  { label: "Total Cost", render: (po) => money(po.totalCost) },
  { label: "Created", render: (po) => new Date(po.createdAt).toLocaleDateString() },
];

export default function PurchaseOrders() {
  const navigate = useNavigate();
  const [status, setStatus] = useState("");
  const [showForm, setShowForm] = useState(false);

  // Filters go in the URL; usePagedList adds search and paging
  const list = usePagedList<PurchaseOrderRow>(`/purchase-orders?${status ? new URLSearchParams({ status }) : ""}`);

  return (
    <>
      <h1>Purchase Orders</h1>

      <Table
        list={list}
        columns={columns}
        actions={(po) => (
          <Link className="btn small" to={`/purchase-orders/${po.id}`}>
            Open
          </Link>
        )}
        toolbar={
          <div className="filters">
            <select
              value={status}
              onChange={(e) => {
                setStatus(e.target.value);
                list.setPage(1);
              }}
            >
              <option value="">All statuses</option>
              <option value="OPEN">Open (Ordered + Partial)</option>
              {(Object.keys(statusBadge) as PoStatus[]).map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
            <button className="btn primary" onClick={() => setShowForm(true)}>
              + New PO
            </button>
          </div>
        }
      />

      {showForm && (
        <PurchaseOrderForm onClose={() => setShowForm(false)} onSaved={(id) => navigate(`/purchase-orders/${id}`)} />
      )}
    </>
  );
}

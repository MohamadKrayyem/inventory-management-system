import { useEffect, useState, type FormEvent } from "react";
import { Link } from "react-router";
import api, { errorMessage } from "../api";
import { useAuth } from "../auth";
import Table, { usePagedList, type Column } from "../components/Table";
import Modal from "../components/Modal";

type MovementType = "IN" | "OUT" | "TRANSFER" | "ADJUST";

type Movement = {
  id: number;
  type: MovementType;
  productName: string;
  sku: string;
  fromWarehouseName: string | null;
  toWarehouseName: string | null;
  quantity: number;
  note: string | null;
  userName: string;
  createdAt: string;
  purchaseOrderId: number | null;
  purchaseOrderNumber: string | null;
};

type Option = { id: number; name: string };

type Form = {
  type: MovementType;
  productId: string;
  fromWarehouseId: string;
  toWarehouseId: string;
  quantity: string;
  note: string;
};

const emptyForm: Form = { type: "IN", productId: "", fromWarehouseId: "", toWarehouseId: "", quantity: "", note: "" };

const typeLabels: Record<MovementType, string> = {
  IN: "IN (receive)",
  OUT: "OUT (remove)",
  TRANSFER: "TRANSFER (move)",
  ADJUST: "ADJUST (stock count)",
};

const badgeClass: Record<MovementType, string> = {
  IN: "badge green",
  OUT: "badge gray",
  TRANSFER: "badge",
  ADJUST: "badge gray",
};

const columns: Column<Movement>[] = [
  { label: "Date", render: (m) => new Date(m.createdAt).toLocaleString() },
  { label: "Type", render: (m) => <span className={badgeClass[m.type]}>{m.type}</span> },
  { label: "Product", render: (m) => `${m.productName} (${m.sku})` },
  { label: "From", render: (m) => m.fromWarehouseName ?? "-" },
  { label: "To", render: (m) => m.toWarehouseName ?? "-" },
  { label: "Qty", render: (m) => m.quantity },
  { label: "Note", render: (m) => m.note ?? "" },
  {
    label: "PO",
    render: (m) =>
      m.purchaseOrderId && (
        <Link className="link" to={`/purchase-orders/${m.purchaseOrderId}`}>
          {m.purchaseOrderNumber}
        </Link>
      ),
  },
  { label: "User", render: (m) => m.userName },
];

export default function Movements() {
  const { user } = useAuth();
  const [warehouses, setWarehouses] = useState<Option[]>([]);
  const [products, setProducts] = useState<Option[]>([]);
  const [type, setType] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [form, setForm] = useState<Form | null>(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState("");

  // Filters go in the URL; usePagedList adds search and paging
  const filters = new URLSearchParams();
  if (type) filters.set("type", type);
  if (warehouseId) filters.set("warehouseId", warehouseId);
  const list = usePagedList<Movement>(`/movements?${filters}`);

  useEffect(() => {
    api
      .get<{ items: Option[] }>("/warehouses", { params: { pageSize: 500 } })
      .then((r) => setWarehouses(r.data.items))
      .catch(() => {}); // the table already shows API errors
  }, []);

  // Only Admin can adjust stock
  const types = (Object.keys(typeLabels) as MovementType[]).filter((t) => t !== "ADJUST" || user?.role === "Admin");

  function openForm() {
    setFormError("");
    setForm(emptyForm);
    api
      .get<{ items: Option[] }>("/products", { params: { pageSize: 500 } })
      .then((r) => setProducts(r.data.items))
      .catch((err) => setFormError(errorMessage(err)));
  }

  function set(key: keyof Form, value: string) {
    setForm((f) => f && { ...f, [key]: value });
  }

  const needsFrom = form?.type === "OUT" || form?.type === "TRANSFER";
  const needsTo = form?.type === "IN" || form?.type === "TRANSFER" || form?.type === "ADJUST";

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!form) return;
    setSaving(true);
    setFormError("");
    try {
      await api.post("/movements", {
        type: form.type,
        productId: Number(form.productId),
        fromWarehouseId: needsFrom ? Number(form.fromWarehouseId) : null,
        toWarehouseId: needsTo ? Number(form.toWarehouseId) : null,
        quantity: Number(form.quantity),
        note: form.note || null,
      });
      setForm(null);
      list.reload();
    } catch (err) {
      setFormError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  const warehouseSelect = (key: "fromWarehouseId" | "toWarehouseId", label: string) => (
    <label>
      {label}
      <select value={form![key]} onChange={(e) => set(key, e.target.value)} required>
        <option value="">Select a warehouse</option>
        {warehouses.map((w) => (
          <option key={w.id} value={w.id}>
            {w.name}
          </option>
        ))}
      </select>
    </label>
  );

  return (
    <>
      <h1>Movements</h1>

      <Table
        list={list}
        columns={columns}
        toolbar={
          <div className="filters">
            <select
              value={type}
              onChange={(e) => {
                setType(e.target.value);
                list.setPage(1);
              }}
            >
              <option value="">All types</option>
              {(Object.keys(typeLabels) as MovementType[]).map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
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
            <button className="btn primary" onClick={openForm}>
              + New Movement
            </button>
          </div>
        }
      />

      {form && (
        <Modal title="New Movement" onClose={() => setForm(null)}>
          <form onSubmit={save}>
            {formError && <div className="alert">{formError}</div>}

            <label>
              Type
              <select value={form.type} onChange={(e) => set("type", e.target.value)}>
                {types.map((t) => (
                  <option key={t} value={t}>
                    {typeLabels[t]}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Product
              <select value={form.productId} onChange={(e) => set("productId", e.target.value)} required>
                <option value="">Select a product</option>
                {products.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name}
                  </option>
                ))}
              </select>
            </label>

            {needsFrom && warehouseSelect("fromWarehouseId", "From warehouse")}
            {needsTo && warehouseSelect("toWarehouseId", form.type === "ADJUST" ? "Warehouse" : "To warehouse")}

            <label>
              {form.type === "ADJUST" ? "Counted quantity" : "Quantity"}
              <input
                type="number"
                min={form.type === "ADJUST" ? 0 : 1}
                step={1}
                value={form.quantity}
                onChange={(e) => set("quantity", e.target.value)}
                required
              />
            </label>

            <label>
              Note
              <input value={form.note} onChange={(e) => set("note", e.target.value)} maxLength={200} />
            </label>

            <div className="modal-actions">
              <button type="button" className="btn" onClick={() => setForm(null)}>
                Cancel
              </button>
              <button className="btn primary" disabled={saving}>
                {saving ? "Saving..." : "Save"}
              </button>
            </div>
          </form>
        </Modal>
      )}
    </>
  );
}

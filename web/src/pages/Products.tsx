import { useState, type FormEvent } from "react";
import api, { errorMessage } from "../api";
import { useAuth } from "../auth";
import Table, { usePagedList, type Column } from "../components/Table";
import Modal from "../components/Modal";

type Product = {
  id: number;
  name: string;
  sku: string;
  categoryId: number;
  categoryName?: string;
  costPrice: number;
  sellPrice: number;
  minStock: number;
  isActive: boolean;
};

type Category = { id: number; name: string };

const emptyProduct: Product = {
  id: 0,
  name: "",
  sku: "",
  categoryId: 0,
  costPrice: 0,
  sellPrice: 0,
  minStock: 0,
  isActive: true,
};

const columns: Column<Product>[] = [
  { label: "Name", render: (p) => p.name },
  { label: "SKU", render: (p) => p.sku },
  { label: "Category", render: (p) => p.categoryName },
  { label: "Cost", render: (p) => p.costPrice.toFixed(2) },
  { label: "Price", render: (p) => p.sellPrice.toFixed(2) },
  { label: "Min Stock", render: (p) => p.minStock },
  {
    label: "Status",
    render: (p) => <span className={`badge ${p.isActive ? "green" : "gray"}`}>{p.isActive ? "Active" : "Inactive"}</span>,
  },
];

export default function Products() {
  const { user } = useAuth();
  const list = usePagedList<Product>("/products");
  const [categories, setCategories] = useState<Category[]>([]);
  const [form, setForm] = useState<Product | null>(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState("");

  function openForm(product?: Product) {
    setFormError("");
    setForm(product ?? emptyProduct);
    // Load categories fresh each time so new ones show up in the dropdown
    api
      .get<{ items: Category[] }>("/categories", { params: { pageSize: 500 } })
      .then((r) => setCategories(r.data.items))
      .catch((err) => setFormError(errorMessage(err)));
  }

  function set<K extends keyof Product>(key: K, value: Product[K]) {
    setForm((f) => f && { ...f, [key]: value });
  }

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!form) return;
    setSaving(true);
    setFormError("");
    try {
      if (form.id) await api.put(`/products/${form.id}`, form);
      else await api.post("/products", form);
      setForm(null);
      list.reload();
    } catch (err) {
      setFormError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function remove(product: Product) {
    if (!confirm(`Delete product "${product.name}"?`)) return;
    try {
      await api.delete(`/products/${product.id}`);
      list.reload();
    } catch (err) {
      alert(errorMessage(err));
    }
  }

  return (
    <>
      <h1>Products</h1>

      <Table
        list={list}
        columns={columns}
        toolbar={
          <button className="btn primary" onClick={() => openForm()}>
            + Add Product
          </button>
        }
        actions={(p) => (
          <>
            <button className="btn small" onClick={() => openForm(p)}>
              Edit
            </button>
            {user?.role === "Admin" && (
              <button className="btn small danger" onClick={() => remove(p)}>
                Delete
              </button>
            )}
          </>
        )}
      />

      {form && (
        <Modal title={form.id ? "Edit Product" : "Add Product"} onClose={() => setForm(null)}>
          <form onSubmit={save}>
            {formError && <div className="alert">{formError}</div>}

            <div className="form-grid">
              <label>
                Name
                <input value={form.name} onChange={(e) => set("name", e.target.value)} required maxLength={150} />
              </label>
              <label>
                SKU
                <input value={form.sku} onChange={(e) => set("sku", e.target.value)} required maxLength={50} />
              </label>
              <label>
                Category
                <select
                  value={form.categoryId || ""}
                  onChange={(e) => set("categoryId", Number(e.target.value))}
                  required
                >
                  <option value="">Select a category</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Min Stock
                <input
                  type="number"
                  min={0}
                  value={form.minStock}
                  onChange={(e) => set("minStock", Number(e.target.value))}
                />
              </label>
              <label>
                Cost Price
                <input
                  type="number"
                  min={0}
                  step="0.01"
                  value={form.costPrice}
                  onChange={(e) => set("costPrice", Number(e.target.value))}
                />
              </label>
              <label>
                Sell Price
                <input
                  type="number"
                  min={0}
                  step="0.01"
                  value={form.sellPrice}
                  onChange={(e) => set("sellPrice", Number(e.target.value))}
                />
              </label>
            </div>

            <label className="checkbox">
              <input type="checkbox" checked={form.isActive} onChange={(e) => set("isActive", e.target.checked)} />
              Active
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

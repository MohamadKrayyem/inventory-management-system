import { useState, type FormEvent } from "react";
import api, { errorMessage } from "../api";
import { useAuth } from "../auth";
import Table, { usePagedList } from "../components/Table";
import Modal from "../components/Modal";

export type Field = { name: string; label: string };

type Row = { id: number; [key: string]: unknown };

type Props = { title: string; path: string; fields: Field[] };

// One page for simple tables (Categories, Suppliers, Warehouses).
// The table columns and the form inputs are built from `fields`.
// The first field (Name) is required.
export default function CrudPage({ title, path, fields }: Props) {
  const { user } = useAuth();
  const list = usePagedList<Row>(path);
  const [form, setForm] = useState<Row | null>(null);
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState("");

  const columns = fields.map((f) => ({ label: f.label, render: (row: Row) => String(row[f.name] ?? "") }));

  function openForm(row?: Row) {
    setFormError("");
    setForm(row ?? { id: 0, ...Object.fromEntries(fields.map((f) => [f.name, ""])) });
  }

  async function save(e: FormEvent) {
    e.preventDefault();
    if (!form) return;
    setSaving(true);
    setFormError("");
    // Send empty optional fields as null
    const body = Object.fromEntries(Object.entries(form).map(([k, v]) => [k, v === "" ? null : v]));
    try {
      if (form.id) await api.put(`${path}/${form.id}`, body);
      else await api.post(path, body);
      setForm(null);
      list.reload();
    } catch (err) {
      setFormError(errorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function remove(row: Row) {
    if (!confirm(`Delete "${row[fields[0].name]}"?`)) return;
    try {
      await api.delete(`${path}/${row.id}`);
      list.reload();
    } catch (err) {
      alert(errorMessage(err));
    }
  }

  return (
    <>
      <h1>{title}</h1>

      <Table
        list={list}
        columns={columns}
        toolbar={
          <button className="btn primary" onClick={() => openForm()}>
            + Add
          </button>
        }
        actions={(row) => (
          <>
            <button className="btn small" onClick={() => openForm(row)}>
              Edit
            </button>
            {user?.role === "Admin" && (
              <button className="btn small danger" onClick={() => remove(row)}>
                Delete
              </button>
            )}
          </>
        )}
      />

      {form && (
        <Modal title={`${form.id ? "Edit" : "Add"} ${title}`} onClose={() => setForm(null)}>
          <form onSubmit={save}>
            {formError && <div className="alert">{formError}</div>}

            {fields.map((f, i) => (
              <label key={f.name}>
                {f.label}
                <input
                  type={f.name === "email" ? "email" : "text"}
                  value={String(form[f.name] ?? "")}
                  onChange={(e) => setForm({ ...form, [f.name]: e.target.value })}
                  required={i === 0}
                />
              </label>
            ))}

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

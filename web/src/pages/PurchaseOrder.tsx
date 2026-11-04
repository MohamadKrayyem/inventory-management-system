import { useEffect, useState, type FormEvent } from "react";
import { Link, useParams } from "react-router";
import api, { errorMessage } from "../api";
import { useAuth } from "../auth";
import Modal from "../components/Modal";
import { PurchaseOrderForm, money, statusBadge, type PurchaseOrderDetail } from "./PurchaseOrders";

const date = (value: string | null) => (value ? new Date(value).toLocaleString() : "-");

export default function PurchaseOrder() {
  const { id } = useParams();
  const { user } = useAuth();
  const isAdmin = user?.role === "Admin";
  const [po, setPo] = useState<PurchaseOrderDetail | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState(false);
  const [receiving, setReceiving] = useState<Record<number, string> | null>(null); // itemId -> qty
  const [receiveError, setReceiveError] = useState("");

  function load() {
    api
      .get<PurchaseOrderDetail>(`/purchase-orders/${id}`)
      .then((r) => setPo(r.data))
      .catch((err) => setError(errorMessage(err)));
  }

  useEffect(load, [id]);

  // Mark as Ordered / Cancel
  async function action(name: "order" | "cancel", question: string) {
    if (!confirm(question)) return;
    setBusy(true);
    setError("");
    try {
      const { data } = await api.post<PurchaseOrderDetail>(`/purchase-orders/${id}/${name}`);
      setPo(data);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  function openReceive() {
    if (!po) return;
    setReceiveError("");
    // Prefill every open line with what is still remaining
    setReceiving(Object.fromEntries(po.items.filter((i) => i.remaining > 0).map((i) => [i.id, String(i.remaining)])));
  }

  async function receive(e: FormEvent) {
    e.preventDefault();
    if (!receiving) return;
    const lines = Object.entries(receiving)
      .map(([itemId, qty]) => ({ itemId: Number(itemId), quantity: Number(qty) }))
      .filter((l) => l.quantity > 0);
    if (lines.length === 0) return setReceiveError("Enter a quantity to receive.");

    setBusy(true);
    setReceiveError("");
    try {
      const { data } = await api.post<PurchaseOrderDetail>(`/purchase-orders/${id}/receive`, lines);
      setPo(data);
      setReceiving(null);
    } catch (err) {
      setReceiveError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  if (!po) return error ? <div className="alert">{error}</div> : <p className="muted">Loading...</p>;

  const canReceive = po.status === "ORDERED" || po.status === "PARTIAL";
  const canCancel = isAdmin && (po.status === "DRAFT" || po.status === "ORDERED");

  return (
    <>
      <Link className="back" to="/purchase-orders">
        ← Purchase Orders
      </Link>

      <div className="page-header">
        <h1>
          {po.number} <span className={statusBadge[po.status]}>{po.status}</span>
        </h1>
        <div className="button-row">
          {po.status === "DRAFT" && (
            <button className="btn" onClick={() => setEditing(true)} disabled={busy}>
              Edit
            </button>
          )}
          {isAdmin && po.status === "DRAFT" && (
            <button className="btn primary" onClick={() => action("order", `Mark ${po.number} as ordered?`)} disabled={busy}>
              Mark as Ordered
            </button>
          )}
          {canReceive && (
            <button className="btn primary" onClick={openReceive} disabled={busy}>
              Receive
            </button>
          )}
          {canCancel && (
            <button className="btn danger" onClick={() => action("cancel", `Cancel ${po.number}?`)} disabled={busy}>
              Cancel PO
            </button>
          )}
        </div>
      </div>

      {error && <div className="alert">{error}</div>}

      <div className="card details">
        <div>
          <span className="muted small">Supplier</span>
          {po.supplierName}
        </div>
        <div>
          <span className="muted small">Deliver to</span>
          {po.warehouseName}
        </div>
        <div>
          <span className="muted small">Created</span>
          {date(po.createdAt)} by {po.createdByName}
        </div>
        <div>
          <span className="muted small">Ordered</span>
          {date(po.orderedAt)}
        </div>
        <div>
          <span className="muted small">Received</span>
          {date(po.receivedAt)}
        </div>
        <div>
          <span className="muted small">Note</span>
          {po.note || "-"}
        </div>
      </div>

      <div className="card">
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Product</th>
                <th>Ordered</th>
                <th>Received</th>
                <th>Remaining</th>
                <th>Unit Cost</th>
                <th>Line Total</th>
              </tr>
            </thead>
            <tbody>
              {po.items.map((i) => (
                <tr key={i.id}>
                  <td>
                    {i.productName} <span className="muted">({i.sku})</span>
                  </td>
                  <td>{i.quantityOrdered}</td>
                  <td>{i.quantityReceived}</td>
                  <td>{i.remaining}</td>
                  <td>{money(i.unitCost)}</td>
                  <td>{money(i.lineTotal)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={5}>Total</td>
                <td>{money(po.totalCost)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </div>

      {editing && (
        <PurchaseOrderForm
          po={po}
          onClose={() => setEditing(false)}
          onSaved={() => {
            setEditing(false);
            load();
          }}
        />
      )}

      {receiving && (
        <Modal title={`Receive ${po.number}`} onClose={() => setReceiving(null)}>
          <form onSubmit={receive}>
            {receiveError && <div className="alert">{receiveError}</div>}
            <p className="muted">Goods go into {po.warehouseName}.</p>

            {po.items
              .filter((i) => i.remaining > 0)
              .map((i) => (
                <label key={i.id}>
                  {i.productName} (remaining {i.remaining})
                  <input
                    type="number"
                    min={0}
                    max={i.remaining}
                    step={1}
                    value={receiving[i.id] ?? ""}
                    onChange={(e) => setReceiving({ ...receiving, [i.id]: e.target.value })}
                  />
                </label>
              ))}

            <div className="modal-actions">
              <button type="button" className="btn" onClick={() => setReceiving(null)}>
                Cancel
              </button>
              <button className="btn primary" disabled={busy}>
                {busy ? "Saving..." : "Receive"}
              </button>
            </div>
          </form>
        </Modal>
      )}
    </>
  );
}

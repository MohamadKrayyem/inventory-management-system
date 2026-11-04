import { useEffect, useState } from "react";
import { Link } from "react-router";
import api, { errorMessage } from "../api";

const cards = [
  { label: "Products", path: "/products" },
  { label: "Categories", path: "/categories" },
  { label: "Suppliers", path: "/suppliers" },
  { label: "Warehouses", path: "/warehouses" },
];

type Summary = { totalStockValue: number; lowStockCount: number; movementsToday: number };

export default function Dashboard() {
  const [counts, setCounts] = useState<number[] | null>(null);
  const [summary, setSummary] = useState<Summary | null>(null);
  const [openPos, setOpenPos] = useState<number>();
  const [error, setError] = useState("");

  // Each list endpoint returns { total }, so ask for 1 row and read the count
  useEffect(() => {
    Promise.all(cards.map((c) => api.get<{ total: number }>(c.path, { params: { pageSize: 1 } })))
      .then((res) => setCounts(res.map((r) => r.data.total)))
      .catch((err) => setError(errorMessage(err)));
    api
      .get<Summary>("/stock/summary")
      .then((r) => setSummary(r.data))
      .catch((err) => setError(errorMessage(err)));
    // OPEN = ORDERED + PARTIAL
    api
      .get<{ total: number }>("/purchase-orders", { params: { status: "OPEN", pageSize: 1 } })
      .then((r) => setOpenPos(r.data.total))
      .catch((err) => setError(errorMessage(err)));
  }, []);

  const stats = [
    ...cards.map((c, i) => ({ ...c, value: counts?.[i] })),
    {
      label: "Total Stock Value",
      path: "/stock",
      value: summary?.totalStockValue.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }),
    },
    { label: "Low Stock Items", path: "/stock", value: summary?.lowStockCount },
    { label: "Movements Today", path: "/movements", value: summary?.movementsToday },
    { label: "Open POs", path: "/purchase-orders", value: openPos },
  ];

  return (
    <>
      <h1>Dashboard</h1>
      {error && <div className="alert">{error}</div>}
      <div className="cards">
        {stats.map((s) => (
          <Link key={s.label} to={s.path} className="card stat">
            <span className="muted">{s.label}</span>
            <strong>{s.value ?? "..."}</strong>
          </Link>
        ))}
      </div>
    </>
  );
}

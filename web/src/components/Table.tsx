import { useEffect, useState, type ReactNode } from "react";
import api, { errorMessage } from "../api";

export type Column<T> = { label: string; render: (row: T) => ReactNode };

// Loads one page from a GET list endpoint that returns { items, total }
export function usePagedList<T>(path: string, pageSize = 10) {
  const [items, setItems] = useState<T[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError("");
    // Small delay so typing in the search box doesn't send a request per key
    const timer = setTimeout(() => {
      api
        .get<{ items: T[]; total: number }>(path, { params: { search, page, pageSize } })
        .then((r) => {
          if (cancelled) return;
          setItems(r.data.items);
          setTotal(r.data.total);
          if (r.data.items.length === 0 && page > 1) setPage(page - 1); // e.g. last row on a page was deleted
        })
        .catch((err) => !cancelled && setError(errorMessage(err)))
        .finally(() => !cancelled && setLoading(false));
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [path, search, page, pageSize, reloadKey]);

  return {
    items,
    total,
    page,
    pageSize,
    search,
    loading,
    error,
    setPage,
    setSearch: (value: string) => {
      setSearch(value);
      setPage(1);
    },
    reload: () => setReloadKey((k) => k + 1),
  };
}

export type PagedList<T> = ReturnType<typeof usePagedList<T>>;

type Props<T> = {
  list: PagedList<T>;
  columns: Column<T>[];
  actions?: (row: T) => ReactNode;
  toolbar?: ReactNode;
};

export default function Table<T extends { id: number }>({ list, columns, actions, toolbar }: Props<T>) {
  const pages = Math.max(1, Math.ceil(list.total / list.pageSize));
  const colCount = columns.length + (actions ? 1 : 0);

  return (
    <div className="card">
      <div className="toolbar">
        <input
          className="search"
          placeholder="Search..."
          value={list.search}
          onChange={(e) => list.setSearch(e.target.value)}
        />
        {toolbar}
      </div>

      {list.error && <div className="alert">{list.error}</div>}

      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              {columns.map((c) => (
                <th key={c.label}>{c.label}</th>
              ))}
              {actions && <th className="actions">Actions</th>}
            </tr>
          </thead>
          <tbody>
            {list.items.length === 0 ? (
              <tr>
                <td colSpan={colCount} className="empty">
                  {list.loading ? "Loading..." : "No records found."}
                </td>
              </tr>
            ) : (
              list.items.map((row) => (
                <tr key={row.id}>
                  {columns.map((c) => (
                    <td key={c.label}>{c.render(row)}</td>
                  ))}
                  {actions && <td className="actions">{actions(row)}</td>}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      <div className="pagination">
        <span className="muted">
          {list.loading ? "Loading..." : `${list.total} record${list.total === 1 ? "" : "s"}`}
        </span>
        <button className="btn" disabled={list.page <= 1} onClick={() => list.setPage(list.page - 1)}>
          Previous
        </button>
        <span>
          Page {list.page} of {pages}
        </span>
        <button className="btn" disabled={list.page >= pages} onClick={() => list.setPage(list.page + 1)}>
          Next
        </button>
      </div>
    </div>
  );
}

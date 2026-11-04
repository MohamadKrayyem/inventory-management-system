import { NavLink, Outlet } from "react-router";
import {
  ArrowLeftRight,
  Boxes,
  ClipboardList,
  LayoutDashboard,
  Package,
  Tags,
  Truck,
  Warehouse,
  type LucideIcon,
} from "lucide-react";
import { useAuth } from "../auth";

type NavGroup = { title: string; links: { to: string; label: string; icon: LucideIcon }[] };

const groups: NavGroup[] = [
  { title: "Overview", links: [{ to: "/", label: "Dashboard", icon: LayoutDashboard }] },
  {
    title: "Inventory",
    links: [
      { to: "/products", label: "Products", icon: Package },
      { to: "/stock", label: "Stock", icon: Boxes },
      { to: "/movements", label: "Movements", icon: ArrowLeftRight },
    ],
  },
  {
    title: "Purchasing",
    links: [
      { to: "/purchase-orders", label: "Purchase Orders", icon: ClipboardList },
      { to: "/suppliers", label: "Suppliers", icon: Truck },
    ],
  },
  {
    title: "Setup",
    links: [
      { to: "/categories", label: "Categories", icon: Tags },
      { to: "/warehouses", label: "Warehouses", icon: Warehouse },
    ],
  },
];

export default function Layout() {
  const { user, logout } = useAuth();

  return (
    <div className="layout">
      <aside className="sidebar">
        <div className="brand">
          <Package size={22} />
          <span>Inventory</span>
        </div>
        <nav>
          {groups.map((group) => (
            <div key={group.title} className="nav-group">
              <div className="nav-title">{group.title}</div>
              {group.links.map(({ to, label, icon: Icon }) => (
                <NavLink key={to} to={to} end={to === "/"} title={label}>
                  <Icon size={18} />
                  <span>{label}</span>
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
      </aside>

      <div className="main">
        <header className="topbar">
          <span>
            {user?.fullName} <span className="badge">{user?.role}</span>
          </span>
          <button className="btn" onClick={logout}>
            Logout
          </button>
        </header>
        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}

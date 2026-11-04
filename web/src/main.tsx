import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter, Navigate, Route, Routes } from "react-router";
import { AuthProvider, useAuth } from "./auth";
import Layout from "./components/Layout";
import Login from "./pages/Login";
import Dashboard from "./pages/Dashboard";
import Products from "./pages/Products";
import CrudPage from "./pages/CrudPage";
import Stock from "./pages/Stock";
import Movements from "./pages/Movements";
import PurchaseOrders from "./pages/PurchaseOrders";
import PurchaseOrder from "./pages/PurchaseOrder";
import "./styles.css";

// Shows the app layout when logged in, otherwise sends the user to /login
function RequireAuth() {
  const { user } = useAuth();
  return user ? <Layout /> : <Navigate to="/login" replace />;
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route element={<RequireAuth />}>
            <Route index element={<Dashboard />} />
            <Route path="products" element={<Products />} />
            <Route path="stock" element={<Stock />} />
            <Route path="movements" element={<Movements />} />
            <Route path="purchase-orders" element={<PurchaseOrders />} />
            <Route path="purchase-orders/:id" element={<PurchaseOrder />} />
            <Route
              path="categories"
              element={<CrudPage key="categories" title="Categories" path="/categories" fields={[{ name: "name", label: "Name" }]} />}
            />
            <Route
              path="suppliers"
              element={
                <CrudPage
                  key="suppliers"
                  title="Suppliers"
                  path="/suppliers"
                  fields={[
                    { name: "name", label: "Name" },
                    { name: "phone", label: "Phone" },
                    { name: "email", label: "Email" },
                    { name: "address", label: "Address" },
                  ]}
                />
              }
            />
            <Route
              path="warehouses"
              element={
                <CrudPage
                  key="warehouses"
                  title="Warehouses"
                  path="/warehouses"
                  fields={[
                    { name: "name", label: "Name" },
                    { name: "location", label: "Location" },
                  ]}
                />
              }
            />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  </StrictMode>
);

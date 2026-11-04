import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import api from "./api";

export type User = { id: number; fullName: string; email: string; role: "Admin" | "Staff" };

type AuthValue = {
  user: User | null;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
};

const AuthContext = createContext<AuthValue>(null!);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState(!!localStorage.getItem("token"));

  // If a token is saved, load the user it belongs to
  useEffect(() => {
    if (!localStorage.getItem("token")) return;
    api
      .get<User>("/auth/me")
      .then((r) => setUser(r.data))
      .catch(() => localStorage.removeItem("token"))
      .finally(() => setLoading(false));
  }, []);

  async function login(email: string, password: string) {
    const { data } = await api.post<{ token: string; user: User }>("/auth/login", { email, password });
    localStorage.setItem("token", data.token);
    setUser(data.user);
  }

  function logout() {
    localStorage.removeItem("token");
    setUser(null);
  }

  if (loading) return <p className="center muted">Loading...</p>;

  return <AuthContext.Provider value={{ user, login, logout }}>{children}</AuthContext.Provider>;
}

export const useAuth = () => useContext(AuthContext);

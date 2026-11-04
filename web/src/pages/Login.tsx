import { useState, type FormEvent } from "react";
import { Navigate } from "react-router";
import { useAuth } from "../auth";
import { errorMessage } from "../api";

export default function Login() {
  const { user, login } = useAuth();
  const [email, setEmail] = useState("admin@demo.com");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  if (user) return <Navigate to="/" replace />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError("");
    try {
      await login(email, password);
    } catch (err) {
      setError(errorMessage(err));
      setLoading(false);
    }
  }

  return (
    <div className="login-page">
      <form className="card login-card" onSubmit={submit}>
        <h1>Inventory</h1>
        <p className="muted">Sign in to continue</p>

        {error && <div className="alert">{error}</div>}

        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>

        <button className="btn primary full" disabled={loading}>
          {loading ? "Signing in..." : "Sign in"}
        </button>

        <p className="muted small">
          Demo: admin@demo.com or staff@demo.com, password <b>Demo@123</b>
        </p>
      </form>
    </div>
  );
}

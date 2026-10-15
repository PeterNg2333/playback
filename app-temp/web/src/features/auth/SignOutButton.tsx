import { api, csrfToken } from "../../lib/backend/client";
import { showError } from "../../lib/store";

export function SignOutButton() {
  if (!csrfToken()) return null;
  return (
    <button
      className="rounded-lg px-2 py-1.5 text-xs text-muted hover:bg-panel"
      onClick={() =>
        api
          .post("/auth/logout")
          .then(() => window.location.assign("/login"))
          .catch(showError)
      }
    >
      Sign out
    </button>
  );
}

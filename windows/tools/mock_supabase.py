"""Tiny in-memory stand-in for the Supabase REST endpoints the app uses.

Used only by the self-test so it never touches the real database:
    python tools/mock_supabase.py 8765 [state.json]
"""
import json
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
DUMP = sys.argv[2] if len(sys.argv) > 2 else None
LOCK = threading.Lock()
DB = {
    "projects": {
        "proj-default": {"id": "proj-default", "name": "Main", "color_key": "coral", "sort_order": 0, "created_at": "2026-11-01T00:00:00.000Z"},
        "proj-blue": {"id": "proj-blue", "name": "Studio", "color_key": "blue", "sort_order": 1, "created_at": "2026-11-01T00:01:00.000Z"},
    },
    "issues": {
        "seed-1": {"id": "seed-1", "project_id": "proj-default", "section": "prompted", "title": "Seeded running task", "completed": False,
                   "active": True, "expanded": False, "comments": [{"id": "c1", "text": "hello from server", "createdAt": "2026-11-02T10:15:00.000Z"}],
                   "created_at": "2026-11-02T10:10:00.000Z"},
        "seed-2": {"id": "seed-2", "project_id": "proj-default", "section": "drafts", "title": "Seeded idea", "completed": False,
                   "active": False, "expanded": False, "comments": [], "created_at": "2026-11-02T09:10:00.000Z"},
    },
}


def dump():
    if DUMP:
        with open(DUMP, "w", encoding="utf-8") as f:
            json.dump(DB, f, indent=1)


class H(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        sys.stderr.write("%s %s\n" % (self.command, self.path[:120]))

    def _table(self):
        u = urlparse(self.path)
        parts = u.path.strip("/").split("/")
        return parts, parse_qs(u.query)

    def _send(self, code, body=None):
        data = b"" if body is None else json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _auth_ok(self):
        return self.headers.get("apikey") and self.headers.get("Authorization", "").startswith("Bearer ")

    def do_GET(self):
        parts, q = self._table()
        if parts[:2] != ["rest", "v1"] or not self._auth_ok():
            return self._send(404, {"message": "not found"})
        with LOCK:
            rows = list(DB.get(parts[2], {}).values())
        self._send(200, rows)

    def do_POST(self):
        parts, q = self._table()
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        if parts[:1] == ["storage"]:
            return self._send(400, {"message": "Bucket not found"})
        if parts[:2] != ["rest", "v1"] or not self._auth_ok():
            return self._send(404, {"message": "not found"})
        if "merge-duplicates" not in self.headers.get("Prefer", ""):
            return self._send(400, {"message": "expected upsert"})
        payload = json.loads(body)
        rows = payload if isinstance(payload, list) else [payload]
        with LOCK:
            for r in rows:
                DB.setdefault(parts[2], {})[r["id"]] = r
            dump()
        self._send(201)

    def do_DELETE(self):
        parts, q = self._table()
        if parts[:2] != ["rest", "v1"] or not self._auth_ok():
            return self._send(404, {"message": "not found"})
        ident = q.get("id", [""])[0]
        if not ident.startswith("eq."):
            return self._send(400, {"message": "refusing unfiltered delete"})
        with LOCK:
            DB.get(parts[2], {}).pop(ident[3:], None)
            dump()
        self._send(204)


if __name__ == "__main__":
    dump()
    ThreadingHTTPServer(("127.0.0.1", PORT), H).serve_forever()

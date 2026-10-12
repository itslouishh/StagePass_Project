"""
StagePass REST API  -  Theater Ticketing & Seat Reservation System
Backend: Python 3 standard library only (http.server + sqlite3). No pip install needed.

Run:   python server.py            (listens on http://localhost:5000)
DB:    stagepass.db (SQLite, auto-created and seeded on first run)
"""
import json
import re
import secrets
import sqlite3
import sys
import uuid
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

DB_PATH = Path(__file__).with_name("stagepass.db")
ADMIN_USER, ADMIN_PASS = "admin", "admin123"
MAX_SEATS_PER_BOOKING = 8
TOKENS = set()  # in-memory admin session tokens
EMAIL_RE = re.compile(r"^[^@\s]+@[^@\s]+\.[^@\s]+$")


# ----------------------------------------------------------------- database
def connect():
    conn = sqlite3.connect(DB_PATH, timeout=10, isolation_level=None)  # manual transactions
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA foreign_keys = ON")
    return conn


SCHEMA = """
CREATE TABLE IF NOT EXISTS venues (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    seat_rows INTEGER NOT NULL CHECK (seat_rows BETWEEN 1 AND 26),
    seat_cols INTEGER NOT NULL CHECK (seat_cols BETWEEN 1 AND 30)
);
CREATE TABLE IF NOT EXISTS shows (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    venue_id INTEGER NOT NULL REFERENCES venues(id),
    show_datetime TEXT NOT NULL,              -- ISO 'YYYY-MM-DD HH:MM'
    base_price REAL NOT NULL CHECK (base_price > 0),
    status TEXT NOT NULL DEFAULT 'Active' CHECK (status IN ('Active','Cancelled'))
);
CREATE TABLE IF NOT EXISTS customers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    email TEXT NOT NULL UNIQUE,
    phone TEXT NOT NULL DEFAULT ''
);
CREATE TABLE IF NOT EXISTS bookings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    reference TEXT NOT NULL UNIQUE,
    customer_id INTEGER NOT NULL REFERENCES customers(id),
    show_id INTEGER NOT NULL REFERENCES shows(id),
    total REAL NOT NULL,
    status TEXT NOT NULL DEFAULT 'Confirmed' CHECK (status IN ('Confirmed','Cancelled')),
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS booking_seats (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    booking_id INTEGER NOT NULL REFERENCES bookings(id) ON DELETE CASCADE,
    show_id INTEGER NOT NULL REFERENCES shows(id),
    seat_row INTEGER NOT NULL,
    seat_col INTEGER NOT NULL,
    tier TEXT NOT NULL,
    price REAL NOT NULL,
    UNIQUE (show_id, seat_row, seat_col)      -- DB-level double-booking protection
);
"""


def init_db():
    with connect() as c:
        c.executescript(SCHEMA)
        if c.execute("SELECT COUNT(*) FROM venues").fetchone()[0] == 0:
            c.execute("INSERT INTO venues(name, seat_rows, seat_cols) VALUES ('Main Theater', 10, 12)")
            c.execute("INSERT INTO venues(name, seat_rows, seat_cols) VALUES ('Studio Stage', 6, 8)")
            seed = [
                ("The Phantom of the Opera", "Classic musical about a masked genius haunting an opera house.", 1, "2026-12-05 19:30", 800),
                ("Romeo and Juliet", "Shakespeare's timeless tragedy of star-crossed lovers.", 1, "2026-12-12 18:00", 650),
                ("Jazz Under the Lights", "An intimate evening concert with a live jazz quartet.", 2, "2026-12-19 20:00", 500),
                ("Les Miserables", "Epic musical of redemption set in 19th-century France.", 1, "2027-01-09 19:00", 900),
            ]
            c.executemany("INSERT INTO shows(title, description, venue_id, show_datetime, base_price) VALUES (?,?,?,?,?)", seed)


# ----------------------------------------------------------------- business rules
def row_letter(r):  # 1 -> 'A'
    return chr(64 + r)


def tier_for(row, total_rows):
    """Pricing tier by row: front ~20% VIP (x2.0), next ~40% Orchestra (x1.5), rest Balcony (x1.0)."""
    if row <= max(1, round(total_rows * 0.2)):
        return "VIP", 2.0
    if row <= max(2, round(total_rows * 0.6)):
        return "Orchestra", 1.5
    return "Balcony", 1.0


def parse_dt(s):
    try:
        return datetime.strptime(s, "%Y-%m-%d %H:%M")
    except (TypeError, ValueError):
        raise ApiError(400, "show_datetime must be 'YYYY-MM-DD HH:MM'")


class ApiError(Exception):
    def __init__(self, status, message):
        super().__init__(message)
        self.status, self.message = status, message


def show_dict(r, sold=None):
    d = {"id": r["id"], "title": r["title"], "description": r["description"],
         "venue_id": r["venue_id"], "venue_name": r["venue_name"],
         "show_datetime": r["show_datetime"], "base_price": r["base_price"], "status": r["status"]}
    if sold is not None:
        d["seats_sold"] = sold
        d["capacity"] = r["seat_rows"] * r["seat_cols"]
    return d


SHOW_SQL = """SELECT s.*, v.name AS venue_name, v.seat_rows, v.seat_cols
              FROM shows s JOIN venues v ON v.id = s.venue_id"""


def get_show(c, show_id):
    r = c.execute(SHOW_SQL + " WHERE s.id = ?", (show_id,)).fetchone()
    if not r:
        raise ApiError(404, "Show not found")
    return r


def validate_show(body, c):
    title = (body.get("title") or "").strip()
    if not title:
        raise ApiError(400, "Title is required")
    dt = parse_dt(body.get("show_datetime"))
    try:
        price = float(body.get("base_price"))
    except (TypeError, ValueError):
        raise ApiError(400, "base_price must be a number")
    if price <= 0:
        raise ApiError(400, "base_price must be greater than 0")
    venue_id = body.get("venue_id")
    if not c.execute("SELECT 1 FROM venues WHERE id=?", (venue_id,)).fetchone():
        raise ApiError(400, "Unknown venue_id")
    return title, (body.get("description") or "").strip(), venue_id, dt.strftime("%Y-%m-%d %H:%M"), price


# ----------------------------------------------------------------- handlers
def list_venues(c, **_):
    return 200, [dict(r) for r in c.execute("SELECT * FROM venues ORDER BY id")]


def list_shows(c, query, **_):
    sql = SHOW_SQL
    args = []
    if query.get("status"):
        sql += " WHERE s.status = ?"; args.append(query["status"])
    sql += " ORDER BY s.show_datetime"
    out = []
    for r in c.execute(sql, args).fetchall():
        sold = c.execute("SELECT COUNT(*) FROM booking_seats WHERE show_id=?", (r["id"],)).fetchone()[0]
        out.append(show_dict(r, sold))
    return 200, out


def get_show_h(c, id, **_):
    r = get_show(c, int(id))
    sold = c.execute("SELECT COUNT(*) FROM booking_seats WHERE show_id=?", (r["id"],)).fetchone()[0]
    return 200, show_dict(r, sold)


def create_show(c, body, **_):
    title, desc, venue_id, dt, price = validate_show(body, c)
    if dt < datetime.now().strftime("%Y-%m-%d %H:%M"):
        raise ApiError(400, "Cannot schedule a show in the past")
    cur = c.execute("INSERT INTO shows(title, description, venue_id, show_datetime, base_price) VALUES (?,?,?,?,?)",
                    (title, desc, venue_id, dt, price))
    return 201, show_dict(get_show(c, cur.lastrowid))


def update_show(c, id, body, **_):
    show = get_show(c, int(id))
    title, desc, venue_id, dt, price = validate_show(body, c)
    status = body.get("status", show["status"])
    if status not in ("Active", "Cancelled"):
        raise ApiError(400, "status must be Active or Cancelled")
    sold = c.execute("SELECT COUNT(*) FROM booking_seats WHERE show_id=?", (show["id"],)).fetchone()[0]
    if sold and venue_id != show["venue_id"]:
        raise ApiError(409, "Cannot change venue: seats are already booked for this show")
    if sold and price != show["base_price"]:
        raise ApiError(409, "Cannot change price: tickets were already sold at the current price")
    c.execute("BEGIN IMMEDIATE")
    c.execute("UPDATE shows SET title=?, description=?, venue_id=?, show_datetime=?, base_price=?, status=? WHERE id=?",
              (title, desc, venue_id, dt, price, status, show["id"]))
    if status == "Cancelled" and show["status"] != "Cancelled":
        _cancel_show_bookings(c, show["id"])
    c.execute("COMMIT")
    return 200, show_dict(get_show(c, show["id"]))


def _cancel_show_bookings(c, show_id):
    c.execute("UPDATE bookings SET status='Cancelled' WHERE show_id=? AND status='Confirmed'", (show_id,))
    c.execute("DELETE FROM booking_seats WHERE show_id=?", (show_id,))


def delete_show(c, id, **_):
    show = get_show(c, int(id))
    n = c.execute("SELECT COUNT(*) FROM bookings WHERE show_id=?", (show["id"],)).fetchone()[0]
    if n:
        raise ApiError(409, "Show has booking history and cannot be deleted. Cancel it instead (PUT status=Cancelled).")
    c.execute("DELETE FROM shows WHERE id=?", (show["id"],))
    return 200, {"deleted": show["id"]}


def seat_map(c, id, **_):
    show = get_show(c, int(id))
    taken = {(r["seat_row"], r["seat_col"]) for r in
             c.execute("SELECT seat_row, seat_col FROM booking_seats WHERE show_id=?", (show["id"],))}
    seats = []
    for r in range(1, show["seat_rows"] + 1):
        tier, mult = tier_for(r, show["seat_rows"])
        for col in range(1, show["seat_cols"] + 1):
            seats.append({"row": r, "col": col, "label": f"{row_letter(r)}{col}", "tier": tier,
                          "price": round(show["base_price"] * mult, 2),
                          "status": "Booked" if (r, col) in taken else "Available"})
    return 200, {"show_id": show["id"], "rows": show["seat_rows"], "cols": show["seat_cols"], "seats": seats}


def create_booking(c, body, **_):
    show = get_show(c, int(body.get("show_id") or 0))
    cust = body.get("customer") or {}
    name, email, phone = (cust.get("name") or "").strip(), (cust.get("email") or "").strip().lower(), (cust.get("phone") or "").strip()
    if not name:
        raise ApiError(400, "Customer name is required")
    if not EMAIL_RE.match(email):
        raise ApiError(400, "A valid customer email is required")
    seats = body.get("seats") or []
    if not seats:
        raise ApiError(400, "Select at least one seat")
    if len(seats) > MAX_SEATS_PER_BOOKING:
        raise ApiError(400, f"A booking may contain at most {MAX_SEATS_PER_BOOKING} seats")
    if show["status"] != "Active":
        raise ApiError(409, "This show has been cancelled")
    if show["show_datetime"] < datetime.now().strftime("%Y-%m-%d %H:%M"):
        raise ApiError(409, "This show has already taken place")
    keys = []
    for s in seats:
        try:
            r, col = int(s["row"]), int(s["col"])
        except (KeyError, TypeError, ValueError):
            raise ApiError(400, "Each seat needs numeric 'row' and 'col'")
        if not (1 <= r <= show["seat_rows"] and 1 <= col <= show["seat_cols"]):
            raise ApiError(400, f"Seat {row_letter(r)}{col} does not exist in this venue")
        keys.append((r, col))
    if len(set(keys)) != len(keys):
        raise ApiError(400, "Duplicate seats in request")

    c.execute("BEGIN IMMEDIATE")  # lock so two customers cannot grab the same seat
    try:
        taken = [f"{row_letter(r)}{col}" for r, col in keys if c.execute(
            "SELECT 1 FROM booking_seats WHERE show_id=? AND seat_row=? AND seat_col=?", (show["id"], r, col)).fetchone()]
        if taken:
            raise ApiError(409, "Seat(s) already booked: " + ", ".join(taken))
        c.execute("INSERT INTO customers(name, email, phone) VALUES (?,?,?) "
                  "ON CONFLICT(email) DO UPDATE SET name=excluded.name, phone=excluded.phone", (name, email, phone))
        cust_id = c.execute("SELECT id FROM customers WHERE email=?", (email,)).fetchone()[0]
        priced = []
        for r, col in keys:
            tier, mult = tier_for(r, show["seat_rows"])
            priced.append((r, col, tier, round(show["base_price"] * mult, 2)))
        total = round(sum(p[3] for p in priced), 2)
        ref = "SP-" + uuid.uuid4().hex[:8].upper()
        cur = c.execute("INSERT INTO bookings(reference, customer_id, show_id, total, created_at) VALUES (?,?,?,?,?)",
                        (ref, cust_id, show["id"], total, datetime.now().strftime("%Y-%m-%d %H:%M:%S")))
        c.executemany("INSERT INTO booking_seats(booking_id, show_id, seat_row, seat_col, tier, price) VALUES (?,?,?,?,?,?)",
                      [(cur.lastrowid, show["id"], *p) for p in priced])
        c.execute("COMMIT")
    except Exception:
        c.execute("ROLLBACK")
        raise
    return 201, booking_dict(c, cur.lastrowid)


def booking_dict(c, bid):
    b = c.execute("""SELECT b.*, cu.name AS customer_name, cu.email, cu.phone, s.title, s.show_datetime, v.name AS venue_name
                     FROM bookings b JOIN customers cu ON cu.id=b.customer_id
                     JOIN shows s ON s.id=b.show_id JOIN venues v ON v.id=s.venue_id WHERE b.id=?""", (bid,)).fetchone()
    if not b:
        raise ApiError(404, "Booking not found")
    seats = [{"label": f"{row_letter(r['seat_row'])}{r['seat_col']}", "row": r["seat_row"], "col": r["seat_col"],
              "tier": r["tier"], "price": r["price"]}
             for r in c.execute("SELECT * FROM booking_seats WHERE booking_id=? ORDER BY seat_row, seat_col", (bid,))]
    return {"id": b["id"], "reference": b["reference"], "status": b["status"], "total": b["total"],
            "created_at": b["created_at"], "show_id": b["show_id"], "show_title": b["title"],
            "show_datetime": b["show_datetime"], "venue_name": b["venue_name"],
            "customer_name": b["customer_name"], "email": b["email"], "phone": b["phone"], "seats": seats}


def list_bookings(c, query, **_):
    sql, args, where = "SELECT b.id FROM bookings b JOIN customers cu ON cu.id=b.customer_id", [], []
    if query.get("email"):
        where.append("cu.email = ?"); args.append(query["email"].strip().lower())
    if query.get("reference"):
        where.append("b.reference = ?"); args.append(query["reference"].strip().upper())
    if query.get("show_id"):
        where.append("b.show_id = ?"); args.append(int(query["show_id"]))
    if where:
        sql += " WHERE " + " AND ".join(where)
    return 200, [booking_dict(c, r["id"]) for r in c.execute(sql + " ORDER BY b.id DESC", args)]


def get_booking(c, id, **_):
    return 200, booking_dict(c, int(id))


def update_booking(c, id, body, **_):
    """Change the seats of a confirmed booking (customer re-seats) - recalculates total."""
    b = booking_dict(c, int(id))
    if b["status"] != "Confirmed":
        raise ApiError(409, "Only confirmed bookings can be changed")
    show = get_show(c, b["show_id"])
    if show["show_datetime"] < datetime.now().strftime("%Y-%m-%d %H:%M"):
        raise ApiError(409, "Show already taken place")
    seats = body.get("seats") or []
    if not seats or len(seats) > MAX_SEATS_PER_BOOKING:
        raise ApiError(400, f"Provide 1-{MAX_SEATS_PER_BOOKING} seats")
    keys = []
    for s in seats:
        r, col = int(s["row"]), int(s["col"])
        if not (1 <= r <= show["seat_rows"] and 1 <= col <= show["seat_cols"]):
            raise ApiError(400, "Seat does not exist")
        keys.append((r, col))
    if len(set(keys)) != len(keys):
        raise ApiError(400, "Duplicate seats in request")
    c.execute("BEGIN IMMEDIATE")
    try:
        c.execute("DELETE FROM booking_seats WHERE booking_id=?", (b["id"],))
        clash = [f"{row_letter(r)}{col}" for r, col in keys if c.execute(
            "SELECT 1 FROM booking_seats WHERE show_id=? AND seat_row=? AND seat_col=?", (show["id"], r, col)).fetchone()]
        if clash:
            raise ApiError(409, "Seat(s) already booked: " + ", ".join(clash))
        total = 0.0
        for r, col in keys:
            tier, mult = tier_for(r, show["seat_rows"])
            price = round(show["base_price"] * mult, 2); total += price
            c.execute("INSERT INTO booking_seats(booking_id, show_id, seat_row, seat_col, tier, price) VALUES (?,?,?,?,?,?)",
                      (b["id"], show["id"], r, col, tier, price))
        c.execute("UPDATE bookings SET total=? WHERE id=?", (round(total, 2), b["id"]))
        c.execute("COMMIT")
    except Exception:
        c.execute("ROLLBACK")
        raise
    return 200, booking_dict(c, b["id"])


def cancel_booking(c, id, **_):
    b = booking_dict(c, int(id))
    if b["status"] == "Cancelled":
        raise ApiError(409, "Booking is already cancelled")
    if b["show_datetime"] < datetime.now().strftime("%Y-%m-%d %H:%M"):
        raise ApiError(409, "Cannot cancel a booking for a show that already took place")
    c.execute("BEGIN IMMEDIATE")
    c.execute("UPDATE bookings SET status='Cancelled' WHERE id=?", (b["id"],))
    c.execute("DELETE FROM booking_seats WHERE booking_id=?", (b["id"],))  # seats become available again
    c.execute("COMMIT")
    return 200, booking_dict(c, b["id"])


def report_summary(c, **_):
    tickets, revenue = c.execute("SELECT COUNT(*), COALESCE(SUM(price),0) FROM booking_seats").fetchone()
    confirmed = c.execute("SELECT COUNT(*) FROM bookings WHERE status='Confirmed'").fetchone()[0]
    cancelled = c.execute("SELECT COUNT(*) FROM bookings WHERE status='Cancelled'").fetchone()[0]
    now = datetime.now().strftime("%Y-%m-%d %H:%M")
    per_show = []
    for r in c.execute(SHOW_SQL + " ORDER BY s.show_datetime").fetchall():
        sold, rev = c.execute("SELECT COUNT(*), COALESCE(SUM(price),0) FROM booking_seats WHERE show_id=?", (r["id"],)).fetchone()
        cap = r["seat_rows"] * r["seat_cols"]
        per_show.append({"show_id": r["id"], "title": r["title"], "show_datetime": r["show_datetime"], "status": r["status"],
                         "tickets_sold": sold, "capacity": cap, "occupancy_pct": round(100 * sold / cap, 1),
                         "revenue": round(rev, 2), "upcoming": r["show_datetime"] >= now and r["status"] == "Active"})
    return 200, {"total_tickets_sold": tickets, "total_revenue": round(revenue, 2),
                 "confirmed_bookings": confirmed, "cancelled_bookings": cancelled,
                 "upcoming_shows": sum(1 for p in per_show if p["upcoming"]), "per_show": per_show}


def login(c, body, **_):
    if body.get("username") == ADMIN_USER and body.get("password") == ADMIN_PASS:
        t = secrets.token_hex(16); TOKENS.add(t)
        return 200, {"token": t}
    raise ApiError(401, "Invalid username or password")


# (method, regex, handler, admin_only)
ROUTES = [
    ("POST",   r"/api/auth/login",           login,           False),
    ("GET",    r"/api/venues",               list_venues,     False),
    ("GET",    r"/api/shows",                list_shows,      False),
    ("GET",    r"/api/shows/(?P<id>\d+)",    get_show_h,      False),
    ("POST",   r"/api/shows",                create_show,     True),
    ("PUT",    r"/api/shows/(?P<id>\d+)",    update_show,     True),
    ("DELETE", r"/api/shows/(?P<id>\d+)",    delete_show,     True),
    ("GET",    r"/api/shows/(?P<id>\d+)/seats", seat_map,     False),
    ("POST",   r"/api/bookings",             create_booking,  False),
    ("GET",    r"/api/bookings",             list_bookings,   False),
    ("GET",    r"/api/bookings/(?P<id>\d+)", get_booking,     False),
    ("PUT",    r"/api/bookings/(?P<id>\d+)", update_booking,  False),
    ("DELETE", r"/api/bookings/(?P<id>\d+)", cancel_booking,  False),
    ("GET",    r"/api/reports/summary",      report_summary,  True),
]


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        sys.stderr.write("%s %s\n" % (self.command, self.path))

    def _send(self, status, payload):
        data = json.dumps(payload).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _dispatch(self):
        from urllib.parse import urlparse, parse_qs
        url = urlparse(self.path)
        query = {k: v[0] for k, v in parse_qs(url.query).items()}
        path_matched = False
        try:
            for method, rx, fn, admin in ROUTES:
                m = re.fullmatch(rx, url.path)
                if not m:
                    continue
                path_matched = True
                if method != self.command:
                    continue
                if admin and self.headers.get("Authorization", "").replace("Bearer ", "") not in TOKENS:
                    raise ApiError(401, "Admin login required")
                body = {}
                if self.command in ("POST", "PUT"):
                    raw = self.rfile.read(int(self.headers.get("Content-Length") or 0))
                    try:
                        body = json.loads(raw or b"{}")
                    except json.JSONDecodeError:
                        raise ApiError(400, "Invalid JSON body")
                conn = connect()
                try:
                    status, payload = fn(conn, query=query, body=body, **m.groupdict())
                finally:
                    conn.close()
                return self._send(status, payload)
            raise ApiError(405 if path_matched else 404, "Method not allowed" if path_matched else "Route not found")
        except ApiError as e:
            self._send(e.status, {"error": e.message})
        except Exception as e:  # unexpected
            self._send(500, {"error": f"Server error: {e}"})

    do_GET = do_POST = do_PUT = do_DELETE = _dispatch


if __name__ == "__main__":
    init_db()
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 5000
    print(f"StagePass API running on http://localhost:{port}  (admin / admin123)")
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()

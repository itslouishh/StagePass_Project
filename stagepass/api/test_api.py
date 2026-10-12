"""Automated API tests. Start the server first (python server.py), then: python test_api.py"""
import json, sys, urllib.request, urllib.error
BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5000"
tok = None; passed = failed = 0

def call(method, path, body=None, auth=False):
    req = urllib.request.Request(BASE + path, method=method, data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Content-Type": "application/json", **({"Authorization": "Bearer " + tok} if auth else {})})
    try:
        with urllib.request.urlopen(req) as r: return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e: return e.code, json.loads(e.read())

def check(name, cond):
    global passed, failed
    print(("PASS  " if cond else "FAIL  ") + name); passed += bool(cond); failed += (not cond)

s, r = call("POST", "/api/auth/login", {"username": "admin", "password": "bad"}); check("login rejects bad password (401)", s == 401)
s, r = call("POST", "/api/auth/login", {"username": "admin", "password": "admin123"}); tok = r.get("token"); check("admin login returns token", s == 200 and tok)
s, r = call("GET", "/api/reports/summary"); check("report requires admin (401)", s == 401)
s, shows = call("GET", "/api/shows"); check("list shows", s == 200 and len(shows) >= 4)
s, r = call("GET", "/api/venues"); check("list venues", s == 200 and len(r) == 2)
s, r = call("POST", "/api/shows", {"title": "X", "venue_id": 1, "show_datetime": "2020-01-01 10:00", "base_price": 100}, True); check("cannot create show in past (400)", s == 400)
s, r = call("POST", "/api/shows", {"title": "Hamlet", "description": "d", "venue_id": 2, "show_datetime": "2027-03-01 19:00", "base_price": 400}, True); check("create show (201)", s == 201); new = r["id"]
s, r = call("POST", "/api/shows", {"title": "Hamlet", "venue_id": 2, "show_datetime": "2027-03-01 19:00", "base_price": 400}); check("create show without admin (401)", s == 401)
s, r = call("PUT", f"/api/shows/{new}", {"title": "Hamlet (Rescheduled)", "venue_id": 2, "show_datetime": "2027-03-08 19:00", "base_price": 450}, True); check("update/reschedule show", s == 200 and r["show_datetime"].startswith("2027-03-08"))
s, r = call("GET", f"/api/shows/{new}/seats"); check("seat map size 6x8=48", s == 200 and len(r["seats"]) == 48)
tiers = {x["tier"] for x in r["seats"]}; check("three price tiers present", tiers == {"VIP", "Orchestra", "Balcony"})
vip = next(x for x in r["seats"] if x["tier"] == "VIP"); check("VIP price = 2x base", vip["price"] == 900)
cust = {"name": "Ana Cruz", "email": "Ana@Example.com", "phone": "0917"}
s, b = call("POST", "/api/bookings", {"show_id": new, "customer": cust, "seats": [{"row": 1, "col": 1}, {"row": 6, "col": 8}]}); check("create booking (201)", s == 201 and b["status"] == "Confirmed")
check("total = VIP 900 + Balcony 450 = 1350", b["total"] == 1350); check("reference generated", b["reference"].startswith("SP-"))
s, r = call("POST", "/api/bookings", {"show_id": new, "customer": {"name": "Ben", "email": "ben@x.com"}, "seats": [{"row": 1, "col": 1}]}); check("double booking blocked (409)", s == 409 and "A1" in r["error"])
s, r = call("POST", "/api/bookings", {"show_id": new, "customer": {"name": "", "email": "bad"}, "seats": [{"row": 1, "col": 2}]}); check("validation: missing name (400)", s == 400)
s, r = call("POST", "/api/bookings", {"show_id": new, "customer": cust, "seats": [{"row": 99, "col": 1}]}); check("nonexistent seat (400)", s == 400)
s, r = call("POST", "/api/bookings", {"show_id": new, "customer": cust, "seats": [{"row": 2, "col": c} for c in range(1, 9)] + [{"row": 3, "col": 1}]}); check("max 8 seats rule (400)", s == 400)
s, r = call("GET", f"/api/bookings?email=ana@example.com"); check("lookup bookings by email", s == 200 and len(r) == 1)
s, r = call("GET", f"/api/shows/{new}/seats"); check("booked seat shows as Booked", next(x for x in r["seats"] if x["label"] == "A1")["status"] == "Booked")
s, r = call("PUT", f"/api/bookings/{b['id']}", {"seats": [{"row": 3, "col": 3}]}); check("change seats recalculates total (3rd row Orchestra=675)", s == 200 and r["total"] == 675)
s, r = call("GET", f"/api/shows/{new}/seats"); check("old seat freed after change", next(x for x in r["seats"] if x["label"] == "A1")["status"] == "Available")
s, r = call("DELETE", f"/api/shows/{new}", None, True); check("cannot delete show with bookings (409)", s == 409)
s, r = call("GET", "/api/reports/summary", None, True); check("report summary", s == 200 and r["total_tickets_sold"] == 1 and r["total_revenue"] == 675)
s, r = call("DELETE", f"/api/bookings/{b['id']}"); check("cancel booking", s == 200 and r["status"] == "Cancelled")
s, r = call("DELETE", f"/api/bookings/{b['id']}"); check("cancel twice blocked (409)", s == 409)
s, r = call("GET", f"/api/shows/{new}/seats"); check("seats released after cancel", all(x["status"] == "Available" for x in r["seats"]))
s, b2 = call("POST", "/api/bookings", {"show_id": new, "customer": cust, "seats": [{"row": 1, "col": 1}]})
s, r = call("PUT", f"/api/shows/{new}", {"title": "Hamlet", "venue_id": 2, "show_datetime": "2027-03-08 19:00", "base_price": 450, "status": "Cancelled"}, True); check("cancel show", s == 200 and r["status"] == "Cancelled")
s, r = call("GET", f"/api/bookings/{b2['id']}"); check("show cancellation cancels its bookings", r["status"] == "Cancelled")
s, r = call("POST", "/api/bookings", {"show_id": new, "customer": cust, "seats": [{"row": 1, "col": 2}]}); check("cannot book cancelled show (409)", s == 409)
s, r = call("DELETE", f"/api/shows/{new}", None, True); check("delete blocked while history exists (409)", s == 409)
s, r = call("POST", "/api/shows", {"title": "Temp", "venue_id": 1, "show_datetime": "2027-05-01 19:00", "base_price": 100}, True)
s, r2 = call("DELETE", f"/api/shows/{r['id']}", None, True); check("delete show with no bookings (200)", s == 200)
s, r = call("GET", "/api/nope"); check("unknown route (404)", s == 404)
print(f"\n{passed} passed, {failed} failed"); sys.exit(1 if failed else 0)

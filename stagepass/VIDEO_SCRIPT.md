# M2-VID-CAGATIN.mp4 - Video outline (aim for 5-8 minutes; face + screen visible)

Record with OBS, Zoom or Teams (camera on, share screen). Start the API first (`python server.py`).

1. **Intro (30s)** - Say your name and the project: StagePass, a theater ticketing and seat reservation system.
2. **Requirements (45s)** - WinForms GUI, RESTful API with GET/POST/PUT/DELETE, SQLite database, complete beyond CRUD.
3. **Architecture (45s)** - Show Figure 1 in the documentation: client -> ApiClient -> REST API -> database.
4. **Database (45s)** - Show the ERD and open stagepass.db (or DB Browser for SQLite). Mention the UNIQUE seat constraint.
5. **API demo (60s)** - Run `python test_api.py` and show the 34 passing tests, or call `/api/shows` in a browser.
6. **Customer demo (2 min)** - Book Tickets tab: pick a show, click seats (green/red/yellow), enter details, confirm, show the summary. Try booking a red seat to show the conflict. My Bookings: search by email, change seats, cancel.
7. **Admin demo (1.5 min)** - Log in. Add a show, reschedule it, cancel a show, try deleting a show with bookings (blocked). Open Sales Reports.
8. **Code walkthrough (1 min)** - Show ApiClient.cs (HTTP calls) and `create_booking` in server.py (transaction + price tiers).
9. **Closing (20s)** - Summarize what the system does and one future improvement.

Save as MP4 named **M2-VID-CAGATIN.mp4** and submit to the BB link M2-VIDEO.

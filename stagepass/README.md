# StagePass - Theater Ticketing & Seat Reservation System

## 1. Start the API (Python 3.9+, no packages needed)
    cd api
    python server.py          # http://localhost:5000  (admin / admin123)
    python test_api.py        # optional: runs 34 automated API tests (server must be running)

## 2. Start the WinForms client (.NET 8 SDK, Windows)
    cd client
    dotnet run                # or open StagePass.Client.csproj in Visual Studio 2022 and press F5

The client expects the API at http://localhost:5000 (change ApiClient.BaseUrl to use another address).

namespace StagePass.Client;

// Property names are mapped to the API's snake_case JSON automatically (see ApiClient).
public class Venue { public int Id { get; set; } public string Name { get; set; } public int SeatRows { get; set; } public int SeatCols { get; set; } public override string ToString() => Name; }

public class ShowInfo
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; }
    public string ShowDatetime { get; set; }
    public decimal BasePrice { get; set; }
    public string Status { get; set; }
    public int SeatsSold { get; set; }
    public int Capacity { get; set; }
}

public class ShowInput
{
    public string Title { get; set; }
    public string Description { get; set; }
    public int VenueId { get; set; }
    public string ShowDatetime { get; set; }
    public decimal BasePrice { get; set; }
    public string Status { get; set; } = "Active";
}

public class Seat { public int Row { get; set; } public int Col { get; set; } public string Label { get; set; } public string Tier { get; set; } public decimal Price { get; set; } public string Status { get; set; } }
public class SeatMap { public int ShowId { get; set; } public int Rows { get; set; } public int Cols { get; set; } public List<Seat> Seats { get; set; } = new(); }
public class SeatRef { public int Row { get; set; } public int Col { get; set; } }
public class CustomerInfo { public string Name { get; set; } public string Email { get; set; } public string Phone { get; set; } }
public class BookingRequest { public int ShowId { get; set; } public CustomerInfo Customer { get; set; } public List<SeatRef> Seats { get; set; } = new(); }
public class SeatChangeRequest { public List<SeatRef> Seats { get; set; } = new(); }
public class LoginRequest { public string Username { get; set; } public string Password { get; set; } }
public class LoginResponse { public string Token { get; set; } }

public class BookingSeat { public string Label { get; set; } public int Row { get; set; } public int Col { get; set; } public string Tier { get; set; } public decimal Price { get; set; } }

public class Booking
{
    public int Id { get; set; }
    public string Reference { get; set; }
    public string Status { get; set; }
    public decimal Total { get; set; }
    public string CreatedAt { get; set; }
    public int ShowId { get; set; }
    public string ShowTitle { get; set; }
    public string ShowDatetime { get; set; }
    public string VenueName { get; set; }
    public string CustomerName { get; set; }
    public string Email { get; set; }
    public string Phone { get; set; }
    public List<BookingSeat> Seats { get; set; } = new();
}

public class ShowReport
{
    public int ShowId { get; set; }
    public string Title { get; set; }
    public string ShowDatetime { get; set; }
    public string Status { get; set; }
    public int TicketsSold { get; set; }
    public int Capacity { get; set; }
    public double OccupancyPct { get; set; }
    public decimal Revenue { get; set; }
    public bool Upcoming { get; set; }
}

public class ReportSummary
{
    public int TotalTicketsSold { get; set; }
    public decimal TotalRevenue { get; set; }
    public int ConfirmedBookings { get; set; }
    public int CancelledBookings { get; set; }
    public int UpcomingShows { get; set; }
    public List<ShowReport> PerShow { get; set; } = new();
}

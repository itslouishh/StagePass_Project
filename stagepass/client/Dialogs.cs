namespace StagePass.Client;

public class AdminLoginForm : Form
{
    readonly TextBox txtUser = new() { Left = 110, Top = 20, Width = 180, Text = "admin" };
    readonly TextBox txtPass = new() { Left = 110, Top = 55, Width = 180, UseSystemPasswordChar = true };

    public AdminLoginForm()
    {
        Text = "Admin Login";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(320, 150);

        var btnLogin = new Button { Text = "Login", Left = 110, Top = 95, Width = 85 };
        var btnCancel = new Button { Text = "Cancel", Left = 205, Top = 95, Width = 85, DialogResult = DialogResult.Cancel };
        btnLogin.Click += async (s, e) =>
        {
            try
            {
                await ApiClient.Login(txtUser.Text.Trim(), txtPass.Text);
                DialogResult = DialogResult.OK;
            }
            catch (ApiException ex) { MessageBox.Show(ex.Message, "Login failed", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (HttpRequestException) { MessageBox.Show("Cannot reach the API. Is server.py running?", "Connection error"); }
        };
        Controls.AddRange(new Control[]
        {
            new Label { Text = "Username", Left = 20, Top = 23, Width = 85 },
            new Label { Text = "Password", Left = 20, Top = 58, Width = 85 },
            txtUser, txtPass, btnLogin, btnCancel
        });
        AcceptButton = btnLogin; CancelButton = btnCancel;
    }
}

/// <summary>Lets a customer move a confirmed booking to different seats (PUT /api/bookings/{id}).</summary>
public class ChangeSeatsForm : Form
{
    readonly SeatPicker picker = new() { Dock = DockStyle.Fill };
    readonly Label lblTotal = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    readonly Booking booking;
    public Booking Updated { get; private set; }

    public ChangeSeatsForm(Booking b, SeatMap map)
    {
        booking = b;
        Text = $"Change seats - {b.Reference} ({b.ShowTitle})";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 640);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        var btnSave = new Button { Text = "Save Changes", Dock = DockStyle.Fill };
        var btnClose = new Button { Text = "Close", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };
        bottom.Controls.Add(lblTotal, 0, 0);
        bottom.Controls.Add(btnSave, 1, 0);
        bottom.Controls.Add(btnClose, 2, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.Controls.Add(picker, 0, 0);
        layout.Controls.Add(bottom, 0, 1);
        Controls.Add(layout);

        picker.LoadMap(map, b.Seats.Select(s => s.Label).ToList());
        picker.SelectionChanged += (s, e) =>
            lblTotal.Text = $"Selected: {picker.SelectedSeats.Count} seat(s)   Total: ₱{picker.SelectedSeats.Sum(x => x.Price):N2}";
        lblTotal.Text = $"Selected: {picker.SelectedSeats.Count} seat(s)   Total: ₱{picker.SelectedSeats.Sum(x => x.Price):N2}";

        btnSave.Click += async (s, e) =>
        {
            if (picker.SelectedSeats.Count == 0) { MessageBox.Show("Select at least one seat."); return; }
            try
            {
                var req = new SeatChangeRequest { Seats = picker.SelectedSeats.Select(x => new SeatRef { Row = x.Row, Col = x.Col }).ToList() };
                Updated = await ApiClient.ChangeSeats(booking.Id, req);
                DialogResult = DialogResult.OK;
            }
            catch (ApiException ex) { MessageBox.Show(ex.Message, "Cannot change seats", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (HttpRequestException) { MessageBox.Show("Cannot reach the API.", "Connection error"); }
        };
    }
}

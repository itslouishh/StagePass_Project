using System.Globalization;
using System.Text;

namespace StagePass.Client;

public class MainForm : Form
{
    readonly TabControl tabs = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
    int previousTab = 0;

    // ---- Book tab
    readonly ListView lvShows = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
    readonly Label lblShowInfo = new() { Dock = DockStyle.Fill, Padding = new Padding(4) };
    readonly SeatPicker picker = new() { Dock = DockStyle.Fill };
    readonly TextBox txtName = new() { Width = 200 }, txtEmail = new() { Width = 200 }, txtPhone = new() { Width = 200 };
    readonly Label lblSummary = new() { AutoSize = false, Width = 360, Height = 56, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
    readonly Button btnBook = new() { Text = "Confirm Booking", Width = 160, Height = 40, BackColor = Color.FromArgb(39, 174, 96), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
    List<ShowInfo> shows = new();
    ShowInfo currentShow;

    // ---- My bookings tab
    readonly TextBox txtSearch = new() { Width = 280 };
    readonly ListView lvBookings = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
    List<Booking> bookings = new();

    // ---- Admin tab
    readonly ListView lvAdminShows = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
    readonly TextBox txtTitle = new() { Width = 300 }, txtDesc = new() { Width = 300, Height = 70, Multiline = true };
    readonly ComboBox cboVenue = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly DateTimePicker dtpWhen = new() { Width = 300, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm" };
    readonly NumericUpDown numPrice = new() { Width = 120, DecimalPlaces = 2, Maximum = 100000, Minimum = 1, Value = 500 };
    readonly Label lblStatus = new() { AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Italic) };
    readonly Label lblKpi = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
    readonly ListView lvReport = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    List<ShowInfo> adminShows = new();
    ShowInfo selectedAdminShow;

    public MainForm()
    {
        Text = "StagePass - Theater Ticketing & Seat Reservation";
        Size = new Size(1280, 800);
        StartPosition = FormStartPosition.CenterScreen;

        var tBook = new TabPage("🎭 Book Tickets");
        var tMine = new TabPage("🎟 My Bookings");
        var tAdmin = new TabPage("🔒 Admin");
        tabs.TabPages.AddRange(new[] { tBook, tMine, tAdmin });
        BuildBookTab(tBook);
        BuildMyBookingsTab(tMine);
        BuildAdminTab(tAdmin);
        Controls.Add(tabs);

        tabs.SelectedIndexChanged += async (s, e) => await OnTabChanged();
        Load += async (s, e) => await Safe(RefreshShows);
    }

    // =====================================================================  helpers
    static async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (ApiException ex) { MessageBox.Show(ex.Message, "StagePass", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
        {
            MessageBox.Show($"Cannot reach the StagePass API at {ApiClient.BaseUrl}.\nMake sure the server is running (python server.py).",
                "Connection error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static string Money(decimal d) => "₱" + d.ToString("N2", CultureInfo.InvariantCulture);
    static void AddCols(ListView lv, params (string, int)[] cols) { foreach (var (t, w) in cols) lv.Columns.Add(t, w); }

    static string Receipt(Booking b)
    {
        var sb = new StringBuilder();
        sb.AppendLine("STAGEPASS BOOKING SUMMARY");
        sb.AppendLine("--------------------------------------");
        sb.AppendLine($"Reference : {b.Reference}   ({b.Status})");
        sb.AppendLine($"Show      : {b.ShowTitle}");
        sb.AppendLine($"Date/Time : {b.ShowDatetime}");
        sb.AppendLine($"Venue     : {b.VenueName}");
        sb.AppendLine($"Customer  : {b.CustomerName} <{b.Email}>");
        sb.AppendLine("Seats:");
        foreach (var s in b.Seats) sb.AppendLine($"   {s.Label,-5} {s.Tier,-10} {Money(s.Price)}");
        sb.AppendLine("--------------------------------------");
        sb.AppendLine($"TOTAL     : {Money(b.Total)}");
        return sb.ToString();
    }

    // =====================================================================  BOOK TAB
    void BuildBookTab(TabPage page)
    {
        AddCols(lvShows, ("Show", 170), ("Date / Time", 115), ("Venue", 90), ("Avail.", 50));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        left.Controls.Add(new Label { Text = "Upcoming shows (select one)", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        left.Controls.Add(lvShows, 0, 1);
        left.Controls.Add(lblShowInfo, 0, 2);

        var bottom = new Panel { Dock = DockStyle.Fill };
        var legend = new FlowLayoutPanel { Left = 8, Top = 4, Width = 520, Height = 26 };
        foreach (var (c, t) in new[] { (SeatPicker.Available, "Available"), (SeatPicker.Booked, "Booked"), (SeatPicker.Selected, "Selected") })
        {
            legend.Controls.Add(new Label { BackColor = c, Width = 18, Height = 18, Margin = new Padding(6, 4, 2, 0) });
            legend.Controls.Add(new Label { Text = t, AutoSize = true, Margin = new Padding(0, 5, 10, 0) });
        }
        legend.Controls.Add(new Label { Text = "Tiers: VIP x2.0 | Orchestra x1.5 | Balcony x1.0", AutoSize = true, Margin = new Padding(10, 5, 0, 0), ForeColor = Color.DimGray });

        txtName.Location = new Point(80, 38); txtEmail.Location = new Point(80, 70); txtPhone.Location = new Point(80, 102);
        lblSummary.Location = new Point(300, 38);
        btnBook.Location = new Point(300, 98);
        btnBook.Click += async (s, e) => await Safe(ConfirmBooking);
        bottom.Controls.AddRange(new Control[]
        {
            legend,
            new Label { Text = "Name", Left = 8, Top = 41, Width = 70 }, txtName,
            new Label { Text = "Email", Left = 8, Top = 73, Width = 70 }, txtEmail,
            new Label { Text = "Phone", Left = 8, Top = 105, Width = 70 }, txtPhone,
            lblSummary, btnBook
        });

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        right.Controls.Add(picker, 0, 0);
        right.Controls.Add(bottom, 0, 1);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        page.Controls.Add(root);

        lvShows.SelectedIndexChanged += async (s, e) => await Safe(OnShowSelected);
        picker.SelectionChanged += (s, e) => UpdateSummary();
        UpdateSummary();
    }

    async Task RefreshShows()
    {
        shows = (await ApiClient.GetShows()).Where(x => x.Status == "Active").ToList();
        lvShows.Items.Clear();
        foreach (var sh in shows)
        {
            var item = new ListViewItem(sh.Title) { Tag = sh };
            item.SubItems.Add(sh.ShowDatetime);
            item.SubItems.Add(sh.VenueName);
            item.SubItems.Add((sh.Capacity - sh.SeatsSold).ToString());
            lvShows.Items.Add(item);
        }
    }

    async Task OnShowSelected()
    {
        if (lvShows.SelectedItems.Count == 0) return;
        currentShow = (ShowInfo)lvShows.SelectedItems[0].Tag;
        lblShowInfo.Text = $"{currentShow.Title}\n{currentShow.Description}\n{currentShow.ShowDatetime} @ {currentShow.VenueName}\nFrom {Money(currentShow.BasePrice)} (Balcony)";
        await LoadSeats();
    }

    async Task LoadSeats()
    {
        var map = await ApiClient.GetSeats(currentShow.Id);
        picker.LoadMap(map);
    }

    void UpdateSummary()
    {
        var sel = picker.SelectedSeats;
        lblSummary.Text = sel.Count == 0
            ? "No seats selected."
            : $"Seats: {string.Join(", ", sel.OrderBy(x => x.Row).ThenBy(x => x.Col).Select(x => x.Label))}\nTotal: {Money(sel.Sum(x => x.Price))}";
    }

    async Task ConfirmBooking()
    {
        if (currentShow == null) { MessageBox.Show("Please select a show first."); return; }
        if (picker.SelectedSeats.Count == 0) { MessageBox.Show("Please select at least one seat."); return; }
        if (string.IsNullOrWhiteSpace(txtName.Text) || string.IsNullOrWhiteSpace(txtEmail.Text))
        { MessageBox.Show("Please enter your name and email."); return; }

        var req = new BookingRequest
        {
            ShowId = currentShow.Id,
            Customer = new CustomerInfo { Name = txtName.Text.Trim(), Email = txtEmail.Text.Trim(), Phone = txtPhone.Text.Trim() },
            Seats = picker.SelectedSeats.Select(x => new SeatRef { Row = x.Row, Col = x.Col }).ToList()
        };
        try
        {
            var booking = await ApiClient.CreateBooking(req);
            MessageBox.Show(Receipt(booking), "Booking confirmed!", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            MessageBox.Show(ex.Message + "\n\nThe seat map will be refreshed.", "Seat unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        await LoadSeats();      // refresh grid either way
        await RefreshShows();
    }

    // =====================================================================  MY BOOKINGS TAB
    void BuildMyBookingsTab(TabPage page)
    {
        AddCols(lvBookings, ("Reference", 110), ("Show", 220), ("Date / Time", 130), ("Seats", 220), ("Total", 100), ("Status", 90));

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var btnFind = new Button { Text = "Find bookings", Width = 130, Height = 30 };
        top.Controls.Add(new Label { Text = "Email or booking reference:", AutoSize = true, Margin = new Padding(6, 8, 4, 0) });
        top.Controls.Add(txtSearch);
        top.Controls.Add(btnFind);

        var btnReceipt = new Button { Text = "View Summary", Width = 140, Height = 34 };
        var btnChange = new Button { Text = "Change Seats", Width = 140, Height = 34 };
        var btnCancel = new Button { Text = "Cancel Booking", Width = 140, Height = 34, ForeColor = Color.Firebrick };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.AddRange(new Control[] { btnReceipt, btnChange, btnCancel });

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(lvBookings, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        page.Controls.Add(layout);

        btnFind.Click += async (s, e) => await Safe(FindBookings);
        txtSearch.KeyDown += async (s, e) => { if (e.KeyCode == Keys.Enter) await Safe(FindBookings); };
        btnReceipt.Click += (s, e) => { var b = SelectedBooking(); if (b != null) MessageBox.Show(Receipt(b), "Booking summary"); };
        btnChange.Click += async (s, e) => await Safe(ChangeSelectedBooking);
        btnCancel.Click += async (s, e) => await Safe(CancelSelectedBooking);
    }

    Booking SelectedBooking()
    {
        if (lvBookings.SelectedItems.Count == 0) { MessageBox.Show("Select a booking first."); return null; }
        return (Booking)lvBookings.SelectedItems[0].Tag;
    }

    async Task FindBookings()
    {
        if (string.IsNullOrWhiteSpace(txtSearch.Text)) { MessageBox.Show("Enter your email address or booking reference."); return; }
        bookings = await ApiClient.FindBookings(txtSearch.Text);
        FillBookings();
        if (bookings.Count == 0) MessageBox.Show("No bookings found.");
    }

    void FillBookings()
    {
        lvBookings.Items.Clear();
        foreach (var b in bookings)
        {
            var item = new ListViewItem(b.Reference) { Tag = b };
            item.SubItems.Add(b.ShowTitle);
            item.SubItems.Add(b.ShowDatetime);
            item.SubItems.Add(b.Seats.Count == 0 ? "-" : string.Join(", ", b.Seats.Select(x => x.Label)));
            item.SubItems.Add(Money(b.Total));
            item.SubItems.Add(b.Status);
            if (b.Status == "Cancelled") item.ForeColor = Color.Gray;
            lvBookings.Items.Add(item);
        }
    }

    async Task ChangeSelectedBooking()
    {
        var b = SelectedBooking(); if (b == null) return;
        if (b.Status != "Confirmed") { MessageBox.Show("Only confirmed bookings can be changed."); return; }
        var map = await ApiClient.GetSeats(b.ShowId);
        using var dlg = new ChangeSeatsForm(b, map);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            MessageBox.Show("Seats updated.\n\n" + Receipt(dlg.Updated), "Booking updated");
            await FindBookings();
        }
    }

    async Task CancelSelectedBooking()
    {
        var b = SelectedBooking(); if (b == null) return;
        if (MessageBox.Show($"Cancel booking {b.Reference} for \"{b.ShowTitle}\"?\nThe seats will be released.", "Confirm cancellation",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        await ApiClient.CancelBooking(b.Id);
        await FindBookings();
        await RefreshShows();
    }

    // =====================================================================  ADMIN TAB
    void BuildAdminTab(TabPage page)
    {
        var sub = new TabControl { Dock = DockStyle.Fill };
        var tShows = new TabPage("Show Management");
        var tReports = new TabPage("Sales Reports");
        sub.TabPages.AddRange(new[] { tShows, tReports });
        page.Controls.Add(sub);

        // ---- show management
        AddCols(lvAdminShows, ("ID", 40), ("Title", 200), ("Date / Time", 120), ("Venue", 100), ("Price", 80), ("Sold", 60), ("Status", 80));
        var form = new Panel { Dock = DockStyle.Fill };
        var btnAdd = new Button { Text = "Add Show", Left = 100, Top = 280, Width = 140, Height = 34 };
        var btnSave = new Button { Text = "Save Changes", Left = 250, Top = 280, Width = 150, Height = 34 };
        var btnCancelShow = new Button { Text = "Cancel Show", Left = 100, Top = 322, Width = 140, Height = 34, ForeColor = Color.Firebrick };
        var btnDelete = new Button { Text = "Delete Show", Left = 250, Top = 322, Width = 150, Height = 34 };
        var btnClear = new Button { Text = "Clear Form", Left = 100, Top = 364, Width = 140, Height = 34 };
        var btnLogout = new Button { Text = "Logout", Left = 250, Top = 364, Width = 150, Height = 34 };
        lblStatus.Location = new Point(100, 410);
        txtTitle.Location = new Point(100, 15); txtDesc.Location = new Point(100, 50); cboVenue.Location = new Point(100, 130);
        dtpWhen.Location = new Point(100, 168); numPrice.Location = new Point(100, 206);
        form.Controls.AddRange(new Control[]
        {
            new Label { Text = "Title", Left = 8, Top = 18, Width = 90 }, txtTitle,
            new Label { Text = "Description", Left = 8, Top = 53, Width = 90 }, txtDesc,
            new Label { Text = "Venue", Left = 8, Top = 133, Width = 90 }, cboVenue,
            new Label { Text = "Date / Time", Left = 8, Top = 171, Width = 90 }, dtpWhen,
            new Label { Text = "Base price (₱)", Left = 8, Top = 209, Width = 90 }, numPrice,
            new Label { Text = "Balcony price; VIP = x2, Orchestra = x1.5", Left = 100, Top = 236, AutoSize = true, ForeColor = Color.DimGray },
            btnAdd, btnSave, btnCancelShow, btnDelete, btnClear, btnLogout, lblStatus
        });

        var showsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        showsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        showsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        showsLayout.Controls.Add(lvAdminShows, 0, 0);
        showsLayout.Controls.Add(form, 1, 0);
        tShows.Controls.Add(showsLayout);

        lvAdminShows.SelectedIndexChanged += (s, e) => OnAdminShowSelected();
        btnAdd.Click += async (s, e) => await Safe(AddShow);
        btnSave.Click += async (s, e) => await Safe(SaveShow);
        btnCancelShow.Click += async (s, e) => await Safe(CancelShow);
        btnDelete.Click += async (s, e) => await Safe(DeleteShow);
        btnClear.Click += (s, e) => ClearShowForm();
        btnLogout.Click += (s, e) => { ApiClient.Logout(); tabs.SelectedIndex = 0; };

        // ---- reports
        AddCols(lvReport, ("Show", 230), ("Date / Time", 130), ("Status", 80), ("Tickets sold", 100), ("Capacity", 80), ("Occupancy %", 100), ("Revenue", 120), ("Upcoming", 80));
        var btnRefresh = new Button { Text = "Refresh", Width = 100, Height = 30, Dock = DockStyle.Left };
        var rpt = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        rpt.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        rpt.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rpt.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        rpt.Controls.Add(lblKpi, 0, 0);
        rpt.Controls.Add(lvReport, 0, 1);
        rpt.Controls.Add(btnRefresh, 0, 2);
        tReports.Controls.Add(rpt);
        btnRefresh.Click += async (s, e) => await Safe(LoadReport);
        sub.SelectedIndexChanged += async (s, e) => { if (sub.SelectedTab == tReports) await Safe(LoadReport); };
    }

    async Task OnTabChanged()
    {
        if (tabs.SelectedIndex == 2)
        {
            if (!ApiClient.IsAdmin)
            {
                using var dlg = new AdminLoginForm();
                if (dlg.ShowDialog(this) != DialogResult.OK) { tabs.SelectedIndex = previousTab; return; }
            }
            await Safe(LoadAdminData);
        }
        else
        {
            previousTab = tabs.SelectedIndex;
            if (tabs.SelectedIndex == 0) await Safe(RefreshShows);
        }
    }

    async Task LoadAdminData()
    {
        var venues = await ApiClient.GetVenues();
        cboVenue.DataSource = venues;
        cboVenue.DisplayMember = "Name";
        await RefreshAdminShows();
    }

    async Task RefreshAdminShows()
    {
        adminShows = await ApiClient.GetShows();
        lvAdminShows.Items.Clear();
        foreach (var sh in adminShows)
        {
            var item = new ListViewItem(sh.Id.ToString()) { Tag = sh };
            item.SubItems.Add(sh.Title);
            item.SubItems.Add(sh.ShowDatetime);
            item.SubItems.Add(sh.VenueName);
            item.SubItems.Add(Money(sh.BasePrice));
            item.SubItems.Add($"{sh.SeatsSold}/{sh.Capacity}");
            item.SubItems.Add(sh.Status);
            if (sh.Status == "Cancelled") item.ForeColor = Color.Gray;
            lvAdminShows.Items.Add(item);
        }
        selectedAdminShow = null;
    }

    void OnAdminShowSelected()
    {
        if (lvAdminShows.SelectedItems.Count == 0) return;
        var sh = (ShowInfo)lvAdminShows.SelectedItems[0].Tag;
        selectedAdminShow = sh;
        txtTitle.Text = sh.Title; txtDesc.Text = sh.Description;
        foreach (Venue v in cboVenue.Items) if (v.Id == sh.VenueId) cboVenue.SelectedItem = v;
        dtpWhen.Value = DateTime.ParseExact(sh.ShowDatetime, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        numPrice.Value = Math.Max(numPrice.Minimum, Math.Min(numPrice.Maximum, sh.BasePrice));
        lblStatus.Text = $"Selected show #{sh.Id} - {sh.Status}";
    }

    ShowInput ReadShowForm(string status = "Active")
    {
        if (string.IsNullOrWhiteSpace(txtTitle.Text)) throw new ApiException("Title is required.", 400);
        if (cboVenue.SelectedItem == null) throw new ApiException("Select a venue.", 400);
        return new ShowInput
        {
            Title = txtTitle.Text.Trim(),
            Description = txtDesc.Text.Trim(),
            VenueId = ((Venue)cboVenue.SelectedItem).Id,
            ShowDatetime = dtpWhen.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            BasePrice = numPrice.Value,
            Status = status
        };
    }

    void ClearShowForm()
    {
        lvAdminShows.SelectedItems.Clear();
        selectedAdminShow = null;
        txtTitle.Clear(); txtDesc.Clear(); numPrice.Value = 500; dtpWhen.Value = DateTime.Now.AddDays(7);
        lblStatus.Text = "";
    }

    async Task AddShow()
    {
        await ApiClient.CreateShow(ReadShowForm());
        lblStatus.Text = "Show added.";
        await RefreshAdminShows();
    }

    async Task SaveShow()
    {
        if (selectedAdminShow == null) { MessageBox.Show("Select a show to update (or reschedule) first."); return; }
        await ApiClient.UpdateShow(selectedAdminShow.Id, ReadShowForm(selectedAdminShow.Status));
        lblStatus.Text = "Show updated.";
        await RefreshAdminShows();
    }

    async Task CancelShow()
    {
        if (selectedAdminShow == null) { MessageBox.Show("Select a show first."); return; }
        if (MessageBox.Show($"Cancel \"{selectedAdminShow.Title}\"?\nAll its bookings will be cancelled and seats released.",
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await ApiClient.UpdateShow(selectedAdminShow.Id, ReadShowForm("Cancelled"));
        lblStatus.Text = "Show cancelled.";
        await RefreshAdminShows();
    }

    async Task DeleteShow()
    {
        if (selectedAdminShow == null) { MessageBox.Show("Select a show first."); return; }
        if (MessageBox.Show($"Permanently delete \"{selectedAdminShow.Title}\"?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        await ApiClient.DeleteShow(selectedAdminShow.Id);
        lblStatus.Text = "Show deleted.";
        ClearShowForm();
        await RefreshAdminShows();
    }

    async Task LoadReport()
    {
        var r = await ApiClient.GetReport();
        lblKpi.Text = $"Tickets sold: {r.TotalTicketsSold}     Revenue: {Money(r.TotalRevenue)}     " +
                      $"Confirmed bookings: {r.ConfirmedBookings}     Cancelled: {r.CancelledBookings}     Upcoming shows: {r.UpcomingShows}";
        lvReport.Items.Clear();
        foreach (var p in r.PerShow)
        {
            var item = new ListViewItem(p.Title);
            item.SubItems.Add(p.ShowDatetime);
            item.SubItems.Add(p.Status);
            item.SubItems.Add(p.TicketsSold.ToString());
            item.SubItems.Add(p.Capacity.ToString());
            item.SubItems.Add(p.OccupancyPct.ToString("0.0") + "%");
            item.SubItems.Add(Money(p.Revenue));
            item.SubItems.Add(p.Upcoming ? "Yes" : "No");
            lvReport.Items.Add(item);
        }
    }
}

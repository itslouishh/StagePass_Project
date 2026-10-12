using System.Drawing;

namespace StagePass.Client;

/// <summary>Visual seating grid. Green = available, Red = booked, Yellow = selected.</summary>
public class SeatPicker : Panel
{
    public static readonly Color Available = Color.FromArgb(46, 204, 113);
    public static readonly Color Booked = Color.FromArgb(231, 76, 60);
    public static readonly Color Selected = Color.FromArgb(241, 196, 15);

    public int MaxSeats { get; set; } = 8;
    public event EventHandler SelectionChanged;

    readonly List<Seat> _selected = new();
    readonly ToolTip _tip = new();
    public IReadOnlyList<Seat> SelectedSeats => _selected;

    public SeatPicker()
    {
        AutoScroll = true;
        BackColor = Color.White;
    }

    public void Clear()
    {
        Controls.Clear();
        _selected.Clear();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <param name="ownSeats">Labels of seats that belong to the booking being edited (shown as selected, not booked).</param>
    public void LoadMap(SeatMap map, ICollection<string> ownSeats = null)
    {
        SuspendLayout();
        Controls.Clear();
        _selected.Clear();

        const int w = 40, h = 32, gap = 4, left = 36, top = 52;
        Controls.Add(new Label
        {
            Text = "S T A G E",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(44, 62, 80),
            ForeColor = Color.White,
            Left = left, Top = 8, Height = 28,
            Width = map.Cols * (w + gap) - gap
        });

        for (int r = 1; r <= map.Rows; r++)
        {
            Controls.Add(new Label
            {
                Text = ((char)('A' + r - 1)).ToString(),
                Left = 8, Top = top + (r - 1) * (h + gap), Width = 24, Height = h,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            });
        }

        foreach (var seat in map.Seats)
        {
            bool own = ownSeats != null && ownSeats.Contains(seat.Label);
            bool booked = seat.Status == "Booked" && !own;
            var btn = new Button
            {
                Text = seat.Label,
                Tag = seat,
                Width = w, Height = h,
                Left = left + (seat.Col - 1) * (w + gap),
                Top = top + (seat.Row - 1) * (h + gap),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                Padding = Padding.Empty,
                BackColor = booked ? Booked : Available,
                ForeColor = Color.White,
                Cursor = booked ? Cursors.No : Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            _tip.SetToolTip(btn, $"{seat.Label}  |  {seat.Tier}  |  ₱{seat.Price:N2}" + (booked ? "  |  BOOKED" : ""));
            btn.Click += (s, e) => OnSeatClick(btn, seat, booked);
            Controls.Add(btn);
            if (own) { btn.BackColor = Selected; btn.ForeColor = Color.Black; _selected.Add(seat); }
        }
        ResumeLayout();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnSeatClick(Button btn, Seat seat, bool booked)
    {
        if (booked) return;
        var existing = _selected.FirstOrDefault(x => x.Label == seat.Label);
        if (existing != null)
        {
            _selected.Remove(existing);
            btn.BackColor = Available; btn.ForeColor = Color.White;
        }
        else
        {
            if (_selected.Count >= MaxSeats)
            {
                MessageBox.Show($"You can select at most {MaxSeats} seats per booking.", "StagePass", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            _selected.Add(seat);
            btn.BackColor = Selected; btn.ForeColor = Color.Black;
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}

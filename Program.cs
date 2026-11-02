using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

Db.ConnStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new Exception("Connection string 'Default' is missing in appsettings.json");

// ---------- Friendly JSON errors ----------
app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    string msg; int code = 500;
    if (ex is MySqlException me && (me.Number == 1451 || me.Number == 1217))
    { code = 409; msg = "This record is still used by other records, so it cannot be deleted."; }
    else if (ex is MySqlException me2 && (me2.Number == 1452 || me2.Number == 3819 || me2.Number == 4025))
    { code = 400; msg = "Invalid data (unknown student/plan or a value that is not allowed)."; }
    else if (ex is MySqlException me3 && me3.Number == 1062)
    { code = 409; msg = "This record already exists."; }
    else if (ex is ArgumentException) { code = 400; msg = ex.Message; }
    else msg = "Server error: " + (ex?.Message ?? "unknown");
    ctx.Response.StatusCode = code;
    await ctx.Response.WriteAsJsonAsync(new { error = msg });
}));

app.UseDefaultFiles();
app.UseStaticFiles();

static void Need(bool ok, string msg) { if (!ok) throw new ArgumentException(msg); }
static object? N(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

var api = app.MapGroup("/api");

// ================= Dashboard =================
api.MapGet("/stats", async () => (await Db.Query(@"
    SELECT (SELECT COUNT(*) FROM Students)                                   AS students,
           (SELECT COUNT(*) FROM MealPlans)                                  AS plans,
           (SELECT COUNT(*) FROM MealAttendance
              WHERE date = CURDATE() AND status = 'Present')                 AS mealsToday,
           (SELECT COUNT(*) FROM LaundryOrders WHERE status <> 'Returned')   AS openLaundry,
           (SELECT COALESCE(SUM(amount),0) FROM Payments
              WHERE month = DATE_FORMAT(CURDATE(), '%Y-%m'))                 AS collectedThisMonth")).First());

// ================= Meal plans =================
api.MapGet("/plans", async () => await Db.Query(
    @"SELECT p.plan_id AS planId, p.plan_name AS planName, p.monthly_fee AS monthlyFee,
             COUNT(s.student_id) AS students
      FROM MealPlans p LEFT JOIN Students s ON s.plan_id = p.plan_id
      GROUP BY p.plan_id, p.plan_name, p.monthly_fee ORDER BY p.plan_id"));

api.MapPost("/plans", async (PlanIn p) =>
{
    Need(!string.IsNullOrWhiteSpace(p.PlanName), "Plan name is required.");
    Need(p.MonthlyFee >= 0, "Monthly fee must be 0 or more.");
    await Db.Exec("INSERT INTO MealPlans (plan_name, monthly_fee) VALUES (@n, @f)",
        ("@n", p.PlanName!.Trim()), ("@f", p.MonthlyFee));
    return Results.Ok(new { ok = true });
});

api.MapPut("/plans/{id:int}", async (int id, PlanIn p) =>
{
    Need(!string.IsNullOrWhiteSpace(p.PlanName), "Plan name is required.");
    Need(p.MonthlyFee >= 0, "Monthly fee must be 0 or more.");
    await Db.Exec("UPDATE MealPlans SET plan_name=@n, monthly_fee=@f WHERE plan_id=@id",
        ("@n", p.PlanName!.Trim()), ("@f", p.MonthlyFee), ("@id", id));
    return Results.Ok(new { ok = true });
});

api.MapDelete("/plans/{id:int}", async (int id) =>
{
    await Db.Exec("DELETE FROM MealPlans WHERE plan_id=@id", ("@id", id));
    return Results.Ok(new { ok = true });
});

// ================= Students =================
api.MapGet("/students", async (string? search) => await Db.Query(
    @"SELECT s.student_id AS studentId, s.name, s.room_no AS roomNo, s.phone, s.email,
             s.plan_id AS planId, p.plan_name AS planName
      FROM Students s JOIN MealPlans p ON p.plan_id = s.plan_id
      WHERE @q IS NULL OR s.name LIKE CONCAT('%', @q, '%')
         OR s.room_no LIKE CONCAT('%', @q, '%') OR s.phone LIKE CONCAT('%', @q, '%')
      ORDER BY s.name",
    ("@q", N(search))));

api.MapPost("/students", async (StudentIn s) =>
{
    Need(!string.IsNullOrWhiteSpace(s.Name), "Name is required.");
    Need(!string.IsNullOrWhiteSpace(s.RoomNo), "Room number is required.");
    Need(s.PlanId > 0, "Please choose a meal plan.");
    await Db.Exec(@"INSERT INTO Students (name, room_no, phone, email, plan_id)
                    VALUES (@n, @r, @p, @e, @pl)",
        ("@n", s.Name!.Trim()), ("@r", s.RoomNo!.Trim()), ("@p", N(s.Phone)), ("@e", N(s.Email)), ("@pl", s.PlanId));
    return Results.Ok(new { ok = true });
});

api.MapPut("/students/{id:int}", async (int id, StudentIn s) =>
{
    Need(!string.IsNullOrWhiteSpace(s.Name), "Name is required.");
    Need(!string.IsNullOrWhiteSpace(s.RoomNo), "Room number is required.");
    Need(s.PlanId > 0, "Please choose a meal plan.");
    await Db.Exec(@"UPDATE Students SET name=@n, room_no=@r, phone=@p, email=@e, plan_id=@pl
                    WHERE student_id=@id",
        ("@n", s.Name!.Trim()), ("@r", s.RoomNo!.Trim()), ("@p", N(s.Phone)), ("@e", N(s.Email)),
        ("@pl", s.PlanId), ("@id", id));
    return Results.Ok(new { ok = true });
});

api.MapDelete("/students/{id:int}", async (int id) =>
{
    await Db.Exec("DELETE FROM Students WHERE student_id=@id", ("@id", id)); // children cascade
    return Results.Ok(new { ok = true });
});

// ================= Meal attendance =================
api.MapGet("/attendance", async (string date) =>
{
    Need(!string.IsNullOrWhiteSpace(date), "Date is required.");
    return await Db.Query(
        @"SELECT student_id AS studentId, meal_type AS mealType, status
          FROM MealAttendance WHERE date = @d", ("@d", date));
});

api.MapPost("/attendance", async (AttendanceIn a) =>
{
    Need(a.StudentId > 0, "Student is required.");
    Need(!string.IsNullOrWhiteSpace(a.Date), "Date is required.");
    Need(a.MealType is "Breakfast" or "Lunch" or "Dinner", "Meal must be Breakfast, Lunch or Dinner.");
    if (a.Status is null or "")      // cleared -> remove the record
    {
        await Db.Exec("DELETE FROM MealAttendance WHERE student_id=@s AND date=@d AND meal_type=@m",
            ("@s", a.StudentId), ("@d", a.Date), ("@m", a.MealType));
    }
    else
    {
        Need(a.Status is "Present" or "Absent", "Status must be Present or Absent.");
        await Db.Exec(@"INSERT INTO MealAttendance (student_id, date, meal_type, status)
                        VALUES (@s, @d, @m, @st)
                        ON DUPLICATE KEY UPDATE status = VALUES(status)",
            ("@s", a.StudentId), ("@d", a.Date), ("@m", a.MealType), ("@st", a.Status));
    }
    return Results.Ok(new { ok = true });
});

// ================= Laundry =================
api.MapGet("/laundry", async (string? status) => await Db.Query(
    @"SELECT l.order_id AS orderId, l.student_id AS studentId, s.name AS studentName, s.room_no AS roomNo,
             DATE_FORMAT(l.date_given, '%Y-%m-%d')    AS dateGiven,
             DATE_FORMAT(l.date_returned, '%Y-%m-%d') AS dateReturned,
             l.total_clothes AS totalClothes, l.status
      FROM LaundryOrders l JOIN Students s ON s.student_id = l.student_id
      WHERE @st IS NULL OR l.status = @st
      ORDER BY l.order_id DESC", ("@st", N(status))));

api.MapPost("/laundry", async (LaundryIn l) =>
{
    Need(l.StudentId > 0, "Student is required.");
    Need(!string.IsNullOrWhiteSpace(l.DateGiven), "Date given is required.");
    Need(l.TotalClothes > 0, "Number of clothes must be at least 1.");
    await Db.Exec(@"INSERT INTO LaundryOrders (student_id, date_given, total_clothes, status)
                    VALUES (@s, @d, @c, 'Pending')",
        ("@s", l.StudentId), ("@d", l.DateGiven), ("@c", l.TotalClothes));
    return Results.Ok(new { ok = true });
});

api.MapPut("/laundry/{id:int}/status", async (int id, StatusIn s) =>
{
    Need(s.Status is "Pending" or "Washing" or "Returned", "Status must be Pending, Washing or Returned.");
    await Db.Exec(@"UPDATE LaundryOrders
                    SET status = @st,
                        date_returned = CASE WHEN @st = 'Returned' THEN CURDATE() ELSE NULL END
                    WHERE order_id = @id", ("@st", s.Status), ("@id", id));
    return Results.Ok(new { ok = true });
});

api.MapDelete("/laundry/{id:int}", async (int id) =>
{
    await Db.Exec("DELETE FROM LaundryOrders WHERE order_id=@id", ("@id", id));
    return Results.Ok(new { ok = true });
});

// ================= Payments =================
api.MapGet("/payments", async (string? month) => await Db.Query(
    @"SELECT p.payment_id AS paymentId, p.student_id AS studentId, s.name AS studentName,
             p.amount, p.month, DATE_FORMAT(p.payment_date, '%Y-%m-%d') AS paymentDate
      FROM Payments p JOIN Students s ON s.student_id = p.student_id
      WHERE @m IS NULL OR p.month = @m
      ORDER BY p.payment_id DESC", ("@m", N(month))));

api.MapPost("/payments", async (PaymentIn p) =>
{
    Need(p.StudentId > 0, "Student is required.");
    Need(p.Amount > 0, "Amount must be greater than 0.");
    Need(!string.IsNullOrWhiteSpace(p.Month), "Month is required.");
    Need(!string.IsNullOrWhiteSpace(p.PaymentDate), "Payment date is required.");
    await Db.Exec(@"INSERT INTO Payments (student_id, amount, month, payment_date)
                    VALUES (@s, @a, @m, @d)",
        ("@s", p.StudentId), ("@a", p.Amount), ("@m", p.Month!.Trim()), ("@d", p.PaymentDate));
    return Results.Ok(new { ok = true });
});

api.MapDelete("/payments/{id:int}", async (int id) =>
{
    await Db.Exec("DELETE FROM Payments WHERE payment_id=@id", ("@id", id));
    return Results.Ok(new { ok = true });
});

// ================= Monthly bill report (calculated from the tables) =================
api.MapGet("/bill", async (string month) =>
{
    Need(System.Text.RegularExpressions.Regex.IsMatch(month ?? "", @"^\d{4}-\d{2}$"), "Month must look like 2026-10.");
    return await Db.Query(
        @"SELECT s.student_id AS studentId, s.name, s.room_no AS roomNo, p.plan_name AS planName,
                 p.monthly_fee AS fee,
                 (SELECT COUNT(*) FROM MealAttendance a
                   WHERE a.student_id = s.student_id AND a.status = 'Present'
                     AND DATE_FORMAT(a.date, '%Y-%m') = @m)                          AS mealsEaten,
                 (SELECT COALESCE(SUM(pay.amount), 0) FROM Payments pay
                   WHERE pay.student_id = s.student_id AND pay.month = @m)           AS paid,
                 p.monthly_fee - (SELECT COALESCE(SUM(pay.amount), 0) FROM Payments pay
                   WHERE pay.student_id = s.student_id AND pay.month = @m)           AS balance
          FROM Students s JOIN MealPlans p ON p.plan_id = s.plan_id
          ORDER BY s.name", ("@m", month));
});

app.Run();

// ---------- Request models ----------
record PlanIn(string? PlanName, decimal MonthlyFee);
record StudentIn(string? Name, string? RoomNo, string? Phone, string? Email, int PlanId);
record AttendanceIn(int StudentId, string? Date, string? MealType, string? Status);
record LaundryIn(int StudentId, string? DateGiven, int TotalClothes);
record StatusIn(string? Status);
record PaymentIn(int StudentId, decimal Amount, string? Month, string? PaymentDate);

// ---------- Tiny MySQL helper ----------
static class Db
{
    public static string ConnStr = "";

    public static async Task<List<Dictionary<string, object?>>> Query(string sql, params (string, object?)[] ps)
    {
        await using var con = new MySqlConnection(ConnStr);
        await con.OpenAsync();
        await using var cmd = new MySqlCommand(sql, con);
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await r.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++)
                row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public static async Task Exec(string sql, params (string, object?)[] ps)
    {
        await using var con = new MySqlConnection(ConnStr);
        await con.OpenAsync();
        await using var cmd = new MySqlCommand(sql, con);
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}

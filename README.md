# 🏠 Hostel Mess and Laundry Management System

A web-based **Database Management System (DBMS)** project that replaces paper registers in a hostel with a central **MySQL** database. It manages students, meal plans, daily meal attendance, laundry orders and monthly payments in one place.

**Tech stack:** HTML · CSS · JavaScript (front-end) | C# ASP.NET Core (back-end) | MySQL (database)

---

## 📌 Problem

In many hostels, mess and laundry records are still kept in paper registers. Records get lost or written wrongly, the monthly mess bill takes a long time to calculate, students' clothes get confused in laundry, and the manager cannot easily see who has paid.

## ✨ Features

| Module | What it does |
|---|---|
| **Student Management** | Add, update, delete and search hostel students |
| **Meal Plan Management** | Create meal plans with monthly fees |
| **Meal Attendance** | Mark daily Breakfast, Lunch and Dinner as Present/Absent (auto-saved) |
| **Laundry Management** | Track orders from *Pending → Washing → Returned*; return date is set automatically |
| **Payments & Billing** | Record payments and view a printable monthly bill report (fee, paid, balance, status) |
| **Dashboard** | Totals for students, plans, today's meals, open laundry and this month's collection |

## 🗄️ Database Design

Five tables, all relationships are **one-to-many**:

```
MealPlans (PK plan_id)
   └── Students (PK student_id, FK plan_id)
          ├── MealAttendance (PK attendance_id, FK student_id)
          ├── LaundryOrders  (PK order_id,      FK student_id)
          └── Payments       (PK payment_id,    FK student_id)
```

| Table | Main columns |
|---|---|
| `MealPlans` | plan_id, plan_name, monthly_fee |
| `Students` | student_id, name, room_no, phone, email, plan_id |
| `MealAttendance` | attendance_id, student_id, date, meal_type, status |
| `LaundryOrders` | order_id, student_id, date_given, date_returned, total_clothes, status |
| `Payments` | payment_id, student_id, amount, month, payment_date |

Extras: `UNIQUE(student_id, date, meal_type)` on attendance, `CHECK` constraints, and `ON DELETE CASCADE` so a deleted student's records are removed too.

**Bill calculation:** `balance = plan monthly_fee − payments made for that month` (month stored as `YYYY-MM`).

## 🚀 Getting Started

### Requirements
- [.NET SDK](https://dotnet.microsoft.com/download) (project targets `net10.0`)
- [MySQL Server](https://dev.mysql.com/downloads/) (MySQL Workbench optional)

### Setup
1. **Clone the repo**
   ```bash
   git clone https://github.com/<your-username>/HostelMessLaundry.git
   cd HostelMessLaundry
   ```
2. **Create the database** (also inserts sample data)
   ```bash
   mysql -u root -p < database/schema.sql
   ```
   Or open `database/schema.sql` in MySQL Workbench and run it.
3. **Configure the connection** – copy `appsettings.example.json` to `appsettings.json` and put your MySQL password in the connection string.
4. **Run**
   ```bash
   dotnet run
   ```
5. Open **http://localhost:5000** in your browser.

## 📂 Project Structure

```
HostelMessLaundry/
├── database/schema.sql        # tables, constraints, sample data
├── wwwroot/                   # front-end
│   ├── index.html
│   ├── style.css
│   └── app.js
├── Program.cs                 # C# back-end (REST API + MySQL access)
├── HostelMessLaundry.csproj
└── appsettings.example.json   # connection string template
```

## 🔌 API Overview

`/api/students` · `/api/plans` · `/api/attendance` · `/api/laundry` · `/api/payments` · `/api/bill?month=YYYY-MM` · `/api/stats`

## 🧪 Sample SQL

```sql
-- Monthly bill per student
SELECT s.name, p.monthly_fee, COALESCE(SUM(pay.amount), 0) AS paid
FROM Students s
JOIN MealPlans p ON p.plan_id = s.plan_id
LEFT JOIN Payments pay ON pay.student_id = s.student_id AND pay.month = '2026-10'
GROUP BY s.student_id, s.name, p.monthly_fee;

-- Laundry not yet returned
SELECT s.name, l.total_clothes, l.status
FROM LaundryOrders l JOIN Students s USING (student_id)
WHERE l.status <> 'Returned';
```

## 👩‍💻 Author

**Aena Arshad** 

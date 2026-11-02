# Hostel Mess and Laundry Management System
DBMS project — Front-end: HTML, CSS, JavaScript | Back-End: C# (ASP.NET Core) | Database: MySQL

## Run (3 steps)
1. Install **.NET SDK (10)** and **MySQL Server** (Workbench optional).
2. Create the database + sample data:  `mysql -u root -p < database/schema.sql`
   (or open `database/schema.sql` in MySQL Workbench and run it).
3. Put your MySQL password in `appsettings.json`, then:
   ```
   dotnet run
   ```
   Open **http://localhost:5000**

## Modules (from the proposal)
Student Management · Meal Plan Management · Meal Attendance (breakfast/lunch/dinner) ·
Laundry Management (Pending → Washing → Returned, return date auto-set) · Payments & Billing (monthly bill report, printable).

## Database (5 tables)
MealPlans(PK plan_id) ← Students(PK student_id, FK plan_id) ← MealAttendance, LaundryOrders, Payments (FK student_id). All relationships are One-to-Many.
Extra rules added: UNIQUE(student_id,date,meal_type) on attendance, CHECK constraints, ON DELETE CASCADE for a student's records.
Bill = plan monthly_fee − payments for that month (stored as YYYY-MM).

## Useful SQL for your viva
```sql
-- Monthly bill
SELECT s.name, p.monthly_fee, COALESCE(SUM(pay.amount),0) AS paid
FROM Students s JOIN MealPlans p ON p.plan_id=s.plan_id
LEFT JOIN Payments pay ON pay.student_id=s.student_id AND pay.month='2026-10'
GROUP BY s.student_id, s.name, p.monthly_fee;
-- Unreturned laundry
SELECT s.name, l.total_clothes, l.status FROM LaundryOrders l JOIN Students s USING(student_id) WHERE l.status<>'Returned';
```

# REQ-SQL-2: Rename the order amount column

Rename `amount` to `amount_cents` on `orders`, so the unit is in the name. Every existing order
keeps its amount under the new name. `queries/report.sql` must keep working. The migration must be
reversible.

-- Revert REQ-SQL-1: stop defaulting new rows to a zero discount.
ALTER TABLE orders ALTER COLUMN discount_cents DROP DEFAULT;

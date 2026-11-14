# REQ-SQL-1: Add a discount column to orders

Add a nullable `discount_cents` column to `orders`, default 0. Existing rows keep their totals:
no order loses or changes its `amount`. The migration must be reversible: the down migration
returns the schema to exactly what it was before.

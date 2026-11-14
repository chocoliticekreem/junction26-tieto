CREATE TABLE orders (
    id         serial PRIMARY KEY,
    customer   text    NOT NULL,
    amount     integer NOT NULL,
    created_at date    NOT NULL
);

CREATE INDEX orders_customer_idx ON orders (customer);

INSERT INTO orders (customer, amount, created_at) VALUES
    ('aurora-oy',   12900, '2026-10-01'),
    ('aurora-oy',    4500, '2026-10-03'),
    ('borealis-ab', 89900, '2026-10-04'),
    ('cirrus-gmbh',   990, '2026-10-07'),
    ('cirrus-gmbh', 23050, '2026-10-09');

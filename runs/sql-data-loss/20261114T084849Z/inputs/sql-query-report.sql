SELECT customer, count(*) AS orders, sum(amount_cents) AS total_cents
FROM orders
GROUP BY customer
ORDER BY customer;

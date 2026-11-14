
SELECT 'COLUMN', table_name || '.' || column_name, data_type, is_nullable,
       coalesce(column_default, '')
FROM information_schema.columns WHERE table_schema = 'public' ORDER BY 2;
SELECT 'CONSTRAINT', table_name, constraint_name, constraint_type
FROM information_schema.table_constraints
WHERE constraint_schema = 'public' AND constraint_name NOT LIKE '%\_not\_null' ORDER BY 2, 3;
SELECT 'INDEX', tablename, indexname, indexdef
FROM pg_indexes WHERE schemaname = 'public' ORDER BY 2, 3;
SELECT 'ROWS', t.table_name, (xpath('/row/c/text()', query_to_xml(
         format('SELECT count(*) AS c FROM public.%I', t.table_name), false, true, '')))[1]::text
FROM information_schema.tables t
WHERE t.table_schema = 'public' AND t.table_type = 'BASE TABLE' ORDER BY 2;
SELECT 'VALUES', c.table_name || '.' || c.column_name, (xpath('/row/h/text()', query_to_xml(
         format('SELECT md5(coalesce(string_agg(%1$I::text, %2$L ORDER BY %1$I::text), %3$L))'
                ' || %4$L || count(%1$I) AS h FROM public.%5$I',
                c.column_name, '|', '', ':', c.table_name), false, true, '')))[1]::text
FROM information_schema.columns c
JOIN information_schema.tables t
  ON t.table_schema = c.table_schema AND t.table_name = c.table_name
WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE' ORDER BY 2;

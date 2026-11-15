import openpyxl as x,sys,collections
for f in sys.argv[1:]:
 w=x.load_workbook(f)
 print(f)
 for s in w:
  rows=[(r[0].row,' | '.join(f'{c.column_letter}:{str(c.value)[:160]}' for c in r if c.value is not None)) for r in s.iter_rows() if any(c.value is not None for c in r)]
  print(s.title,s.max_row,s.max_column,len(rows), 'merges',len(s.merged_cells.ranges))
  for n,r in (rows[:32] if len(w.worksheets)<12 else rows[:5]): print(n,r[:420])

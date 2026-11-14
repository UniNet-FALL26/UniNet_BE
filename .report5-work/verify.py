import sys,openpyxl as x,collections
p=sys.argv[1]
w=x.load_workbook(p)
v=x.load_workbook(p,data_only=True)
assert len(w.sheetnames)==10
counts=[]
ids=[]
for s in w.worksheets[3:]:
 rr=[r for r in s.iter_rows(min_row=11,max_col=15) if str(r[0].value).startswith('TC_')]
 for r in rr:
  assert all(c.value for c in r[:5])
  assert [r[i].value for i in [5,8,11]]==['Pending']*3
  assert all(r[i].value is None for i in [6,7,9,10,12,13])
  ids.append(r[0].value)
 counts.append(len(rr))
 assert v[s.title]['B4'].value==len(rr)
 for n in (6,7,8): assert v[s.title][f'D{n}'].value==len(rr)
assert len(set(ids))==len(ids)==62
assert [v['Test Statistics'][f'{c}18'].value for c in 'DEFGH']==[0,0,62,0,62]
assert all(c.data_type!='e' and '#REF!' not in str(c.value) for s in w for row in s.iter_rows() for c in row)
print('Verified', counts, 'total',sum(counts), 'and all three rounds.')

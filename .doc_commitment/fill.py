from docx import Document
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from copy import deepcopy
from zipfile import ZipFile
from pathlib import Path
import hashlib, json

base=Path(r'D:\SEP490\UniNet_BE\.doc_commitment')
src=Path(r'D:\SWP490_R2_BM04_Review2_Commitment_1.docx')
out=base/'SWP490_FA26SE117_R2_Commitment_Filled.docx'
d=Document(src); ts=list(d.tables)
names=['Nguyễn Phước Thịnh','Trương Tấn Đạt','Lê Tấn Kiệt','Huỳnh Thanh Vinh','Trịnh Trọng Quyền']
short=['Thịnh','Đạt','Kiệt','Vinh','Quyền']
def cell(c,text):
    p=c.paragraphs[0]
    rpr=deepcopy(p.runs[0]._r.rPr) if p.runs and p.runs[0]._r.rPr is not None else None
    for ch in list(p._p):
        if ch.tag!=qn('w:pPr'): p._p.remove(ch)
    for extra in c.paragraphs[1:]: c._tc.remove(extra._p)
    r=p.add_run(str(text))
    if rpr is not None: r._r.insert(0,rpr)
def row(t,i,vals):
    for c,v in zip(t.rows[i].cells,vals): cell(c,v)
def matrix(t,rows):
    while len(t.rows)<len(rows)+1: t._tbl.append(deepcopy(t.rows[-1]._tr))
    while len(t.rows)>len(rows)+1: t._tbl.remove(t.rows[-1]._tr)
    for i,vals in enumerate(rows,1): row(t,i,vals)
def kv(t,vals):
    for r,v in zip(t.rows,vals): cell(r.cells[1],v)

kv(ts[0],['FA26SE117','UniNet—An Integrated Collaboration and Career Networking Platform for University Students','; '.join(names),'Nguyễn Trọng Tài; Huỳnh Văn Thống','Chờ xác nhận','Chờ xác nhận','Chờ xác nhận','1.0','01/11/2026'])
kv(ts[1],['Bản đăng ký điều chỉnh theo BM01 v2.0; chờ xác nhận bản cuối','Chờ xác nhận','Chờ xác nhận tên file đăng ký cuối'])
features=[
('F-01','Tạo dự án và tuyển thành viên','Student, Moderator','Tạo dự án hợp lệ; duyệt theo quyền; lời mời được chấp nhận/từ chối; chặn thao tác trái quyền.','WF-01','Thịnh'),
('F-02','Workspace, task evidence và peer review','Student members','Thành viên nộp evidence; dự án hoàn tất mở đánh giá đóng góp; người ngoài không truy cập dữ liệu riêng.','WF-01','Thịnh; Đạt hỗ trợ'),
('F-03A','Điều kiện L1, chọn chuyên gia, đặt lịch và thanh toán','Student Leader, Verificator','L1 đạt và lĩnh vực khớp; giữ đúng người; không đặt trùng; payment hợp lệ tạo đúng một assignment; không tự đổi chuyên gia.','WF-02','Đạt'),
('F-03B','Meeting và report thẩm định','Student Leader, Verificator','Chỉ người liên quan vào phiên; report sau khi meeting kết thúc; ghi Verified/Failed; report hợp lệ hoàn tất dịch vụ.','WF-03','Thịnh'),
('F-03C','Đánh giá chất lượng sau phiên','Student Leader','Người đặt đánh giá 1–5 sao một lần sau meeting và report; chặn trùng/trái quyền; không tác động kết quả chuyên môn.','WF-03','Thịnh'),
('F-04','Ứng tuyển và xử lý hồ sơ','Student, Company','Student gửi đơn; Company sở hữu tin đổi trạng thái; Student xem trạng thái mới; chặn trái quyền.','WF-04','Kiệt'),
('F-05','Community và quảng cáo','Student, Company, Moderator','Bài đăng được kiểm duyệt; quảng cáo thanh toán và phê duyệt đúng trạng thái; không xuất bản chiến dịch chưa đủ điều kiện.','WF-05','Vinh'),
('F-06','Quản trị và xét duyệt','Admin, Moderator, Verificator','Quyền theo vai trò; Admin tạo/kích hoạt chuyên gia ngoài hệ thống và xác nhận lĩnh vực; quyết định có audit; không Admin assign booking mới.','WF-06','Quyền'),
('F-07','Career và Company profile','Student, Company','Chủ hồ sơ cập nhật; người khác bị từ chối; dữ liệu career/profile hỗ trợ ứng tuyển.','WF-04','Kiệt')]
matrix(ts[2],features)
nfr=[('NFR-01','Performance','API read p95','≤ 2 giây; 50 người dùng đồng thời','Load test common reads','Thịnh'),('NFR-02','Security','Tỷ lệ chặn trái quyền','100% protected cases đã kiểm thử','RBAC + resource authorization tests','Quyền'),('NFR-03','Reliability','Booking trùng được chấp nhận','0','Concurrent booking tests PostgreSQL','Đạt'),('NFR-04','Auditability','Quyết định được ghi log','100% mẫu kiểm tra','DB audit inspection','Quyền'),('NFR-05','Compatibility','Client smoke scenarios','Pass trên Chrome, Edge; Android target chờ xác nhận','Manual smoke tests','Kiệt'),('NFR-06','AI fallback','AI error sang human review','100% lỗi AI mô phỏng','Mock AI failure tests','Quyền')]
matrix(ts[3],nfr)
matrix(ts[4],[('Gamified points; surveys','Ngoài đăng ký điều chỉnh BM01'),('AI semantic swipe matching','Đã loại khỏi giải pháp điều chỉnh'),('Premium portfolio/application quotas','Chưa quy định trong đăng ký điều chỉnh'),('Company giới thiệu chuyên gia; tự đăng ký chuyên gia; ví/rút tiền','Chuyên gia do Admin tiếp nhận; chưa cam kết marketplace hoặc tự động chi trả'),('Student–Company connection/private chat','SR-02: chờ chốt phạm vi; chưa đưa vào mandatory features của bản cam kết')])

# Add sixth workflow by cloning the entire fifth block before section 7.
body=d._element.body
start=next(p._p for p in d.paragraphs if p.text=='6.5 Workflow WF-05')
end=next(p._p for p in d.paragraphs if p.text=='7. Individual Commitments')
items=list(body); a=items.index(start); b=items.index(end)
clones=[deepcopy(x) for x in items[a:b]]
for x in clones:
    for t in x.iter(qn('w:t')):
        if t.text: t.text=t.text.replace('6.5 Workflow WF-05','6.6 Workflow WF-06').replace('DS-05','DS-06')
    end.addprevious(x)
from docx.table import Table
extra=[Table(x,d._body) for x in clones if x.tag==qn('w:tbl')]
wf_tables=[[ts[5+i*6+j] for j in range(6)] for i in range(5)]+[extra]

wf=[
dict(name='Dự án, tuyển thành viên và cộng tác',owner=0,support='Đạt: workspace/task/evidence/peer review; Quyền: moderation',actors='Student Leader, Student members, Moderator',trigger='Student tạo dự án; dự án được duyệt và có thành viên.',outcome='Dự án hoàn tất với evidence và kết quả đánh giá đóng góp.',states='Dự án: Pending → Active → Completed (2); review: Pending → Submitted (1); lời mời: Pending → Accepted (1).',f=[0,1],main='Tạo và duyệt dự án; mời/nhận thành viên; giao task, nộp evidence, hoàn tất và peer review.',ex='Người ngoài truy cập dự án riêng hoặc gửi evidence.',result='403; không lộ nội dung và không đổi dữ liệu.',ft=6,st=4,auto=8),
dict(name='Chọn chuyên gia, đặt lịch và thanh toán',owner=1,support='Thịnh: verification/payment integration; Quyền: hồ sơ chuyên gia',actors='Student Leader, Verificator; payment provider',trigger='Dự án Completed, L1 đạt và có lĩnh vực chuẩn hóa.',outcome='Booking Confirmed và assignment cho đúng chuyên gia Student chọn.',states='Booking: None → Held → Confirmed (2), Held → Expired (1), Held → Cancelled (1); payment: Pending → Success / Failed (2).',f=[2],main='Chọn chuyên gia khớp lĩnh vực; giữ lịch; checkout; nhận callback hợp lệ; xem booking và assignment.',ex='Hai Leader giữ cùng chuyên gia/cùng giờ; callback trễ hoặc lặp.',result='Không nhận booking trùng; không đổi người; callback lặp không tạo assignment mới; tiền thu trễ được đưa vào review.',ft=3,st=6,auto=8),
dict(name='Meeting, report và đánh giá sau phiên',owner=0,support='Đạt: booking handover; Quyền: quyền truy cập',actors='Student Leader, Verificator',trigger='Booking đã trả tiền hợp lệ, có assignment và đến lịch.',outcome='Có report và kết quả dự án; dịch vụ Completed; Leader có thể đánh giá.',states='Service: Pending → InProgress → Completed (2); kết quả: Pending → Verified / Failed (2); review: NotSubmitted → Submitted (1).',f=[3,4],main='Hai bên vào meeting; chuyên gia kết thúc phiên và nộp report; Leader đọc kết quả và đánh giá một lần.',ex='Nộp report trước khi kết thúc meeting hoặc đánh giá lần hai/trái quyền.',result='Từ chối; không hoàn tất service sai điều kiện, không tạo review trùng; quá hạn report báo Admin, không tự thay người.',ft=6,st=5,auto=9),
dict(name='Career profile và ứng tuyển',owner=2,support='Quyền: career profile; Thịnh: tích hợp',actors='Student, Company',trigger='Company có tin tuyển dụng hợp lệ; Student có profile.',outcome='Đơn được Company xử lý; Student theo dõi kết quả.',states='Application: Submitted → Reviewed → Accepted (2); Reviewed → Rejected (1).',f=[5,8],main='Cập nhật profile; Company đăng tin; Student nộp đơn; Company review và cập nhật kết quả.',ex='Company khác cố đổi trạng thái đơn hoặc sửa profile không thuộc mình.',result='403; đơn/profile không bị thay đổi.',ft=6,st=3,auto=7),
dict(name='Community và quảng cáo',owner=3,support='Quyền: moderation; Thịnh: payment integration',actors='Student, Company, Moderator',trigger='Có bài đăng hoặc chiến dịch quảng cáo được gửi.',outcome='Nội dung/chiến dịch được xuất bản khi đủ điều kiện.',states='Content: Pending → Approved / Rejected (2); campaign: Pending → Approved → Active (2).',f=[6],main='Gửi bài; Moderator duyệt; Company thanh toán quảng cáo và được duyệt; chiến dịch hoạt động.',ex='Bài vi phạm hoặc quảng cáo chưa thanh toán cố xuất bản.',result='Từ chối/giữ chờ đúng trạng thái; không hiển thị nội dung chưa được phép.',ft=3,st=4,auto=5),
dict(name='Quản trị, moderation và tiếp nhận chuyên gia',owner=4,support='Thịnh: verification rules; Vinh: community',actors='Admin, Moderator, Verificator',trigger='Có hồ sơ chuyên gia hoặc nội dung/hồ sơ cần xét duyệt.',outcome='Chuyên gia đúng lĩnh vực được kích hoạt; quyết định được ghi nhận và audit.',states='Expert profile: Draft → Active → Suspended (2); review item: Pending → Resolved (1).',f=[7],main='Admin tạo tài khoản chuyên gia; xác nhận lĩnh vực/kích hoạt; người có quyền xử lý review và kiểm tra audit.',ex='Actor thiếu quyền cố kích hoạt chuyên gia hoặc xử lý hồ sơ.',result='403; hồ sơ giữ nguyên; AI lỗi chuyển human review.',ft=3,st=3,auto=5)]
for i,(w,t) in enumerate(zip(wf,wf_tables),1):
    owner=names[w['owner']]; pref=f'{i:02}'
    kv(t[0],[w['name'],owner,w['support'],w['actors'],w['trigger'],w['outcome'],w['states'],f'Report 4 / SDD IV.3 – Activity WF-{pref}; bổ sung theo SR-04/SR-05'])
    fr=[]
    for j,fi in enumerate(w['f']):
        f=features[fi]; fr.append((f[0],f[1],f[3],f'TC-W{pref}-F{j*3+1:02}..F{j*3+3:02}'))
    matrix(t[1],fr)
    matrix(t[2],[(f'DS-{pref}a','Main',w['trigger']+' Seed các actor hợp lệ.',w['main'],w['outcome'],owner),(f'DS-{pref}b','Exception','Seed actor trái quyền/dữ liệu xung đột.',w['ex'],w['result'],owner)])
    total=w['ft']+1+w['st']; w['total']=total
    matrix(t[3],[('Feature tests (≥ 3 per feature)',str(w['ft']),str(w['ft']),owner),('End-to-end workflow test (≥ 1)','1','1',owner),('State transition tests (≥ 1 per transition)',str(w['st']),str(w['auto']-w['ft']-1),owner),('Total for this workflow',str(total),str(w['auto']),owner)])
    for rr,v in zip(t[4].rows[1:],['Yes','Yes','100% kịch bản exception đã cam kết','≥ 90%; 100% main-path mandatory tests','0']): cell(rr.cells[1],v)
    cell(t[5].rows[0].cells[1],'')

# Individual commitments; proposed targets, no fabricated reviewer approval/signature.
memberwf=[[0,2],[1],[3],[4],[5]]
nfrown=[[0],[2],[4],[],[1,3,5]]
nfauto=[1,1,0,0,2]
parts=['Recruitment và cộng tác; meeting/report/review; tích hợp','Booking và payment; workspace/evidence; peer review hỗ trợ','Job/application; Company profile; career integration','Community; advertising','Admin/moderation; student/career profile; chuyên gia onboarding']
decisions=['D1, D3','D4; hỗ trợ thiết kế booking/payment thuộc D3','D5','Không có D-ID riêng trong BM01','D2 (ownership map BM01); cần thống nhất với bảng D2 đang ghi Thịnh']
fixids=['SR-01, SR-03, SR-04, SR-05, SR-07','SR-06; hỗ trợ SR-04/SR-05','SR-02','Hỗ trợ test moderation/advertising','Hỗ trợ quyền và AI fallback; đồng bộ ownership D2']
for m in range(5):
    ix=35+m*5; owned=memberwf[m]; fids=[features[fi][0] for wi in owned for fi in wf[wi]['f']]
    tc=sum(wf[x]['total'] for x in owned)+len(nfrown[m]); auto=sum(wf[x]['auto'] for x in owned)+nfauto[m]
    kv(ts[ix],[names[m],'Chờ bổ sung mã sinh viên','Team leader / Full-stack' if m==0 else 'Full-stack'])
    matrix(ts[ix+1],[('Main workflow(s) owned',', '.join(f'WF-{x+1:02}' for x in owned),'Mỗi workflow có một owner'),('Mandatory features owned',', '.join(fids),'Đạt giữ phần workspace hỗ trợ theo BM01'),('Modules / entities owned',parts[m],'Phân công triển khai đề xuất theo WF2/WF3 điều chỉnh'),('Design decisions owned (D-IDs)',decisions[m],'Không tạo thêm quyết định thay BM01'),('NFRs responsible',', '.join(nfr[x][0] for x in nfrown[m]) or 'Hỗ trợ kiểm thử liên workflow','Mỗi test/fix chỉ tính cho một người')])
    testids='; '.join(f'TC-W{x+1:02}-F01..F{wf[x]["ft"]:02}, E01, S01..S{wf[x]["st"]:02}' for x in owned)
    testids+=('; '+', '.join('TC-'+nfr[x][0] for x in nfrown[m])) if nfrown[m] else ''
    matrix(ts[ix+2],[('Functional test cases written',f'{tc} test: '+testids,'Trước Defense 1.1; ngày cụ thể chờ xác nhận'),('Of which automated in CI',str(auto),'Trước Defense 1.1'),('Demo scenarios presented',', '.join(f'DS-{x+1:02}a/b' for x in owned),'Defense 1.1'),('Required fixes owned (section 9)',fixids[m],'Theo mục 9'),('Documentation sections (Report / section)','Report 4 / SDD IV.3: workflow sở hữu; Testing V.1–V.5; traceability','Trước Defense 1.1')])
    # Reviewer verification and signature left untouched.

sumrows=[]
for i,w in enumerate(wf): sumrows.append((f'WF-{i+1:02}',short[w['owner']],', '.join(features[x][0] for x in w['f']),'1 / 1',str(w['total']),str(w['auto'])))
sumrows += [('NFR (cross-workflow)','Theo mục 4','6 NFR','—','6','4'),('Total','5 thành viên','9 feature IDs','6 / 6','64','46')]
matrix(ts[60],sumrows)
rows=[]
for m in range(5):
    owned=memberwf[m]; rows.append((names[m],', '.join(f'WF-{x+1:02}' for x in owned),str(sum(len(wf[x]['f']) for x in owned)),str(sum(wf[x]['total'] for x in owned)+len(nfrown[m])),str(sum(wf[x]['auto'] for x in owned)+nfauto[m]),str(2*len(owned)),'Chờ reviewer xác nhận'))
rows.append(('Total','6','9','64','46','12','Chờ xác nhận'))
matrix(ts[61],rows)
kv(ts[62],['6','12 (6 main / 6 exception)','64 = 58 feature/E2E/state + 6 NFR','46/64 (71,9%)'])
for r in ts[63].rows[1:]: cell(r.cells[2],r.cells[1].text)
matrix(ts[64],[('SR-01','Đồng bộ scope/SRS/BM03 và xác nhận bản đăng ký cuối','Tất cả','Thịnh','02/11/2026'),('SR-02','Chốt connection/private chat; không coi là mandatory khi chưa xác nhận','WF-04','Kiệt','04/11/2026'),('SR-03','Chuẩn hóa NFR đo được và chuẩn bị test','Tất cả','Thịnh','05/11/2026'),('SR-04','State diagram/FR traceability; cập nhật WF2 booking/payment, WF3 meeting/report/review','WF-02, WF-03','Thịnh; Đạt hỗ trợ','06/11/2026'),('SR-05','Activity booking/payment/refund và E2E evidence; giữ đúng chuyên gia Student chọn','WF-02, WF-03','Thịnh; Đạt hỗ trợ','09/11/2026'),('SR-06','Đồng bộ giới hạn định dạng/kích thước upload với SRS','WF-01','Đạt','05/11/2026'),('SR-07','Resource authorization cho project riêng và regression tests','WF-01','Thịnh','02/11/2026')])
row(ts[65],3,['Supervisor','Nguyễn Trọng Tài; Huỳnh Văn Thống','',''])
row(ts[65],4,['Team leader',names[0],'',''])

# Keep each workflow/member together at a clear page boundary.
for p in d.paragraphs:
    if p.text in ['Committed test cases','Demo scenarios','Features in this workflow','Pass criteria at Defense 1.1','Reviewer note','Ownership','Delivery commitments to Defense 1.1']:
        p.paragraph_format.keep_with_next=True
# Keep native table geometry/styles; let all expanded rows grow and repeat headers.
for t in d.tables:
    for r in t.rows:
        for h in list(r._tr.xpath('./w:trPr/w:trHeight')): h.getparent().remove(h)
    if len(t.columns)>2:
        pr=t.rows[0]._tr.get_or_add_trPr()
        if pr.find(qn('w:tblHeader')) is None: pr.append(OxmlElement('w:tblHeader'))

tmp=base/'working.docx'; d.save(tmp)
# Only document.xml changes; preserve every other original package part byte-for-byte.
with ZipFile(src) as z, ZipFile(tmp) as edited, ZipFile(out,'w') as dest:
    for info in z.infolist(): dest.writestr(info,edited.read('word/document.xml') if info.filename=='word/document.xml' else z.read(info.filename))
with ZipFile(src) as a,ZipFile(out) as b:
    assert all(a.read(n)==b.read(n) for n in a.namelist() if n!='word/document.xml')
assert sum(w['total'] for w in wf)+6==64
assert sum(w['auto'] for w in wf)+4==46
(base/'artifact.md').write_text('Reference: '+str(src)+'\nSHA256: '+hashlib.sha256(src.read_bytes()).hexdigest()+'\nNarrow form fill. Preserve all package parts except word/document.xml. Preserve page geometry, typography, table grids and signature/reviewer areas. Extend mandatory/NFR/fix tables and clone WF05 as WF06. Editable slots: project metadata, commitments, workflow blocks, individual blocks and summaries. Reviewer approvals, student IDs, actual Review2 date and final registration approval remain unconfirmed. Source BM01 ownership conflict D2 explicitly flagged. 64 proposed tests / 46 CI; not execution evidence. Native renderer unavailable; Word PDF fallback for visual QA.\n',encoding='utf-8')
print(out)

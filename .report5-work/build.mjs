import fs from 'node:fs/promises';
import {FileBlob,SpreadsheetFile} from 'file:///C:/Users/Thinkpad/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs';
const dir='D:/SEP490/UniNet_BE/.report5-work';
const book=await SpreadsheetFile.importXlsx(await FileBlob.load('C:/Users/Thinkpad/Downloads/Report5_Test Report.xlsx'));
const sheet=n=>book.worksheets.getItem(n);
const put=(s,c,v)=>s.getRange(c).values=[[v]];
const formula=(s,c,v)=>s.getRange(c).formulas=[[v]];
const render=async(n,r,label)=>{try{const p=await book.render({sheetName:n,range:r,scale:1});await fs.writeFile(dir+'/'+label+'.png',new Uint8Array(await p.arrayBuffer()));}catch(e){console.log('Render',n,e.message);}};
await render('Feature 1','A2:O15','before');
if(process.argv.includes('--preview')) process.exit(0);
const parts=[];let part,group;
for(const line of (await fs.readFile(dir+'/cases.txt','utf8')).trim().split(/\r?\n/)){
 const [a,b,c,d]=line.split('|');
 if(a.startsWith('@')){group={name:a.slice(1),pre:b,rows:[]};part.groups.push(group);}
 else if(/^[A-Z]+$/.test(a)){part={id:a,name:b,scope:c,groups:[]};parts.push(part);}
 else group.rows.push([a,b,d,c]);
}
const cover=sheet('Cover');
const date=(Date.UTC(2026,10,13)-Date.UTC(1899,11,30))/86400000;
for(const cell of ['F5','A11']){put(cover,cell,date);cover.getRange(cell).setNumberFormat('dd/mm/yyyy');}
put(cover,'F6','1.0');put(cover,'C11','Functional test cases and statistics');put(cover,'D11','M');put(cover,'E11','Prepared UniNet functional cases and repaired the test statistics formulas.');put(cover,'F11','UniNet backend source and UniNet.Tests');
const index=sheet('Test Cases');
formula(index,'D3','=Cover!B4');formula(index,'D4','=Cover!B5');
put(index,'D5','1. ASP.NET Core Web API (.NET 8) with PostgreSQL and applied EF Core migrations.\n2. UniNet React Native client, staff web dashboard, and an API client for backend checks.\n3. Seed role-based accounts and project, job, campaign, and payment data. Configure Gemini, PayOS, and LiveKit or test doubles for the relevant cases.');
index.getRange('D5').format.wrapText=true;index.getRange('5:5').format.rowHeight=58;
const stats=sheet('Test Statistics');
for(const [c,f] of Object.entries({C3:'=Cover!B4',C4:'=Cover!B5',C5:'=Cover!B6',H3:'=Cover!F4',H5:'=Cover!F5'}))formula(stats,c,f);
put(stats,'H4','Awaiting review');stats.getRange('H5').setNumberFormat('dd/mm/yyyy');
put(stats,'C6','Statistics use Round 1. Coverage is the share of applicable cases executed. All newly documented cases are Pending until executed.');
stats.getRange('C6').format.wrapText=true;stats.getRange('H3').format.wrapText=true;stats.getRange('3:3').format.rowHeight=62;stats.getRange('6:6').format.rowHeight=33;
stats.getRange('B11:H17').clear({contentsOnly:true});
index.getRange('B9:F14').clear({contentsOnly:true});
let ix=9,total=0;
for(const [p,part] of parts.entries()){
 let s;
 if(p<2){s=sheet('Feature '+(p+1));s.name=part.name;s.getRange('A11:O22').clear({contentsOnly:true});s.getRange('A11:O22').format.fill='#FFFFFF';s.getRange('A11:O22').format.font.color='#000000';s.getRange('A11:O22').format.font.italic=false;s.getRange('A11:O22').format.font.bold=false;}
 else {s=book.worksheets.add(part.name);s.getRange('A2:A5').values=[['Feature'],['Test requirement'],['Number of TCs'],['Testing Round']];s.getRange('B5:E5').values=[['Passed','Failed','Pending','N/A']];s.getRange('A6:A8').values=[['Round 1'],['Round 2'],['Round 3']];s.getRange('A10:O10').values=[['Test Case ID','Test Case Description','Test Case Procedure','Expected Results','Pre-conditions','Round 1','Test date','Tester','Round 2','Test date','Tester','Round 3','Test date','Tester','Note']];s.getRange('B2:O2').merge();s.getRange('B3:O3').merge();}
 put(s,'B2',part.name);put(s,'B3',part.scope);
 let r=11,n=0;
 const widths=[118,190,320,300,220,75,90,95,75,90,95,75,90,95,105];
 for(const [j,w] of widths.entries())s.getRange(String.fromCharCode(65+j)+':'+String.fromCharCode(65+j)).format.columnWidthPx=w;
 for(const g of part.groups){
   index.getRange('B'+ix+':F'+ix).values=[[ix-8,g.name,part.name,part.scope,g.pre]];
   ix++;
   put(s,'A'+r,g.name);
   s.getRange('A'+r+':E'+r).merge(); const heading=s.getRange('A'+r+':O'+r);
   heading.format.fill='#CCF5F5';heading.format.font.bold=true;heading.format.rowHeight=21;
   r++;
   for(const [name,steps,pre,expected] of g.rows){
     n++;total++;
     const procedure=steps.split(/(?<=\.) (?=[A-Z])/).map((t,k)=>(k+1)+'. '+t).join('\n');
     s.getRange('A'+r+':O'+r).values=[['TC_'+part.id+'_'+String(n).padStart(3,'0'),name,procedure,expected,pre||g.pre,'Pending',null,null,'Pending',null,null,'Pending',null,null,null]];
     s.getRange('A'+r+':O'+r).format.rowHeight=65;
     r++;
   }
 }
 for(const [i,status] of ['F','I','L'].entries()){
   for(const c of ['B','C','D','E'])formula(s,c+(6+i),'=COUNTIF($'+status+'$11:$'+status+'$'+(r-1)+','+c+'5)');
   s.getRange(status+'11:'+status+(r-1)).dataValidation.rule={type:'list',values:['Passed','Failed','Pending','N/A']};
 }
 formula(s,'B4','=COUNTA(A11:A'+(r-1)+')-'+part.groups.length);
 s.getRange('A10:O'+(r-1)).format.wrapText=true;
 s.getRange('A10:O'+(r-1)).format.verticalAlignment='center';
 s.getRange('A10:O10').format.rowHeight=30;s.getRange('A10:O10').format.fill='#55722B';s.getRange('A10:O10').format.font.color='#FFFFFF';s.getRange('A10:O10').format.font.bold=true;
 const rr=11+p;put(stats,'B'+rr,p+1);
 for(const [col,ref] of Object.entries({C:'B2',D:'B6',E:'C6',F:'D6',G:'E6',H:'B4'}))formula(stats,col+rr,"='"+part.name+"'!"+ref);
}
const idx=index.getRange('B9:F'+(ix-1));idx.format.rowHeight=38;idx.format.wrapText=true;idx.format.verticalAlignment='center';
for(const [c,w] of Object.entries({B:55,C:205,D:220,E:350,F:340}))index.getRange(c+':'+c).format.columnWidthPx=w;
put(stats,'C18','Sub total');
for(const c of 'DEFGH')formula(stats,c+'18','=SUM('+c+'11:'+c+'17)');
put(stats,'C20','Test coverage');put(stats,'C21','Test successful coverage');
formula(stats,'E20','=IFERROR((D18+E18)/(H18-G18),0)');formula(stats,'E21','=IFERROR(D18/(H18-G18),0)');
stats.getRange('E20:E21').setNumberFormat('0.0%');
stats.getRange('C:C').format.columnWidthPx=235;
stats.getRange('H:H').format.columnWidthPx=150;
stats.getRange('B11:H17').format.fill='#FFFFFF';stats.getRange('B11:H17').format.font.color='#000000';stats.getRange('B11:H17').format.font.bold=false;stats.getRange('D11:H18').setNumberFormat('0');stats.getRange('B11:H18').format.rowHeight=20;
stats.getRange('B18:H18').format.fill='#CCF5F5';stats.getRange('B18:H18').format.font.bold=true;
book.recalculate();
console.log('Case count',total,'Functions',ix-9,'Stats',JSON.stringify(stats.getRange('D18:H18').values));
for(const [i,p] of parts.entries())await render(p.name,'A9:F14','feature'+i);
await render('Cover','A2:J12','cover');await render('Test Cases','B3:F13','index');await render('Test Statistics','B3:K21','stats');
const out=await SpreadsheetFile.exportXlsx(book);await out.save('D:/SEP490/UniNet_BE/Report5_Test_Report.xlsx');

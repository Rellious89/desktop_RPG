#!/usr/bin/env node
/* Local, read-only production inventory. Node standard library only. */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, '../..');
const rel = p => path.relative(root, p).split(path.sep).join('/');
const exists = p => fs.existsSync(p);
const files = p => exists(p) ? fs.readdirSync(p, { withFileTypes: true }).filter(x => !x.name.startsWith('.') && !x.name.endsWith('.meta')) : [];
const dirs = p => files(p).filter(x => x.isDirectory()).map(x => x.name).sort();
const walk = p => { if (!exists(p)) return []; const out=[]; for (const e of files(p)) { const q=path.join(p,e.name); e.isDirectory() ? out.push(...walk(q)) : out.push(q); } return out.sort(); };
const read = p => { try { return fs.readFileSync(p, 'utf8'); } catch { return ''; } };
const cfg = JSON.parse(read(path.join(here, 'tracker.config.json')));
const assetRoot = path.join(root, 'Assets');
const charDir = path.join(assetRoot, 'Art/Character'), enemyDir = path.join(assetRoot, 'Art/Enemy');
const packageDirs = ['Characters', 'Enemies'].map(x => path.join(root, 'ProjectDocs/ArtPipeline', x));
const charData = path.join(assetRoot, 'Data/Characters'), motionRoot = path.join(assetRoot, 'Data/MotionProfiles');
const canonical = id => cfg.aliases[id] || id;
const pngSize = p => { try { const b=fs.readFileSync(p); return b.subarray(1,4).toString() === 'PNG' ? { width:b.readUInt32BE(16), height:b.readUInt32BE(20) } : null; } catch { return null; } };
const meta = p => { const s=read(`${p}.meta`); const ppu=s.match(/spritePixelsToUnits:\s*(\d+(?:\.\d+)?)/); const pivot=s.match(/spritePivot:\s*\{x:\s*([\d.]+), y:\s*([\d.]+)\}/); return s ? { ppu: ppu?.[1] || null, pivot: pivot ? `(${pivot[1]}, ${pivot[2]})` : null } : null; };
const bucket = p => { const s=rel(p).toLowerCase(); const n=path.basename(p).toLowerCase();
  if (s.includes('/master/') || n.includes('master')) return 'master'; if (s.includes('portrait') || n.includes('portrait')) return 'portrait';
  if (/idle[_ -]?[abc](?:\/|$)/.test(s)) return 'idleVariant'; if (/(^|\/)idle(\/|$)/.test(s)) return 'baseIdle';
  if (/(?:tier[_ -]?3|attack[_ -]?(?:t3|3)|cast[_ -]?(?:t3|3))/.test(s)) return 'tier3';
  if (/(?:tier[_ -]?2|attack[_ -]?(?:t2|2)|cast[_ -]?(?:t2|2))/.test(s)) return 'tier2';
  if (/(?:^|\/)(?:attack|cast)(?:\/|_|-|$)|tier[_ -]?1/.test(s)) return 'tier1';
  if (/hit.*(recover|return)|recover/.test(s)) return 'hitRecovery'; if (/hit/.test(s)) return 'hitHold'; if (/death|die/.test(s)) return 'death'; return null;
};
const first = a => a.length ? a[0] : null;
const addPath = (a,p) => { if (p && !a.includes(rel(p))) a.push(rel(p)); };
const actorMap = new Map();
function actor(id) { const key=canonical(id); if (!actorMap.has(key)) actorMap.set(key, { id:key, aliases:[], sources:{ assetPaths:[], packagePaths:[], definitionPaths:[], motionProfilePaths:[], tablePaths:[], plannedPaths:[] }, evidence:{}, observations:[], warnings:[] }); const a=actorMap.get(key); if(id!==key && !a.aliases.includes(id)) a.aliases.push(id); return a; }
function addAsset(id, world, folder) { const a=actor(id); a.assetType=world==='Character'?'Player':'Enemy'; addPath(a.sources.assetPaths, folder); for(const p of walk(folder).filter(x=>/\.png$/i.test(x))) { const b=bucket(p); if(!b) continue; (a.evidence[b] ||= []).push({ path:rel(p), size:pngSize(p), meta:meta(p) }); } }
for (const name of dirs(charDir)) addAsset(name, 'Character', path.join(charDir,name));
for (const name of dirs(enemyDir)) addAsset(name, 'Enemy', path.join(enemyDir,name));
for (const packageDir of packageDirs) for (const name of dirs(packageDir)) { const a=actor(name), p=path.join(packageDir,name), all=walk(p); addPath(a.sources.packagePaths,p); const lower=all.map(x=>rel(x));
  const doc = rx => first(lower.filter(x=>rx.test(path.basename(x).toLowerCase())));
  a.package={ index:doc(/00_.*index|package-index/), brief:doc(/brief|character-sheet/), perfectPixel:doc(/perfectpixel/), measurements:doc(/measurement/), motionDocs:lower.filter(x=>/motion|idle/.test(path.basename(x).toLowerCase()) && /\.md$/i.test(x)), prototypes:lower.some(x=>/\/prototypes\//i.test(x)), masterFiles:lower.filter(x=>/master/i.test(path.basename(x)) && /\.png$/i.test(x)) };
  const index=a.package.index ? read(path.join(root,a.package.index)) : ''; const st=index.match(/Package Status:\s*`?([^`\n]+)/i); a.package.status=st?.[1]?.trim() || 'Unknown';
}
for (const p of walk(charData).filter(x=>/_CharacterDefinition\.asset$/i.test(x))) { const id=path.basename(p).replace(/_CharacterDefinition\.asset$/i,''); const a=actor(id); addPath(a.sources.definitionPaths,p); const t=read(p); a.definitionId=(t.match(/characterId:\s*(\S+)/)||[])[1] || id; if (a.definitionId !== a.id && !a.aliases.includes(a.definitionId)) a.aliases.push(a.definitionId); }
for (const kind of ['Characters','Monsters']) for(const id of dirs(path.join(motionRoot,kind))) { const a=actor(id); for(const p of walk(path.join(motionRoot,kind,id)).filter(x=>/_MotionProfile\.asset$/i.test(x))) addPath(a.sources.motionProfilePaths,p); }
const tableFiles=walk(root).filter(p=>/\.csv$/i.test(p) && /(?:character|monster)/i.test(path.basename(p)));
const csvRow = line => { const out=[]; let field='', quoted=false; for(let i=0;i<line.length;i++){ const c=line[i]; if(c==='"'){ if(quoted && line[i+1]==='"'){field+='"';i++;} else quoted=!quoted; } else if(c===','&&!quoted){out.push(field);field='';} else field+=c; } out.push(field); return out; };
for(const p of tableFiles) { const lines=read(p).split(/\r?\n/).filter(Boolean); if(lines.length<2) continue; const h=csvRow(lines[0]); const ni=h.findIndex(x=>/motion_profile_key/i.test(x)); const fallback=h.findIndex(x=>/character_id/i.test(x)); const wi=h.findIndex(x=>/world_id/i.test(x)), wn=h.findIndex(x=>/\$world_name|world_name/i.test(x)); for(const line of lines.slice(1)) { const c=csvRow(line); const key=c[ni>=0?ni:fallback]||''; const id=key.replace(/_MotionProfile$/i,''); if(!id || /^\d+$/.test(id)) continue; const a=actor(id); addPath(a.sources.tablePaths,p); a.table={ path:rel(p), key, world:c[wi]||null, worldName:c[wn]||null }; } }
const contentBankPath='ProjectDocs/WorldBuilding/ANIMAL-LAND-01-content-bank-v0.1.md';
for (const plan of cfg.plannedActors || []) { const a=actor(plan.id); a.planned={ ...plan, displayName:plan.displayName || plan.id, lifecycle:plan.lifecycle || 'Candidate', sourcePath:plan.sourcePath || contentBankPath, approval:plan.approval || 'AI Candidate Draft' }; addPath(a.sources.plannedPaths,path.join(root,a.planned.sourcePath)); }
for(const a of actorMap.values()) { a.aliases.sort(); const o=cfg.overrides[a.id]||{}, planned=a.planned; a.type=o.type || planned?.type || a.assetType || (a.sources.motionProfilePaths.some(p=>p.includes('/Monsters/'))?'Enemy':'Unknown'); a.lifecycle=o.lifecycle || planned?.lifecycle || (a.id.startsWith('Test_')?'Test':'Active'); a.world=o.world || planned?.world || (a.table?.world ? `${a.table.world}${a.table.worldName ? ` ${a.table.worldName}` : ''}` : 'Unmapped'); a.profile=o.profile || (a.lifecycle==='Candidate'?'Concept Actor':a.lifecycle==='Test'?'Test Actor':a.lifecycle==='Hold'?'Hold Actor':a.type==='Enemy'?'Passive Enemy':'Player Basic');
  for(const k of Object.keys(a.evidence)) a.evidence[k].sort((x,y)=>x.path.localeCompare(y.path));
  // Passive-enemy hit phases are semantic: one hold frame plus a separate recovery
  // frame selected by MotionProfile.hitReaction, not literal hit_hold/hit_recovery folders.
  if (a.type==='Enemy' && (a.evidence.hitHold?.length||0) >= 2) {
    const runtime = a.sources.motionProfilePaths.map(p=>read(path.join(root,p))).join('\n');
    if (/hitReaction:[\s\S]*?recoveryFrame:\s*[1-9]/.test(runtime)) a.evidence.hitRecovery=[a.evidence.hitHold[1]];
  }
  const profile=cfg.profiles[a.profile]; const present=k=>(a.evidence[k]?.length||0)>0; a.requirements={ required:profile.required, optional:profile.optional, observed:Object.keys(a.evidence).sort(), missingRequired:profile.required.filter(k=>!present(k)), missingOptional:profile.optional.filter(k=>!present(k)) };
  a.assetMotionCounts=Object.fromEntries(Object.entries(a.evidence).map(([k,v])=>[k,v.length]).sort(([x],[y])=>x.localeCompare(y)));
  a.packageChecklist={ index:!!a.package?.index, brief:!!a.package?.brief, perfectPixelInput:!!a.package?.perfectPixel, measurements:!!a.package?.measurements, motionBrief:(a.package?.motionDocs?.length||0)>0, status:a.package?.status || 'No package' };
  a.runtimeChecklist={ motionProfile:a.sources.motionProfilePaths.length>0, characterDefinition:a.type==='Player'?a.sources.definitionPaths.length>0:null, table:(a.type==='Enemy' && a.lifecycle!=='Candidate') ? a.sources.tablePaths.length>0 : null };
  a.packageComplete=a.packageChecklist.index && a.packageChecklist.brief && a.packageChecklist.motionBrief && (present('master') || (a.package?.masterFiles?.length||0)>0);
  a.runtimeConnected=a.runtimeChecklist.motionProfile && (a.type==='Enemy' ? a.runtimeChecklist.table : a.runtimeChecklist.characterDefinition===true);
  a.completeness=a.lifecycle==='Test'?null: profile.required.length ? Math.round(100*(profile.required.length-a.requirements.missingRequired.length)/profile.required.length) : null;
  const stages=[['제작 패키지 인덱스 작성',!a.package?.index],['캐릭터 Brief 작성',!a.package?.brief],['Master 준비/승인',!(present('master')||a.package?.masterFiles?.length)],['모션 Brief 작성',(a.package?.motionDocs?.length||0)===0],['필수 애니메이션 프레임 제작',profile.required.some(k=>!present(k))],['Unity 런타임 연결',!a.runtimeConnected],['테이블 등록',a.type==='Enemy'&&!a.runtimeChecklist.table]];
  const need=stages.find(x=>x[1]); a.nextAction=a.lifecycle==='Candidate'?'후보 선택 및 Brief 승인':a.lifecycle==='Test'?'테스트 배우: 관찰 전용':a.lifecycle==='Hold'?'보류: 누락은 표시하지만 우선순위에서 제외':need?.[0] || '애니메이션 요구사항 및 런타임 연결 확인';
  if(a.aliases.length) a.warnings.push(`Alias mapping: ${a.aliases.join(', ')} → ${a.id}`); if(a.world==='Unmapped') a.warnings.push('World mapping not found; no guess applied.'); if(a.type==='Unknown') a.warnings.push('Actor type could not be determined.');
  const deliveryKeys=['baseIdle','idleVariant','tier1','tier2','tier3','hitHold','hitRecovery','death']; const imports=deliveryKeys.flatMap(k=>a.evidence[k]||[]).map(x=>x.meta).filter(Boolean); const ppu=[...new Set(imports.map(x=>x.ppu).filter(Boolean))]; const pivots=[...new Set(imports.map(x=>x.pivot).filter(Boolean))];
  a.importProfile=imports.length===0?'unknown':(ppu.length===1 && ppu[0]==='50' && pivots.length===1 && pivots[0]==='(0.5, 0.234)')?'V2':(ppu.length===1 && ppu[0]==='50' && pivots.length===1 && pivots[0]==='(0.5, 0.1)')?'V1':'unknown';
  if(ppu.length>1 || pivots.length>1) a.warnings.push(`Mixed import settings observed (PPU: ${ppu.join(', ')||'none'}; pivot: ${pivots.join(', ')||'none'}); profile unknown.`);
  if(a.package?.index && /KeyBuddy V2 Pilot|Test_IceMage V2/i.test(read(path.join(root,a.package.index))) && a.importProfile==='unknown') a.importProfile='V2 candidate/pending import';
  a.sources.assetPaths.sort(); for(const k of Object.keys(a.sources)) a.sources[k].sort();
}
const actors=[...actorMap.values()].sort((a,b)=>a.id.localeCompare(b.id));
const active=actors.filter(a=>a.lifecycle==='Active'), ready=active.filter(a=>a.requirements.missingRequired.length===0), gaps=active.filter(a=>a.requirements.missingRequired.length>0);
const report={ schemaVersion:1, readOnly:true, root:'.', profiles:cfg.profiles, summary:{ total:actors.length, active:active.length, animationRequirementsReady:ready.length, withRequiredGaps:gaps.length, packageComplete:active.filter(a=>a.packageComplete).length, runtimeConnected:active.filter(a=>a.runtimeConnected).length, candidate:actors.filter(a=>a.lifecycle==='Candidate').length, test:actors.filter(a=>a.lifecycle==='Test').length, hold:actors.filter(a=>a.lifecycle==='Hold').length, unmapped:actors.filter(a=>a.world==='Unmapped').length }, actors };
const out=path.join(root,'ProjectDocs/ActorProduction'); fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'actor-production-index.json'),JSON.stringify(report,null,2)+'\n');
fs.writeFileSync(path.join(out,'dashboard-data.js'),`window.ACTOR_PRODUCTION_DATA = ${JSON.stringify(report,null,2)};\n`);
const q=gaps.sort((a,b)=>a.id.localeCompare(b.id)); const current=active.slice().sort((a,b)=>a.id.localeCompare(b.id)); const md=['# Actor Production Tracker','', '> Local, read-only derived report. Re-run `node Tools/ActorProductionTracker/scan.mjs` to refresh.','',`## Summary`,`- Actors: ${report.summary.total} (active ${report.summary.active}, candidate ${report.summary.candidate}, test ${report.summary.test}, hold ${report.summary.hold})`,`- Animation requirements ready (not overall project completion): ${report.summary.animationRequirementsReady}/${report.summary.active}`,`- Active actors with required animation gaps: ${report.summary.withRequiredGaps}`,`- Package complete: ${report.summary.packageComplete}/${report.summary.active}; runtime connected: ${report.summary.runtimeConnected}/${report.summary.active}`,`- Package completion is a new-package-rule migration signal; it does not mean existing assets are absent.`,`- Unmapped worlds: ${report.summary.unmapped}`,'','## Current production next actions','',...current.map(a=>`- **${a.id}** — ${a.nextAction}`),'','## Animation gaps (active only)','',...(q.length?q.map(a=>`- **${a.id}** — ${a.requirements.missingRequired.join(', ')}`):['- No active required animation gaps.']),'','## Requirement profiles (provisional asset rules)','',...Object.entries(cfg.profiles).map(([n,p])=>`- **${n}** — required: ${p.required.join(', ')||'none'}; optional: ${p.optional.join(', ')||'none'}.`),'','## Actor index','',...actors.map(a=>`- **${a.id}** (${a.type}, ${a.lifecycle}, ${a.profile}) — animation requirements: ${a.completeness===null?'excluded':`${a.completeness}%`}; package ${a.packageComplete?'complete':'incomplete'}; runtime ${a.runtimeConnected?'connected':'not connected'}; missing: ${a.requirements.missingRequired.join(', ')||'none'}; next: ${a.nextAction}`),'']; fs.writeFileSync(path.join(out,'actor-production-report.md'),md.join('\n'));
console.log(`Actor Production Tracker: ${actors.length} actors; ${gaps.length} active actors with required gaps.`);

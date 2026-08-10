#!/usr/bin/env node
/* Local, read-only production inventory. Node standard library only. */
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
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
const walkMeta = p => { if (!exists(p)) return []; const out=[]; for (const e of fs.readdirSync(p,{withFileTypes:true})) { if(e.name.startsWith('.')) continue; const q=path.join(p,e.name); e.isDirectory()?out.push(...walkMeta(q)):e.name.endsWith('.meta')&&out.push(q); } return out; };
const guidToAsset = new Map();
for (const p of walkMeta(assetRoot)) { const guid=read(p).match(/^guid:\s*([0-9a-f]+)/m)?.[1]; if(guid) guidToAsset.set(guid,p.slice(0,-5)); }
const poolAssetPaths = walk(assetRoot).filter(p=>/AttackPool(?: \d+)?\.asset$/i.test(p));
const canonical = id => cfg.aliases[id] || id;
const requirementLabel = key => ({
  baseIdle:'Base Idle', idleVariant:'Idle Variant', tier1:'Attack A (Tier 1)',
  tier2:'Attack B (Tier 2)', tier3:'Attack C (Tier 3)', hitHold:'Hit Hold',
  hitRecovery:'Hit Recovery', death:'Death'
}[key] || key);
const attackPoolStatusLabel = status => ({ ready:'연결', 'pool missing':'풀 없음', 'pool empty':'빈 풀', 'lower motion missing':'하위 모션 누락', 'new motion missing':'새 모션 누락' }[status] || status);
const enemyClassLabel = status => ({ usable:'그대로 사용 가능', 'package-only':'제작 패키지 보강', 'motion-revision':'모션 수정 필요', 'runtime-gap':'런타임 연결 필요' }[status] || status);
const enemyRecoveryLabel = reaction => reaction.recoveryVisuallyDistinct?'복귀 구분':reaction.repeatedRecoveryAllowed?'복귀 반복(승인)':'복귀 중복';
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
const guidRefs = s => [...s.matchAll(/guid:\s*([0-9a-f]{32})/g)].map(m=>m[1]);
function motionAudit(guid) {
  const p=guidToAsset.get(guid), s=p?read(p):'';
  const frameBlock=s.match(/\n  frames:\s*([\s\S]*?)(?=\n  (?:overlayFrames|animationFps):)/)?.[1] || '';
  const frameGuids=guidRefs(frameBlock);
  return { guid, path:p?rel(p):null, name:(s.match(/\n  m_Name:\s*([^\n]+)/)||[])[1]?.trim()||path.basename(p||guid), frameCount:frameGuids.length, frameSignature:frameGuids.join(':'), playable:frameGuids.length>0 };
}
function poolAudit(profileText, field) {
  const block=profileText.match(new RegExp(`${field}:\\s*\\{([^}]*)\\}`))?.[1] || '';
  const poolGuid=block.match(/guid:\s*([0-9a-f]{32})/)?.[1] || null;
  const p=poolGuid?guidToAsset.get(poolGuid):null, s=p?read(p):'';
  const motionBlock=s.match(/\n  motions:\s*([\s\S]*)$/)?.[1] || '';
  const motions=guidRefs(motionBlock).map(motionAudit);
  return { field, poolGuid, poolPath:p?rel(p):null, referenced:!!poolGuid, motionCount:motions.length, playableCount:motions.filter(x=>x.playable).length, motions };
}
function attackPoolAudit(a, requiredKeys) {
  const profilePath=a.sources.motionProfilePaths[0], profileText=profilePath?read(path.join(root,profilePath)):'';
  const tiers={ tier1:poolAudit(profileText,'tier1Pool'), tier2:poolAudit(profileText,'tier2Pool'), tier3:poolAudit(profileText,'tier3Pool') };
  let lower=new Set();
  for(const key of ['tier1','tier2','tier3']) {
    const t=tiers[key], sigs=new Set(t.motions.filter(x=>x.playable).map(x=>x.frameSignature||x.guid));
    t.includesLower=[...lower].every(x=>sigs.has(x)); t.newMotionCount=[...sigs].filter(x=>!lower.has(x)).length;
    t.cumulative=t.playableCount>0 && (key==='tier1' || (t.includesLower && t.newMotionCount>0));
    t.status=!t.referenced?'pool missing':t.playableCount===0?'pool empty':!t.includesLower?'lower motion missing':t.newMotionCount===0?'new motion missing':'ready';
    if(t.cumulative) lower=sigs;
  }
  const referenced=new Set(Object.values(tiers).map(x=>x.poolPath).filter(Boolean));
  const orphanPools=poolAssetPaths.filter(p=>path.basename(p).toLowerCase().includes(a.id.toLowerCase())&&!referenced.has(rel(p))).map(p=>{const s=read(p), motions=guidRefs(s.match(/\n  motions:\s*([\s\S]*)$/)?.[1]||'').map(motionAudit);return {path:rel(p),motionCount:motions.length,motions};});
  const requiredTiers=requiredKeys.filter(x=>/^tier[123]$/.test(x));
  return { requiredTiers, connected:requiredTiers.every(k=>tiers[k].cumulative), tiers, orphanPools };
}
function profileSection(profileText, field, nextFieldPattern='[A-Za-z][A-Za-z0-9]*') {
  return profileText.match(new RegExp(`\\n  ${field}:\\s*\\n([\\s\\S]*?)(?=\\n  ${nextFieldPattern}:|$)`))?.[1] || '';
}
function monsterClipAudit(profileText, field) {
  const section=profileSection(profileText,field), frameBlock=section.match(/\n    frames:\s*([\s\S]*?)(?=\n    animationFps:|$)/)?.[1] || '';
  const frameGuids=guidRefs(frameBlock), framePaths=frameGuids.map(g=>guidToAsset.get(g)).filter(Boolean).map(rel);
  return {
    field,
    frameCount:frameGuids.length,
    uniqueFrameCount:new Set(frameGuids).size,
    frameGuids,
    framePaths,
    unresolvedGuids:frameGuids.filter(g=>!guidToAsset.has(g)),
    duplicateFrameRefs:frameGuids.length-new Set(frameGuids).size,
    fps:Number(section.match(/\n    animationFps:\s*([\d.]+)/)?.[1] || 0),
    playable:frameGuids.length>0 && frameGuids.every(g=>guidToAsset.has(g))
  };
}
function monsterIdleEventsAudit(profileText) {
  const section=profileText.match(/\n  idleEvents:\s*([\s\S]*?)(?=\n  idleEventCheckInterval:|$)/)?.[1] || '';
  if(!section.trim() || section.trim()==='[]') return [];
  return (`\n${section}`).split(/\n\s{0,2}- displayName:\s*/).slice(1).map((part,index)=>{
    const name=part.match(/^([^\n]+)/)?.[1]?.trim() || `Idle Event ${index+1}`;
    const frameBlock=part.match(/\n    frames:\s*([\s\S]*?)(?=\n    animationFps:|$)/)?.[1] || '';
    const frameGuids=guidRefs(frameBlock), framePaths=frameGuids.map(g=>guidToAsset.get(g)).filter(Boolean).map(rel);
    return { name, frameCount:frameGuids.length, uniqueFrameCount:new Set(frameGuids).size, frameGuids, framePaths, unresolvedGuids:frameGuids.filter(g=>!guidToAsset.has(g)), fps:Number(part.match(/\n    animationFps:\s*([\d.]+)/)?.[1] || 0), playable:frameGuids.length>0 && frameGuids.every(g=>guidToAsset.has(g)) };
  });
}
function monsterMotionAudit(a) {
  const profilePath=a.sources.motionProfilePaths[0] || null, profileText=profilePath?read(path.join(root,profilePath)):'';
  const baseIdle=monsterClipAudit(profileText,'baseIdle'), hit=monsterClipAudit(profileText,'hit'), defeat=monsterClipAudit(profileText,'defeat');
  const idleEvents=monsterIdleEventsAudit(profileText), reaction=profileSection(profileText,'hitReaction','preview');
  const holdFrame=Number(reaction.match(/(?:^|\n)    holdFrame:\s*(\d+)/)?.[1] ?? -1), recoveryFrame=Number(reaction.match(/(?:^|\n)    recoveryFrame:\s*(\d+)/)?.[1] ?? -1);
  const holdGuid=hit.frameGuids[holdFrame] || null, recoveryGuid=hit.frameGuids[recoveryFrame] || null;
  const hitIndexesValid=holdFrame>=0 && recoveryFrame>=0 && holdFrame<hit.frameCount && recoveryFrame<hit.frameCount;
  const recoveryVisuallyDistinct=hitIndexesValid && !!holdGuid && !!recoveryGuid && holdGuid!==recoveryGuid;
  const repeatedRecoveryAllowed=cfg.overrides[a.id]?.allowRepeatedHitRecovery===true;
  const recoveryAccepted=hitIndexesValid && !!holdGuid && !!recoveryGuid && (recoveryVisuallyDistinct || repeatedRecoveryAllowed);
  const resourceFolderPath=profileText.match(/\n  resourceFolderPath:\s*([^\n]+)/)?.[1]?.trim() || null;
  const displayName=profileText.match(/\n  displayName:\s*([^\n]+)/)?.[1]?.trim() || null;
  const actorAssetRoot=a.sources.assetPaths[0] || null;
  const referencedMotionPaths=[...baseIdle.framePaths,...idleEvents.flatMap(x=>x.framePaths),...hit.framePaths,...defeat.framePaths];
  const rawMotionPaths=['baseIdle','idleVariant','hitHold','death'].flatMap(k=>(a.evidence[k]||[]).map(x=>x.path));
  const unreferencedMotionFrames=[...new Set(rawMotionPaths.filter(p=>!referencedMotionPaths.includes(p)))].sort();
  const foreignFrameRefs=[...new Set(referencedMotionPaths.filter(p=>actorAssetRoot && !p.startsWith(`${actorAssetRoot}/`)))].sort();
  const profilePlayable=!!profilePath && baseIdle.playable && hit.playable;
  const tableProfileMatches=!!a.table && a.table.motionProfileKey===path.basename(profilePath||'', '.asset');
  const previewSpriteReady=!a.table?.previewSpriteKey || (a.evidence.portrait||[]).some(x=>path.basename(x.path,'.png')===a.table.previewSpriteKey);
  const tableConnected=!!a.table && a.table.enabled && tableProfileMatches && previewSpriteReady;
  return {
    profilePath, displayName, resourceFolderPath,
    resourceFolderMatches:!!resourceFolderPath && resourceFolderPath===actorAssetRoot,
    baseIdle, idleEvents, hit, defeat,
    hitReaction:{ holdFrame, recoveryFrame, holdGuid, recoveryGuid, indexesValid:hitIndexesValid, recoveryVisuallyDistinct, repeatedRecoveryAllowed, recoveryAccepted, exceptionReason:repeatedRecoveryAllowed?cfg.overrides[a.id]?.hitRecoveryReason||null:null },
    defeatMode:defeat.playable?'animated':'fade-only',
    profilePlayable, tableConnected, tableProfileMatches, previewSpriteReady,
    unreferencedMotionFrames, foreignFrameRefs,
    motionQualityReady:profilePlayable && recoveryAccepted && foreignFrameRefs.length===0
  };
}
function addAsset(id, world, folder) {
  const a=actor(id); a.assetType=world==='Character'?'Player':'Enemy'; addPath(a.sources.assetPaths, folder);
  for(const p of walk(folder).filter(x=>/\.png$/i.test(x))) {
    const item={ path:rel(p), size:pngSize(p), meta:meta(p) };
    (a.evidence.anyImage ||= []).push(item);
    const b=bucket(p); if(b) (a.evidence[b] ||= []).push(item);
  }
}
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
for(const p of tableFiles) { const lines=read(p).split(/\r?\n/).filter(Boolean); if(lines.length<2) continue; const h=csvRow(lines[0]); const col=name=>h.findIndex(x=>x===name), ni=h.findIndex(x=>/motion_profile_key/i.test(x)); const fallback=h.findIndex(x=>/character_id/i.test(x)); const wi=col('world_id'), wn=h.findIndex(x=>/\$world_name|world_name/i.test(x)); for(const line of lines.slice(1)) { const c=csvRow(line); const key=c[ni>=0?ni:fallback]||''; const id=key.replace(/_MotionProfile$/i,''); if(!id || /^\d+$/.test(id)) continue; const a=actor(id); addPath(a.sources.tablePaths,p); a.table={ path:rel(p), key, motionProfileKey:key, monsterId:col('monster_id')>=0?c[col('monster_id')]||null:null, displayName:col('$monster_name')>=0?c[col('$monster_name')]||null:null, world:c[wi]||null, worldName:c[wn]||null, previewSpriteKey:col('preview_sprite_key')>=0?c[col('preview_sprite_key')]||null:null, enabled:col('enabled')<0 || c[col('enabled')]==='1', displayOrder:col('display_order')>=0?Number(c[col('display_order')]||0):null }; } }
const contentBankPath='ProjectDocs/WorldBuilding/ANIMAL-LAND-01-content-bank-v0.1.md';
for (const plan of cfg.plannedActors || []) { const a=actor(plan.id); a.planned={ ...plan, displayName:plan.displayName || plan.id, lifecycle:plan.lifecycle || 'Candidate', sourcePath:plan.sourcePath || contentBankPath, approval:plan.approval || 'AI Candidate Draft' }; addPath(a.sources.plannedPaths,path.join(root,a.planned.sourcePath)); }
for(const a of actorMap.values()) { a.aliases.sort(); const o=cfg.overrides[a.id]||{}, planned=a.planned; a.type=o.type || planned?.type || a.assetType || (a.sources.motionProfilePaths.some(p=>p.includes('/Monsters/'))?'Enemy':'Unknown'); a.lifecycle=o.lifecycle || planned?.lifecycle || (a.id.startsWith('Test_')?'Test':'Active'); a.world=o.world || planned?.world || (a.table?.world ? `${a.table.world}${a.table.worldName ? ` ${a.table.worldName}` : ''}` : 'Unmapped'); a.profile=o.profile || (a.lifecycle==='Candidate'?'Concept Actor':a.lifecycle==='Test'?'Test Actor':a.lifecycle==='Hold'?'Hold Actor':a.type==='Enemy'?'Passive Enemy':'Player Basic');
  for(const k of Object.keys(a.evidence)) a.evidence[k].sort((x,y)=>x.path.localeCompare(y.path));
  // Passive-enemy hit phases are semantic: the profile's hold/recovery indices,
  // not file ordering, decide which two sprites are used. A duplicate GUID is a
  // playable runtime fallback but does not count as a distinct recovery resource.
  a.enemyRuntimeAudit=a.type==='Enemy' && a.lifecycle!=='Candidate'?monsterMotionAudit(a):null;
  if (a.enemyRuntimeAudit) {
    const allEvidence=Object.values(a.evidence).flat();
    const byGuid=guid=>{const p=guidToAsset.get(guid);return p?allEvidence.find(x=>x.path===rel(p))||{path:rel(p),size:pngSize(p),meta:meta(p)}:null;};
    const hold=byGuid(a.enemyRuntimeAudit.hitReaction.holdGuid), recovery=byGuid(a.enemyRuntimeAudit.hitReaction.recoveryGuid);
    a.evidence.hitHold=hold?[hold]:[];
    a.evidence.hitRecovery=recovery && a.enemyRuntimeAudit.hitReaction.recoveryAccepted?[recovery]:[];
  }
  const profile=cfg.profiles[a.profile]; const present=k=>(a.evidence[k]?.length||0)>0; a.requirements={ required:profile.required, optional:profile.optional, observed:Object.keys(a.evidence).sort(), missingRequired:profile.required.filter(k=>!present(k)), missingOptional:profile.optional.filter(k=>!present(k)) };
  a.attackPoolAudit=a.type==='Player'?attackPoolAudit(a,profile.required):null;
  a.assetMotionCounts=Object.fromEntries(Object.entries(a.evidence).map(([k,v])=>[k,v.length]).sort(([x],[y])=>x.localeCompare(y)));
  a.packageChecklist={ index:!!a.package?.index, brief:!!a.package?.brief, perfectPixelInput:!!a.package?.perfectPixel, measurements:!!a.package?.measurements, motionBrief:(a.package?.motionDocs?.length||0)>0, status:a.package?.status || 'No package' };
  a.runtimeChecklist={ motionProfile:a.sources.motionProfilePaths.length>0, profilePlayable:a.enemyRuntimeAudit?a.enemyRuntimeAudit.profilePlayable:null, characterDefinition:a.type==='Player'?a.sources.definitionPaths.length>0:null, attackPools:a.type==='Player'?a.attackPoolAudit.connected:null, table:a.enemyRuntimeAudit?a.enemyRuntimeAudit.tableConnected:null };
  // Only an imported production master under Assets/.../master counts. Package
  // reference/prototype PNGs are useful context, but cannot certify a master.
  const approvedOrObservedMaster=present('master');
  a.packageChecklist.master=approvedOrObservedMaster;
  a.packageComplete=a.packageChecklist.index && a.packageChecklist.brief && a.packageChecklist.perfectPixelInput && a.packageChecklist.measurements && a.packageChecklist.motionBrief && approvedOrObservedMaster;
  if(a.enemyRuntimeAudit) a.enemyRuntimeAudit.productionClass=!a.enemyRuntimeAudit.profilePlayable || !a.enemyRuntimeAudit.tableConnected?'runtime-gap':!a.enemyRuntimeAudit.motionQualityReady?'motion-revision':!a.packageComplete?'package-only':'usable';
  a.runtimeConnected=a.runtimeChecklist.motionProfile && (a.type==='Enemy' ? a.runtimeChecklist.profilePlayable && a.runtimeChecklist.table : a.runtimeChecklist.characterDefinition===true && a.runtimeChecklist.attackPools===true);
  a.completeness=a.lifecycle==='Test'?null: profile.required.length ? Math.round(100*(profile.required.length-a.requirements.missingRequired.length)/profile.required.length) : null;
  const packageItems=['index','brief','perfectPixelInput','measurements','motionBrief','master'];
  const packageDone=packageItems.filter(k=>a.packageChecklist[k]).length;
  const resourceApplicable=!['Candidate','Test'].includes(a.lifecycle) && profile.required.length>0;
  const gameApplicable=a.lifecycle!=='Candidate';
  const runtimeItems=a.type==='Player' ? ['motionProfile','characterDefinition','attackPools'] : ['motionProfile','profilePlayable','table'];
  const runtimeDone=runtimeItems.filter(k=>a.runtimeChecklist[k]===true).length;
  a.progress={
    package:{ label:'설정/패키지', percent:Math.round(packageDone/packageItems.length*100), done:packageDone, total:packageItems.length, complete:a.packageComplete, checklist:a.packageChecklist },
    resources:{ label:'리소스', percent:resourceApplicable?a.completeness:null, done:resourceApplicable?profile.required.length-a.requirements.missingRequired.length:null, total:resourceApplicable?profile.required.length:null, applicable:resourceApplicable, missing:a.requirements.missingRequired },
    game:{ label:'게임 연결', percent:gameApplicable?Math.round(runtimeDone/runtimeItems.length*100):null, done:gameApplicable?runtimeDone:null, total:gameApplicable?runtimeItems.length:null, applicable:gameApplicable, complete:gameApplicable?a.runtimeConnected:false, checklist:a.runtimeChecklist }
  };
  const imageOrder=['master','portrait','baseIdle','tier1','anyImage'];
  const usefulImage=items=>(items||[]).find(x=>!/(chromakey|\/source\/)/i.test(x.path)) || (items||[])[0];
  const thumbnailKind=imageOrder.find(k=>usefulImage(a.evidence[k]));
  a.thumbnailPath=thumbnailKind ? usefulImage(a.evidence[thumbnailKind]).path : null;
  a.thumbnailKind=thumbnailKind || null;
  if(a.lifecycle==='Candidate') a.stage='Candidate';
  else if(a.lifecycle==='Hold') a.stage='Hold';
  else if(a.lifecycle==='Test') a.stage='Test';
  else if(!a.packageChecklist.brief) a.stage='Brief';
  else if(!approvedOrObservedMaster) a.stage='Master';
  else if(a.requirements.missingRequired.length) a.stage='Animation';
  else if(!a.runtimeConnected) a.stage='Unity';
  else a.stage='Ready';
  const stages=[['제작 패키지 인덱스 작성',!a.package?.index],['캐릭터 Brief 작성',!a.package?.brief],['PerfectPixel 입력 작성',!a.package?.perfectPixel],['Master 측정 작성',!a.package?.measurements],['Master 준비/승인',!approvedOrObservedMaster],['모션 Brief 작성',(a.package?.motionDocs?.length||0)===0],['필수 애니메이션 프레임 제작',profile.required.some(k=>!present(k))],['공격 Tier 풀 연결',a.type==='Player'&&!a.attackPoolAudit.connected],['Unity 런타임 연결',!a.runtimeConnected],['테이블 등록',a.type==='Enemy'&&!a.runtimeChecklist.table]];
  const need=stages.find(x=>x[1]); a.nextAction=a.lifecycle==='Candidate'?'후보 선택 및 Brief 승인':a.lifecycle==='Test'?'테스트 배우: 관찰 전용':a.lifecycle==='Hold'?'보류: 누락은 표시하지만 우선순위에서 제외':need?.[0] || '애니메이션 요구사항 및 런타임 연결 확인';
  if(a.aliases.length) a.warnings.push(`Alias mapping: ${a.aliases.join(', ')} → ${a.id}`); if(a.world==='Unmapped') a.warnings.push('World mapping not found; no guess applied.'); if(a.type==='Unknown') a.warnings.push('Actor type could not be determined.');
  if(a.attackPoolAudit?.orphanPools.length) a.warnings.push(`Unreferenced attack pool assets: ${a.attackPoolAudit.orphanPools.map(x=>x.path).join(', ')}`);
  for(const key of a.attackPoolAudit?.requiredTiers||[]) if(present(key)&&!a.attackPoolAudit.tiers[key].cumulative) a.warnings.push(`${requirementLabel(key)} frames exist but pool status is '${a.attackPoolAudit.tiers[key].status}'.`);
  if(a.enemyRuntimeAudit && !a.enemyRuntimeAudit.hitReaction.indexesValid) a.warnings.push('Monster hit hold/recovery frame index is out of range.');
  if(a.enemyRuntimeAudit && a.enemyRuntimeAudit.hitReaction.indexesValid && !a.enemyRuntimeAudit.hitReaction.recoveryVisuallyDistinct && !a.enemyRuntimeAudit.hitReaction.repeatedRecoveryAllowed) a.warnings.push('Monster hit hold/recovery slots reference the same sprite; a distinct recovery frame is required for production readiness.');
  if(a.enemyRuntimeAudit?.hitReaction.repeatedRecoveryAllowed) a.observations.push(`Repeated Hit Recovery accepted by explicit production exception: ${a.enemyRuntimeAudit.hitReaction.exceptionReason}`);
  if(a.enemyRuntimeAudit?.unreferencedMotionFrames.length) a.warnings.push(`Unreferenced motion frames: ${a.enemyRuntimeAudit.unreferencedMotionFrames.join(', ')}`);
  if(a.enemyRuntimeAudit && !a.enemyRuntimeAudit.resourceFolderMatches) a.warnings.push(`MotionProfile resource folder mismatch: ${a.enemyRuntimeAudit.resourceFolderPath||'missing'}`);
  if(a.enemyRuntimeAudit?.foreignFrameRefs.length) a.warnings.push(`MotionProfile references another actor folder: ${a.enemyRuntimeAudit.foreignFrameRefs.join(', ')}`);
  const deliveryKeys=['baseIdle','idleVariant','tier1','tier2','tier3','hitHold','hitRecovery','death']; const imports=deliveryKeys.flatMap(k=>a.evidence[k]||[]).map(x=>x.meta).filter(Boolean); const ppu=[...new Set(imports.map(x=>x.ppu).filter(Boolean))]; const pivots=[...new Set(imports.map(x=>x.pivot).filter(Boolean))];
  a.importProfile=imports.length===0?'unknown':(ppu.length===1 && ppu[0]==='50' && pivots.length===1 && pivots[0]==='(0.5, 0.234)')?'V2':(ppu.length===1 && ppu[0]==='50' && pivots.length===1 && pivots[0]==='(0.5, 0.1)')?'V1':'unknown';
  if(ppu.length>1 || pivots.length>1) a.warnings.push(`Mixed import settings observed (PPU: ${ppu.join(', ')||'none'}; pivot: ${pivots.join(', ')||'none'}); profile unknown.`);
  if(a.package?.index && /KeyBuddy V2 Pilot|Test_IceMage V2/i.test(read(path.join(root,a.package.index))) && a.importProfile==='unknown') a.importProfile='V2 candidate/pending import';
  a.sources.assetPaths.sort(); for(const k of Object.keys(a.sources)) a.sources[k].sort();
}
const actors=[...actorMap.values()].sort((a,b)=>a.id.localeCompare(b.id));
const active=actors.filter(a=>a.lifecycle==='Active'), ready=active.filter(a=>a.requirements.missingRequired.length===0), gaps=active.filter(a=>a.requirements.missingRequired.length>0);
function recommendation(a) {
  const reasons=[]; let score=0;
  if(a.requirements.missingRequired.length) { score+=100+a.requirements.missingRequired.length*10; reasons.push(`필수 애니메이션 공백: ${a.requirements.missingRequired.map(requirementLabel).join(', ')}`); }
  if(a.progress.package.done>0 && !a.packageComplete) { score+=35+a.progress.package.done; reasons.push('기존 패키지 자료가 있어 빠르게 이어갈 수 있음'); }
  if(!a.packageChecklist.index) { score+=20; reasons.push('제작 패키지 인덱스가 없어 다음 작업을 고정하기 좋음'); }
  if(/V2 candidate|unknown/.test(a.importProfile)) { score+=8; reasons.push(`Import 확인 필요: ${a.importProfile}`); }
  const followUp=!a.packageChecklist.master ? '승인 Master 경로 확정/준비' : a.requirements.missingRequired.length ? `${a.requirements.missingRequired.map(requirementLabel).join(', ')} 프레임` : a.nextAction;
  const immediateAction=a.requirements.missingRequired.length
    ? (a.enemyRuntimeAudit && !a.enemyRuntimeAudit.hitReaction.recoveryVisuallyDistinct?'Hit Hold/Recovery Sprite 연결 분리':'필수 애니메이션 프레임 제작')
    : !a.packageChecklist.index?'제작 패키지 인덱스 작성':a.nextAction;
  return { id:a.id, score, reasons, immediateAction, followUp };
}
const recommendations=active.map(recommendation).sort((a,b)=>b.score-a.score || a.id.localeCompare(b.id)).slice(0,3);
const stageCounts=Object.fromEntries(['Candidate','Brief','Master','Animation','Unity','Ready','Hold','Test'].map(stage=>[stage,actors.filter(a=>a.stage===stage).length]));
const scanRevision=crypto.createHash('sha256').update(JSON.stringify({ actors, recommendations, stageCounts })).digest('hex').slice(0,12);
const activePlayers=active.filter(a=>a.type==='Player'), attackPoolGaps=activePlayers.filter(a=>!a.attackPoolAudit.connected);
const activeEnemies=active.filter(a=>a.type==='Enemy'), enemyMotionGaps=activeEnemies.filter(a=>!a.enemyRuntimeAudit.motionQualityReady);
const report={ schemaVersion:2, readOnly:true, root:'.', scanRevision, profiles:cfg.profiles, recommendations, stageCounts, summary:{ total:actors.length, active:active.length, animationRequirementsReady:ready.length, withRequiredGaps:gaps.length, packageComplete:active.filter(a=>a.packageComplete).length, runtimeConnected:active.filter(a=>a.runtimeConnected).length, attackPoolsConnected:activePlayers.filter(a=>a.attackPoolAudit.connected).length, activePlayers:activePlayers.length, enemyRuntimeReady:activeEnemies.filter(a=>a.runtimeConnected).length, enemyMotionQualityReady:activeEnemies.filter(a=>a.enemyRuntimeAudit.motionQualityReady).length, activeEnemies:activeEnemies.length, candidate:actors.filter(a=>a.lifecycle==='Candidate').length, test:actors.filter(a=>a.lifecycle==='Test').length, hold:actors.filter(a=>a.lifecycle==='Hold').length, unmapped:actors.filter(a=>a.world==='Unmapped').length }, actors };
const out=path.join(root,'ProjectDocs/ActorProduction'); fs.mkdirSync(out,{recursive:true});
fs.writeFileSync(path.join(out,'actor-production-index.json'),JSON.stringify(report,null,2)+'\n');
fs.writeFileSync(path.join(out,'dashboard-data.js'),`window.ACTOR_PRODUCTION_DATA = ${JSON.stringify(report,null,2)};\n`);
const q=gaps.sort((a,b)=>a.id.localeCompare(b.id));
const candidateGroups=actors.filter(a=>a.lifecycle==='Candidate').reduce((m,a)=>{const k=a.planned?.tier || (a.type==='Player'?'Player':'Other'); (m[k] ||= []).push(a.id); return m;},{});
const poolGapLine=a=>a.attackPoolAudit.requiredTiers.map(k=>`${requirementLabel(k)}: ${attackPoolStatusLabel(a.attackPoolAudit.tiers[k].status)}`).join(' · ');
const md=[
  '# Actor Production Tracker','',
  '> 로컬 파일을 읽어 만든 읽기 전용 스캔 결과입니다. 패키지 완료도는 새 패키지 규칙으로의 이행 진행률이며, 기존 자산이 없다는 뜻이 아닙니다.','',
  `스캔 리비전: \`${scanRevision}\` · 갱신: \`node Tools/ActorProductionTracker/scan.mjs\` 또는 Tracker 열기 도구를 실행하세요.`,'',
  '## 지금 진행하기 좋은 작업','',
  ...recommendations.map((r,i)=>`${i+1}. **${r.id}** — ${r.immediateAction}; 후속 공백: ${r.followUp}. (${r.reasons.join('; ')})`),'',
  '## 세 가지 진행도','',
  `- 설정/패키지 (엄격 규칙): ${report.summary.packageComplete}/${report.summary.active} Active — index, brief, PerfectPixel 입력, measurements, motion brief, 승인/관찰 master를 모두 포함합니다.`,
  `- 리소스 (필수 애니메이션): ${report.summary.animationRequirementsReady}/${report.summary.active} Active — Candidate/Test는 해당 없음, Hold는 표시만 하고 Active 지표에서 제외합니다.`,
  `- 게임 연결: ${report.summary.runtimeConnected}/${report.summary.active} Active — Player는 MotionProfile + CharacterDefinition + 필수 공격 풀, Enemy는 유효 MotionProfile + table입니다.`,
  `- 공격 풀 연결: ${report.summary.attackPoolsConnected}/${report.summary.activePlayers} Active Player — Tier 2는 Attack A+B 누적 등록까지 확인합니다.`,
  `- 몬스터 런타임: ${report.summary.enemyRuntimeReady}/${report.summary.activeEnemies} Active Enemy · 모션 제작 준비: ${report.summary.enemyMotionQualityReady}/${report.summary.activeEnemies} — Defeat가 비어 있으면 정상적인 Fade-only로 판정합니다.`,'',
  '## 제작 단계','',...Object.entries(stageCounts).map(([stage,count])=>`- ${stage}: ${count}`),'',
  '## 애니메이션 공백 (Active)','',...(q.length?q.map(a=>`- **${a.id}** — ${a.requirements.missingRequired.map(requirementLabel).join(', ')}`):['- 없음']),'',
  '## 공격 Tier 풀 공백 (Active Player)','',...(attackPoolGaps.length?attackPoolGaps.map(a=>`- **${a.id}** — ${poolGapLine(a)}`):['- 없음']),'',
  '## 몬스터 모션 점검 (Active Enemy)','',...activeEnemies.map(a=>`- **${a.id}** — Idle ${a.enemyRuntimeAudit.baseIdle.frameCount}f · Hit ${a.enemyRuntimeAudit.hit.frameCount}f/${enemyRecoveryLabel(a.enemyRuntimeAudit.hitReaction)} · Defeat ${a.enemyRuntimeAudit.defeatMode==='animated'?`${a.enemyRuntimeAudit.defeat.frameCount}f`:'Fade-only'} · ${enemyClassLabel(a.enemyRuntimeAudit.productionClass)}${a.enemyRuntimeAudit.unreferencedMotionFrames.length?` · 미참조 ${a.enemyRuntimeAudit.unreferencedMotionFrames.length}f`:''}`),'',
  '### 몬스터 모션 수정 필요','',...(enemyMotionGaps.length?enemyMotionGaps.map(a=>`- **${a.id}** — ${!a.enemyRuntimeAudit.hitReaction.recoveryVisuallyDistinct?'Hit Hold와 Recovery가 같은 Sprite를 참조':a.warnings.join('; ')}`):['- 없음']),'',
  '## 후보 뱅크 (요약)','',...Object.entries(candidateGroups).sort(([a],[b])=>a.localeCompare(b)).map(([tier,ids])=>`- ${tier}: ${ids.length}명 — ${ids.join(', ')}`),'',
  '## Actor index','',...actors.filter(a=>a.lifecycle!=='Candidate').map(a=>`- **${a.id}** (${a.stage}) — 패키지 ${a.progress.package.percent}% · 리소스 ${a.progress.resources.percent===null?'N/A':`${a.progress.resources.percent}%`} · 게임 ${a.progress.game.percent}% · 누락: ${a.requirements.missingRequired.map(requirementLabel).join(', ')||'없음'}`),''
];
fs.writeFileSync(path.join(out,'actor-production-report.md'),md.join('\n'));
console.log(`Actor Production Tracker: ${actors.length} actors; ${gaps.length} active actors with required gaps.`);

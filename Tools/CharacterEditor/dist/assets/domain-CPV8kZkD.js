import{a as e,c as t,i as n,n as r,o as i,r as a,s as o,t as s}from"./index-CKLfjgFz.js";var c=e=>t.parse(e),l=e=>e&&typeof e==`object`&&`documentKind`in e&&e.documentKind===`actor-export`?o.parse(e.authored):o.parse(e),u=e=>!!e&&typeof e==`object`&&!Array.isArray(e);function d(e,t){if(!u(e)||!u(t))return t===void 0?e:t;let n={...e};for(let[e,r]of Object.entries(t))r!==void 0&&(n[e]=u(r)&&u(n[e])?d(n[e],r):r);return n}function f(e,t=``){return u(e)?Object.entries(e).flatMap(([e,n])=>f(n,t?`${t}.${e}`:e)):t?[t]:[]}function p(e,t){if(e.worldRef.worldId!==t.worldId||e.worldRef.version!==t.revision)throw Error(`Actor pins ${e.worldRef.worldId} v${e.worldRef.version}, not ${t.worldId} v${t.revision}`);let n=s(d(t.defaults,e.overrides)),r={};for(let e of f(t.defaults))r[e]={source:`world`,documentId:t.worldId,version:t.revision};for(let t of f(e.overrides))r[t]={source:`actor`,documentId:e.actorId,version:e.revision};return m(r),{...e,resolved:n,fieldOrigins:r}}function m(e){let t=e[`anatomy.targetPhysicalHeightPx`],n=e[`anatomy.targetLogicalHeightPx`];if(t?e[`anatomy.targetLogicalHeightPx`]=t:n&&(e[`anatomy.targetPhysicalHeightPx`]=n),!e[`pixelStyle.densityPreset`]){let t=e[`pixelStyle.logicalBlockPx.widthPx`];t&&(e[`pixelStyle.densityPreset`]=t)}}function h(e,t){let n=structuredClone(e),r=t.split(`.`),i=n.overrides;for(let e=0;e<r.length-1;e++){let t=i[r[e]];if(!u(t))return n;i=t}return delete i[r.at(-1)],n}var g={required:`required-field`,conflation:`stature-species-scale-conflation`,torso:`normal-build-wide-torso`,proportion:`proportion-mismatch`,canvas:`large-weapon-canvas`,unity:`unity-scale-not-one`,weapon:`weapon-family-not-allowed`,extremity:`extremity-size-delta`,largeMotion:`large-motion-canvas-exception`,ppu:`ppu-not-200`,floating:`floating-pivot-mismatch`,constraints:`missing-design-constraints`,alias:`actor-id-alias-conflict`,densityBlock:`non-square-density-block`,densityResidual:`physical-height-not-block-multiple`,densityBackCalc:`density-override-without-physical-height`},_=(e,t)=>e.some(e=>e.active&&e.ruleId===t&&e.reason.trim().length>=10);function v(e,t,n,r,i,a){let o=i&&_(e.approvedExceptions,t);return{ruleId:t,severity:n,message:r,overridable:i,exceptionApproved:o,blocksExport:n===`error`&&!o,path:a}}var y={xs:0,s:1,m:2,l:3,xl:4};function b(e,t,n=[]){let r;try{r=p(e,t).resolved}catch(t){return[v(e,g.required,`error`,t.message,!1,`worldRef`)]}let a=[],o=r.anatomy,s=r.production;(!e.actorId||!e.identity.species||!e.identity.role||!e.identity.concept)&&a.push(v(e,g.required,`error`,`One or more required identity fields are missing.`,!1));let c=i(o,r.pixelStyle),l=i(t.defaults.anatomy,t.defaults.pixelStyle),u=l.targetPhysicalHeightPx>0?c.targetPhysicalHeightPx/l.targetPhysicalHeightPx:0;Math.abs(o.speciesScale-1)>.05&&Math.abs(u-o.speciesScale)<.1&&a.push(v(e,g.conflation,`warning`,`Species scale appears derived from actor height; confirm species-wide evidence.`,!0,`anatomy.speciesScale`)),c.squareBlock||a.push(v(e,g.densityBlock,`error`,`Logical block is ${r.pixelStyle.logicalBlockPx.widthPx}×${r.pixelStyle.logicalBlockPx.heightPx}; production requires a square block.`,!1,`pixelStyle.logicalBlockPx`)),c.roundingResidualPx!==0&&a.push(v(e,g.densityResidual,`warning`,`Target physical height ${c.targetPhysicalHeightPx}px is not a multiple of the ${c.blockPx}px block, so the logical height rounds to ${c.targetLogicalHeightPx}. Production still builds the body at ${c.targetPhysicalHeightPx}px, but tools that read only the logical height will compute ${c.effectivePhysicalHeightPx}px.`,!0,`anatomy.targetPhysicalHeightPx`));let d=e.overrides.anatomy?.targetPhysicalHeightPx!==void 0||t.defaults.anatomy.targetPhysicalHeightPx!==void 0;(e.overrides.pixelStyle?.logicalBlockPx!==void 0||e.overrides.pixelStyle?.densityPreset!==void 0)&&!d&&a.push(v(e,g.densityBackCalc,`warning`,`Pixel density is overridden but no target physical height is recorded; height was back-calculated as ${c.targetPhysicalHeightPx}px from the logical height at ${c.blockPx}px per logical pixel. Set the physical height to pin the body size.`,!0,`anatomy.targetPhysicalHeightPx`)),o.build===`normal`&&[`broad`,`very-broad`].includes(o.torsoWidth)&&a.push(v(e,g.torso,`warning`,`Normal build conflicts with a broad torso width.`,!0,`anatomy.torsoWidth`)),e.equipment.weapon?.estimatedOccupancyPercent&&e.equipment.weapon.estimatedOccupancyPercent>60&&a.push(v(e,g.canvas,`warning`,`Estimated weapon occupancy is ${e.equipment.weapon.estimatedOccupancyPercent}%.`,!0,`equipment.weapon`)),s.unityVisualScale!==1&&a.push(v(e,g.unity,`error`,`Unity visual scale is ${s.unityVisualScale}; project policy is 1.0.`,!0,`production.unityVisualScale`));let f=e.equipment.weapon?.family;f&&!e.equipment.allowedWeaponFamilies.includes(f)&&a.push(v(e,g.weapon,`error`,`${f} is not allowed for ${e.actorId}.`,!0,`equipment.weapon.family`)),s.pixelsPerUnit!==200&&a.push(v(e,g.ppu,`error`,`PPU is ${s.pixelsPerUnit}; project policy is 200.`,!1,`production.pixelsPerUnit`)),o.isFloatingActor&&s.pivotRule===`forward-foot-contact`&&a.push(v(e,g.floating,`error`,`Floating actors must use ground-projection or a custom actor origin.`,!1,`production.pivotRule`));let m=s.largeMotionCanvas;m.policy===`explicit`&&(m.widthPx!==s.baseCanvas.widthPx||m.heightPx!==s.baseCanvas.heightPx)&&a.push(v(e,g.largeMotion,`warning`,`Large-motion canvas differs from the base 512 policy.`,!0,`production.largeMotionCanvas`)),(!e.constraints.invariants.length||!e.constraints.forbidden.length)&&a.push(v(e,g.constraints,`warning`,`Invariant or forbidden design constraints are empty.`,!1,`constraints`)),e.resourceFolderPath&&!e.resourceFolderPath.split(`/`).at(-1)?.toLowerCase().includes(e.actorId.toLowerCase())&&a.push(v(e,g.alias,`info`,`Actor ID differs from resource folder ${e.resourceFolderPath}.`,!1,`actorId`));for(let t of n){if(t.actor.actorId===e.actorId||t.actor.worldRef.worldId!==e.worldRef.worldId)continue;let n=p(t.actor,t.world).resolved;t.actor.identity.species===e.identity.species&&n.anatomy.proportionTemplateId!==o.proportionTemplateId&&a.push(v(e,g.proportion,`warning`,`${t.actor.actorId} uses proportion ${n.anatomy.proportionTemplateId}.`,!0,`anatomy.proportionTemplateId`)),t.actor.identity.species===e.identity.species&&n.anatomy.build===o.build&&n.anatomy.proportionTemplateId===o.proportionTemplateId&&[`headSize`,`handSize`,`footSize`].some(e=>Math.abs(y[o[e]]-y[n.anatomy[e]])>=2)&&a.push(v(e,g.extremity,`warning`,`Extremity sizes differ substantially from ${t.actor.actorId}.`,!0,`anatomy`))}return a}var x=e=>!e.some(e=>e.blocksExport),S=(e,t,n,r)=>{let i=n===r;return typeof n!=`number`||typeof r!=`number`?{key:e,label:t,draft:n,reference:r,matches:i}:{key:e,label:t,draft:n,reference:r,matches:i,absoluteDelta:n-r,percentDelta:r?(n-r)/r*100:void 0}};function C(e,t,n){if(e.worldRef.worldId!==t.worldRef.worldId)throw Error(`Comparison requires actors from the same world`);let r=p(e,n).resolved,a=p(t,n).resolved,o=r.production.largeMotionCanvas,s=a.production.largeMotionCanvas,c=i(r.anatomy,r.pixelStyle),l=i(a.anatomy,a.pixelStyle),u=[S(`stature`,`Stature`,r.anatomy.stature,a.anatomy.stature),S(`physicalHeight`,`Physical height`,c.targetPhysicalHeightPx,l.targetPhysicalHeightPx),S(`density`,`Pixel density`,`${c.blockPx}×${c.blockPx}`,`${l.blockPx}×${l.blockPx}`),S(`height`,`Logical height`,r.anatomy.targetLogicalHeightPx,a.anatomy.targetLogicalHeightPx),S(`build`,`Build`,r.anatomy.build,a.anatomy.build),S(`proportion`,`Proportion`,r.anatomy.proportionTemplateId,a.anatomy.proportionTemplateId),...[`headSize`,`handSize`,`footSize`,`torsoWidth`].map(e=>S(e,e,r.anatomy[e],a.anatomy[e])),S(`speciesScale`,`Species scale`,r.anatomy.speciesScale,a.anatomy.speciesScale),S(`weaponOccupancy`,`Weapon occupancy`,e.equipment.weapon?.estimatedOccupancyPercent??0,t.equipment.weapon?.estimatedOccupancyPercent??0),S(`baseCanvas`,`Base canvas`,`${r.production.baseCanvas.widthPx}×${r.production.baseCanvas.heightPx}`,`${a.production.baseCanvas.widthPx}×${a.production.baseCanvas.heightPx}`),S(`largeMotionCanvas`,`Large-motion canvas`,o.policy===`explicit`?`${o.widthPx}×${o.heightPx}`:`same-as-base`,s.policy===`explicit`?`${s.widthPx}×${s.heightPx}`:`same-as-base`)];return{referenceActorId:t.actorId,metrics:u,diagnostics:b(e,n,[{actor:t,world:n}])}}function w(e){return Array.isArray(e)?e.map(w):e&&typeof e==`object`?Object.fromEntries(Object.keys(e).sort().map(t=>[t,w(e[t])])):e}var T=e=>`${JSON.stringify(w(e),null,2)}\n`;function E(e){return T(e)}function D(e,t,n=[]){let{resolved:r,fieldOrigins:a}=p(e,t),o=b(e,t,n.map(e=>({actor:e,world:t}))),s=e.equipment.weapon?.lengthToBodyRatio,c=i(r.anatomy,r.pixelStyle),l=i(t.defaults.anatomy,t.defaults.pixelStyle);return{schemaVersion:`1.0.0`,documentKind:`actor-export`,authored:e,resolved:r,fieldOrigins:a,calculated:{heightFromWorldBaselinePercent:(c.targetPhysicalHeightPx/l.targetPhysicalHeightPx-1)*100,targetPhysicalHeightPx:c.targetPhysicalHeightPx,targetLogicalHeightPx:c.targetLogicalHeightPx,logicalPixelBlockPx:c.blockPx,densityPreset:c.densityPreset,effectivePhysicalHeightPx:c.effectivePhysicalHeightPx,physicalHeightResidualPx:c.roundingResidualPx,weaponLengthLogicalPx:s?c.targetLogicalHeightPx*s:null,weaponLengthPhysicalPx:s?c.targetPhysicalHeightPx*s:null,weaponOccupancyPercent:e.equipment.weapon?.estimatedOccupancyPercent??null},interpretations:[`Species scale is independent of stature and logical height.`,`Unity visual scale is a runtime display transform and is normally 1.0.`,`Canvas pixels, logical silhouette pixels, and logical pixel block size are distinct measurements.`,`Target physical height is the authoritative body size; logical height is derived as physical height divided by the pixel density block.`,`Changing pixel density changes detail, not how large the character is.`],diagnostics:o,comparison:n[0]?C(e,n[0],t):void 0}}var O=e=>T(e);function k(e){let t=e.authored,n=e.resolved,r=t.equipment.weapon,i=t=>e.fieldOrigins[t]?.source===`actor`?`Override`:`World`,a=e.diagnostics.map(e=>`- **${e.severity.toUpperCase()}** \`${e.ruleId}\`: ${e.message}${e.exceptionApproved?` — Approved exception`:``}`).join(`
`)||`- None`,o=t.approvedExceptions.filter(e=>e.active).map(e=>`- \`${e.ruleId}\`: ${e.reason}`).join(`
`)||`- None`,s=e.comparison?.metrics.map(e=>`| ${e.label} | ${e.draft} | ${e.reference} | ${e.matches??e.draft===e.reference?`Match`:`Mismatch`} | ${e.absoluteDelta===void 0?`—`:`${e.absoluteDelta>=0?`+`:``}${e.absoluteDelta}${e.percentDelta===void 0?``:` (${e.percentDelta.toFixed(1)}%)`}`} |`).join(`
`);return`# ${t.actorId} — Character Sheet

- Display name: ${t.displayName.ko?`${t.displayName.ko} / `:``}${t.displayName.en}
- Type: ${t.actorType} · World: \`${t.worldRef.worldId}\` v${t.worldRef.version}
- Aliases: ${t.aliases.length?t.aliases.join(`, `):`None`}
- Species: ${t.identity.species} · Role: ${t.identity.role} · Status: ${t.identity.status}
- Concept: ${t.identity.concept}

## Body & Proportions

| Field | Value | Origin |
|---|---:|---|
| Stature | ${n.anatomy.stature} | ${i(`anatomy.stature`)} |
| Target physical height | ${e.calculated.targetPhysicalHeightPx}px | ${i(`anatomy.targetPhysicalHeightPx`)} |
| Pixel density | ${e.calculated.densityPreset} (${e.calculated.logicalPixelBlockPx}×${e.calculated.logicalPixelBlockPx}) | ${i(`pixelStyle.densityPreset`)} |
| Target logical height | ${n.anatomy.targetLogicalHeightPx}px | Derived |
| Build | ${n.anatomy.build} | ${i(`anatomy.build`)} |
| Proportion | ${n.anatomy.proportionTemplateId} | ${i(`anatomy.proportionTemplateId`)} |
| Species scale | ${n.anatomy.speciesScale} | ${i(`anatomy.speciesScale`)} |
| Head / hand / foot / torso | ${n.anatomy.headSize} / ${n.anatomy.handSize} / ${n.anatomy.footSize} / ${n.anatomy.torsoWidth} | mixed |

## Look

- Physical traits: ${t.physicalTraits.join(`; `)||`None`}
- Hair / eyes / skin: ${t.appearance.hair??`N/A`} / ${t.appearance.eyes??`N/A`} / ${t.appearance.skin??`N/A`}
- Clothing: ${t.appearance.clothing.join(`; `)||`None`}
- Materials: ${t.appearance.materials.join(`; `)||`None`}
- Decorations: ${t.appearance.decorations.join(`; `)||`None`}
- Invariants: ${t.constraints.invariants.join(`; `)||`None`}
- Forbidden: ${t.constraints.forbidden.join(`; `)||`None`}

## Weapon & Equipment

- Weapon: ${r?`${r.family}, ${r.sizeClass}, count ${r.count}`:`None`}
- Hands: ${r?`${r.mainHand} / ${r.offHand}`:`N/A`}
- Structure: ${r?.structure??`N/A`}
- Allowed families: ${t.equipment.allowedWeaponFamilies.join(`, `)||`None`}
- Secondary: ${t.equipment.secondary.join(`, `)||`None`}

## Production & Canvas

- Base canvas: ${n.production.baseCanvas.widthPx}×${n.production.baseCanvas.heightPx}
- Large-motion canvas: ${n.production.largeMotionCanvas.policy===`explicit`?`${n.production.largeMotionCanvas.widthPx}×${n.production.largeMotionCanvas.heightPx}`:`same as base`}
- Logical block: ${n.pixelStyle.logicalBlockPx.widthPx}×${n.pixelStyle.logicalBlockPx.heightPx} (${e.calculated.densityPreset})
- Body height: ${e.calculated.targetPhysicalHeightPx}px physical → ${e.calculated.targetLogicalHeightPx} logical px${e.calculated.physicalHeightResidualPx?` (lands on ${e.calculated.effectivePhysicalHeightPx}px)`:``}
- Pivot: ${n.production.pivotRule}${n.production.pivot?` (${n.production.pivot.xNormalized}, ${n.production.pivot.yNormalized}; ${n.production.pivot.source})`:``}
- PPU: ${n.production.pixelsPerUnit}
- Unity visual scale: ${n.production.unityVisualScale}
- Layers: ${n.production.layers.join(` → `)}

## Validation Summary

${a}

## Approved Exceptions

${o}
${e.comparison?`
## Comparison Snapshot (vs ${e.comparison.referenceActorId})

| Metric | Actor | Reference | Result | Delta |
|---|---:|---:|:---:|---:|
${s}
`:``}
## Calculated Values and Interpretations

- Height relative to world baseline: ${Number(e.calculated.heightFromWorldBaselinePercent).toFixed(1)}%
- Weapon logical length estimate: ${e.calculated.weaponLengthLogicalPx??`N/A`}
${e.interpretations.map(e=>`- ${e}`).join(`
`)}

## Schema

- schemaVersion: \`${e.schemaVersion}\`
- worldTemplateRef: \`${t.worldRef.worldId}\` v${t.worldRef.version}
`}export{g as RULE_IDS,s as applyResolvedScale,r as blockForPreset,a as blockSizeOf,D as buildExport,x as canExport,C as compareActors,O as exportJson,k as exportMarkdown,n as logicalHeightAt,l as parseActor,c as parseWorld,e as presetForBlock,h as removeOverride,p as resolveActor,i as resolveScale,E as serializeActor,b as validateActor};
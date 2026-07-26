import { useCallback, useEffect, useMemo, useState } from "react";
import type { ActorDocument, ActorReference, RuleId, WorldTemplate } from "./types";
import { DomainApiContext, type DomainApi } from "./domainApi";
import { loadRealDomainApi } from "./realDomainApi";
import { loadRealPersistenceApi, type PersistenceApi } from "./realPersistenceApi";
import { loadSampleLibrary } from "./sampleLibrary";
import { createBlankActor, removeApprovedException, setBodyScale, upsertApprovedException } from "./actorDraft";
import { createBlankWorld, draftNextRevisionOf } from "./worldDraft";
import { DENSITY_PRESETS, logicalHeightAt, resolveScale, type DensityPresetId } from "./scale";

import { AppShell, type Breadcrumb } from "../components/layout/AppShell";
import { StatusBanner } from "../components/common/StatusBanner";
import { LibraryHome } from "../components/home/LibraryHome";
import { NewActorWorldPicker } from "../components/home/NewActorWorldPicker";
import { WorldTemplateForm } from "../components/world/WorldTemplateForm";
import { ActorEditorShell } from "../components/actor/ActorEditorShell";
import { ComparisonView } from "../components/comparison/ComparisonView";
import { ExportPreview } from "../components/export/ExportPreview";
import { ImportDialog } from "../components/import/ImportDialog";

import "../styles/index.css";

type View =
  | { kind: "home" }
  | { kind: "world-form"; mode: "create" | "edit"; initial: WorldTemplate; sourceNote?: string }
  | { kind: "new-actor-pick-world" }
  | { kind: "actor-editor"; actor: ActorDocument }
  | { kind: "comparison"; actor: ActorDocument; referenceId: string | null }
  | { kind: "export"; actor: ActorDocument }
  | { kind: "import" };

function upsertWorldList(list: WorldTemplate[], world: WorldTemplate): WorldTemplate[] {
  const filtered = list.filter((entry) => !(entry.worldId === world.worldId && entry.revision === world.revision));
  return [...filtered, world];
}

function findWorldByRef(worlds: WorldTemplate[], ref: { worldId: string; version: number }): WorldTemplate | undefined {
  return worlds.find((world) => world.worldId === ref.worldId && world.revision === ref.version);
}

export function App() {
  const [loadStatus, setLoadStatus] = useState<"loading" | "ready" | "error">("loading");
  const [loadError, setLoadError] = useState<string | null>(null);
  const [domainApi, setDomainApi] = useState<DomainApi | null>(null);
  const [persistenceApi, setPersistenceApi] = useState<PersistenceApi | null>(null);
  const [bundledWorlds, setBundledWorlds] = useState<WorldTemplate[]>([]);
  const [bundledActors, setBundledActors] = useState<ActorDocument[]>([]);
  const [customWorlds, setCustomWorlds] = useState<WorldTemplate[]>([]);
  const [view, setView] = useState<View>({ kind: "home" });
  const [draftsVersion, setDraftsVersion] = useState(0);
  const [draftSavedAt, setDraftSavedAt] = useState<string | null>(null);
  const [worldFormErrors, setWorldFormErrors] = useState<string[] | undefined>(undefined);
  const [importErrors, setImportErrors] = useState<string[] | undefined>(undefined);
  const [runtimeError, setRuntimeError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.all([loadRealDomainApi(), loadRealPersistenceApi(), loadSampleLibrary()])
      .then(([api, persistence, library]) => {
        if (cancelled) return;
        setDomainApi(api);
        setPersistenceApi(persistence);
        setBundledWorlds(library.worlds);
        setBundledActors(library.actors);
        const custom = persistence
          .listWorldDrafts()
          .map((ref) => persistence.loadWorldDraft(ref.worldId, ref.version))
          .filter((w): w is WorldTemplate => w !== null);
        setCustomWorlds(custom);
        setLoadStatus("ready");
      })
      .catch((error: unknown) => {
        if (cancelled) return;
        setLoadError(error instanceof Error ? error.message : String(error));
        setLoadStatus("error");
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const worlds = useMemo(() => [...bundledWorlds, ...customWorlds], [bundledWorlds, customWorlds]);

  const drafts = useMemo(() => {
    if (!persistenceApi) return [];
    return persistenceApi
      .listActorDraftIds()
      .map((id) => persistenceApi.loadActorDraft(id))
      .filter((a): a is ActorDocument => a !== null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [persistenceApi, draftsVersion]);

  function getReferenceActors(worldId: string, excludeActorId: string): ActorDocument[] {
    const fromBundle = bundledActors.filter(
      (actor) => actor.worldRef.worldId === worldId && actor.actorId !== excludeActorId,
    );
    const fromDrafts = drafts.filter(
      (actor) => actor.worldRef.worldId === worldId && actor.actorId !== excludeActorId,
    );
    const seen = new Set<string>();
    return [...fromBundle, ...fromDrafts].filter((actor) => {
      if (seen.has(actor.actorId)) return false;
      seen.add(actor.actorId);
      return true;
    });
  }

  const goHome = useCallback(() => setView({ kind: "home" }), []);

  const persistDraftNow = useCallback(
    (actor: ActorDocument) => {
      if (!persistenceApi || !actor.actorId) return;
      persistenceApi.saveActorDraft(actor);
      setDraftSavedAt(actor.updatedAt);
      setDraftsVersion((value) => value + 1);
    },
    [persistenceApi],
  );

  // Autosave drafts to localStorage (spec section 5) once the actor has an ID.
  useEffect(() => {
    if (view.kind !== "actor-editor" || !persistenceApi) return;
    const timer = setTimeout(() => persistDraftNow(view.actor), 600);
    return () => clearTimeout(timer);
  }, [view, persistenceApi, persistDraftNow]);

  const currentActor: ActorDocument | null =
    view.kind === "actor-editor" || view.kind === "comparison" || view.kind === "export" ? view.actor : null;

  const currentWorld = currentActor ? findWorldByRef(worlds, currentActor.worldRef) : undefined;

  const references = useMemo(() => {
    if (!currentActor) return [];
    return getReferenceActors(currentActor.worldRef.worldId, currentActor.actorId);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentActor, bundledActors, drafts]);

  const referenceActorPairs: ActorReference[] = useMemo(
    () => (currentWorld ? references.map((actor) => ({ actor, world: currentWorld })) : []),
    [references, currentWorld],
  );

  let resolvedActor: ReturnType<DomainApi["resolveActor"]> | null = null;
  let resolveFailure: string | null = null;
  if (domainApi && currentActor && currentWorld) {
    try {
      resolvedActor = domainApi.resolveActor(currentActor, currentWorld);
    } catch (error) {
      resolveFailure = error instanceof Error ? error.message : String(error);
    }
  }

  const diagnostics = useMemo(() => {
    if (!domainApi || !currentActor || !currentWorld || !resolvedActor) return [];
    try {
      return domainApi.validateActor(currentActor, currentWorld, referenceActorPairs);
    } catch {
      return [];
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [domainApi, currentActor, currentWorld, referenceActorPairs, resolvedActor]);

  function onChangeActor(updater: (actor: ActorDocument) => ActorDocument) {
    setView((current) => (current.kind === "actor-editor" ? { ...current, actor: updater(current.actor) } : current));
  }

  function handleApproveException(ruleId: RuleId, reason: string) {
    onChangeActor((actor) => upsertApprovedException(actor, ruleId, reason));
  }

  function handleRemoveException(ruleId: RuleId) {
    onChangeActor((actor) => removeApprovedException(actor, ruleId));
  }

  function openWorldForCreate() {
    setWorldFormErrors(undefined);
    setView({ kind: "world-form", mode: "create", initial: createBlankWorld() });
  }

  function openWorldForEdit(world: WorldTemplate) {
    setWorldFormErrors(undefined);
    setView({
      kind: "world-form",
      mode: "edit",
      initial: draftNextRevisionOf(world),
      sourceNote: `기존 rev.${world.revision}을 기반으로 편집합니다. 저장하면 새 버전 rev.${world.revision + 1}이 생성됩니다 (World 템플릿은 불변 버전 문서입니다).`,
    });
  }

  function handleSaveWorld(world: WorldTemplate) {
    if (!domainApi || !persistenceApi) return;
    const result = domainApi.parseWorld(world);
    if (!result.success) {
      setWorldFormErrors(result.errors);
      return;
    }
    setCustomWorlds((current) => upsertWorldList(current, result.data));
    persistenceApi.saveWorldDraft(result.data);
    setWorldFormErrors(undefined);
    goHome();
  }

  function handleDownloadWorld(world: WorldTemplate) {
    persistenceApi?.downloadText(`${world.worldId || "world"}.rev${world.revision}.world.json`, JSON.stringify(world, null, 2));
  }

  function openNewActorPicker() {
    setView({ kind: "new-actor-pick-world" });
  }

  function pickWorldForNewActor(
    world: WorldTemplate,
    actorType: ActorDocument["actorType"],
    densityPreset: DensityPresetId,
  ) {
    const blank = createBlankActor({ worldId: world.worldId, version: world.revision }, actorType);
    const worldScale = resolveScale(world.defaults.anatomy, world.defaults.pixelStyle);
    // A density other than the world's is stored as an override that pins the
    // world's physical height, so the new actor is the same size as its peers
    // and only its pixel grid differs.
    const actor =
      densityPreset === "custom" || densityPreset === worldScale.densityPreset
        ? blank
        : setBodyScale(blank, {
            targetPhysicalHeightPx: worldScale.targetPhysicalHeightPx,
            targetLogicalHeightPx: logicalHeightAt(worldScale.targetPhysicalHeightPx, DENSITY_PRESETS[densityPreset]),
            densityPreset,
            blockPx: DENSITY_PRESETS[densityPreset],
          });
    setDraftSavedAt(null);
    setView({ kind: "actor-editor", actor });
  }

  function openActor(actor: ActorDocument) {
    setDraftSavedAt(null);
    setView({ kind: "actor-editor", actor });
  }

  function openDraft(actor: ActorDocument) {
    setDraftSavedAt(actor.updatedAt);
    setView({ kind: "actor-editor", actor });
  }

  function handleDeleteDraft(actorId: string) {
    persistenceApi?.deleteActorDraft(actorId);
    setDraftsVersion((value) => value + 1);
  }

  function openImport() {
    setImportErrors(undefined);
    setView({ kind: "import" });
  }

  function handleImportRaw(raw: unknown) {
    if (!domainApi) return;
    const actorResult = domainApi.parseActor(raw);
    if (actorResult.success) {
      setImportErrors(undefined);
      setDraftSavedAt(null);
      setView({ kind: "actor-editor", actor: actorResult.data });
      return;
    }
    const worldResult = domainApi.parseWorld(raw);
    if (worldResult.success) {
      setCustomWorlds((current) => upsertWorldList(current, worldResult.data));
      persistenceApi?.saveWorldDraft(worldResult.data);
      setImportErrors(undefined);
      goHome();
      return;
    }
    setImportErrors([...actorResult.errors, ...worldResult.errors]);
  }

  let breadcrumbs: Breadcrumb[] = [{ label: "Home", onClick: view.kind === "home" ? undefined : goHome }];
  if (view.kind === "world-form") {
    breadcrumbs = [...breadcrumbs, { label: view.mode === "create" ? "새 World 템플릿" : "World 템플릿 편집" }];
  } else if (view.kind === "new-actor-pick-world") {
    breadcrumbs = [...breadcrumbs, { label: "새 액터" }];
  } else if (view.kind === "import") {
    breadcrumbs = [...breadcrumbs, { label: "JSON 가져오기" }];
  } else if (currentActor) {
    const label = currentActor.displayName.ko || currentActor.displayName.en || currentActor.actorId || "액터 편집";
    if (view.kind === "actor-editor") {
      breadcrumbs = [...breadcrumbs, { label }];
    } else {
      breadcrumbs = [
        ...breadcrumbs,
        { label, onClick: () => setView({ kind: "actor-editor", actor: currentActor }) },
        { label: view.kind === "comparison" ? "Comparison" : "Export" },
      ];
    }
  }

  return (
    <DomainApiContext.Provider value={domainApi}>
      <AppShell breadcrumbs={breadcrumbs}>
        {loadStatus === "loading" && (
          <StatusBanner tone="info" title="Character Editor를 불러오는 중입니다...">
            도메인 모듈과 샘플 데이터를 불러오고 있습니다.
          </StatusBanner>
        )}

        {loadStatus === "error" && (
          <StatusBanner tone="error" title="도메인 모듈을 연결하지 못했습니다.">
            {loadError} — Wave 2는 UI(Claude)와 도메인/스키마/데이터(Codex)가 병렬로 개발됩니다. Codex
            워커의 산출물이 아직 반영되지 않았을 수 있습니다. 통합 담당자에게 확인해 주세요.
          </StatusBanner>
        )}

        {loadStatus === "ready" && domainApi && persistenceApi && (
          <>
            {runtimeError && (
              <StatusBanner tone="error" title="예상치 못한 오류가 발생했습니다.">
                {runtimeError}
              </StatusBanner>
            )}

            {view.kind === "home" && (
              <LibraryHome
                worlds={worlds}
                actors={bundledActors}
                drafts={drafts}
                onOpenWorld={openWorldForEdit}
                onNewWorld={openWorldForCreate}
                onOpenActor={openActor}
                onNewActor={openNewActorPicker}
                onOpenDraft={openDraft}
                onDeleteDraft={handleDeleteDraft}
                onImport={openImport}
              />
            )}

            {view.kind === "world-form" && (
              <WorldTemplateForm
                mode={view.mode}
                initialWorld={view.initial}
                sourceNote={view.sourceNote}
                validationErrors={worldFormErrors}
                onSave={handleSaveWorld}
                onDownload={handleDownloadWorld}
                onCancel={goHome}
              />
            )}

            {view.kind === "new-actor-pick-world" && (
              <NewActorWorldPicker worlds={worlds} onPick={pickWorldForNewActor} onCancel={goHome} />
            )}

            {view.kind === "import" && (
              <ImportDialog onImportRaw={handleImportRaw} errors={importErrors} onCancel={goHome} />
            )}

            {view.kind === "actor-editor" &&
              (currentWorld && resolvedActor ? (
                <ActorEditorShell
                  actor={view.actor}
                  world={currentWorld}
                  resolved={resolvedActor.resolved}
                  diagnostics={diagnostics}
                  onChangeActor={onChangeActor}
                  onApproveException={handleApproveException}
                  onRemoveException={handleRemoveException}
                  onSaveDraft={() => persistDraftNow(view.actor)}
                  onGoCompare={() => setView({ kind: "comparison", actor: view.actor, referenceId: null })}
                  onGoExport={() => setView({ kind: "export", actor: view.actor })}
                  draftSavedAt={draftSavedAt}
                />
              ) : (
                <StatusBanner tone="error" title="이 액터가 참조하는 World 템플릿을 찾을 수 없습니다.">
                  {view.actor.worldRef.worldId} v{view.actor.worldRef.version}
                  {resolveFailure && ` — ${resolveFailure}`}
                </StatusBanner>
              ))}

            {view.kind === "comparison" &&
              (() => {
                const candidates = references.filter(
                  (actor) => actor.identity.status === "master" || actor.identity.status === "active",
                );
                const reference = view.referenceId
                  ? candidates.find((actor) => actor.actorId === view.referenceId) ?? null
                  : null;
                let comparisonResult = null;
                if (reference && currentWorld) {
                  try {
                    comparisonResult = domainApi.compareActors(view.actor, reference, currentWorld);
                  } catch (error) {
                    comparisonResult = null;
                    if (!runtimeError) setRuntimeError(error instanceof Error ? error.message : String(error));
                  }
                }
                return (
                  <ComparisonView
                    actor={view.actor}
                    candidates={candidates}
                    selectedReferenceId={view.referenceId}
                    comparison={comparisonResult}
                    onSelectReference={(actorId) =>
                      setView((current) =>
                        current.kind === "comparison" ? { ...current, referenceId: actorId } : current,
                      )
                    }
                    onBack={() => setView({ kind: "actor-editor", actor: view.actor })}
                  />
                );
              })()}

            {view.kind === "export" &&
              (() => {
                if (!currentWorld) {
                  return (
                    <StatusBanner tone="error" title="이 액터가 참조하는 World 템플릿을 찾을 수 없습니다." />
                  );
                }
                const envelope = domainApi.buildExport(view.actor, currentWorld, references);
                const jsonText = domainApi.exportJson(envelope);
                const markdownText = domainApi.exportMarkdown(envelope);
                return (
                  <ExportPreview
                    envelope={envelope}
                    jsonText={jsonText}
                    markdownText={markdownText}
                    onDownloadJson={() =>
                      persistenceApi.downloadText(`${view.actor.actorId}.character.json`, jsonText)
                    }
                    onDownloadMarkdown={() =>
                      persistenceApi.downloadText(`${view.actor.actorId}.character.md`, markdownText, "text/markdown")
                    }
                    onBack={() => setView({ kind: "actor-editor", actor: view.actor })}
                  />
                );
              })()}
          </>
        )}
      </AppShell>
    </DomainApiContext.Provider>
  );
}

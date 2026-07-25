import type { ActorDocument, WorldTemplate } from "../../app/types";
import { setIdentity, setTopLevel } from "../../app/actorDraft";
import { FieldRow } from "../common/FieldRow";
import { TagListEditor } from "../common/TagListEditor";

type ActorType = ActorDocument["actorType"];
type Sex = ActorDocument["identity"]["sex"];
type AgeGroup = ActorDocument["identity"]["ageGroup"];
type Status = ActorDocument["identity"]["status"];

const ACTOR_TYPE_OPTIONS: ActorType[] = ["character", "monster"];
const SEX_OPTIONS: Sex[] = ["female", "male", "intersex", "none", "unknown"];
const AGE_OPTIONS: AgeGroup[] = ["child", "adolescent", "adult", "elder", "ageless", "unknown"];
const STATUS_OPTIONS: Status[] = ["concept", "master", "active", "hold"];

export interface IdentitySectionProps {
  actor: ActorDocument;
  world: WorldTemplate;
  onChangeActor: (updater: (actor: ActorDocument) => ActorDocument) => void;
}

export function IdentitySection({ actor, world, onChangeActor }: IdentitySectionProps) {
  const identity = actor.identity;

  return (
    <div className="ce-card">
      <h3 className="ce-card-title">Identity</h3>
      <p className="ce-card-subtitle">액터의 기본 신원 정보입니다. 세계관 상속과는 무관하게 항상 직접 입력합니다.</p>

      <div className="ce-form-grid" style={{ marginTop: "var(--ce-space-3)" }}>
        <FieldRow label="Actor ID" required htmlFor="actor-id" hint="Unity 자산 폴더명과 일치하는 것을 권장합니다 (예: ElfGuardian).">
          <input
            id="actor-id"
            type="text"
            value={actor.actorId}
            onChange={(event) => onChangeActor((current) => setTopLevel(current, { actorId: event.target.value }))}
          />
        </FieldRow>
        <FieldRow label="유형" required htmlFor="actor-type">
          <select
            id="actor-type"
            value={actor.actorType}
            onChange={(event) =>
              onChangeActor((current) => setTopLevel(current, { actorType: event.target.value as ActorType }))
            }
          >
            {ACTOR_TYPE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option === "character" ? "Character" : "Monster"}
              </option>
            ))}
          </select>
        </FieldRow>
        <FieldRow label="표시 이름 (한국어)" htmlFor="actor-name-ko">
          <input
            id="actor-name-ko"
            type="text"
            value={actor.displayName.ko ?? ""}
            onChange={(event) =>
              onChangeActor((current) =>
                setTopLevel(current, { displayName: { ...current.displayName, ko: event.target.value } }),
              )
            }
          />
        </FieldRow>
        <FieldRow label="표시 이름 (영어)" required htmlFor="actor-name-en">
          <input
            id="actor-name-en"
            type="text"
            value={actor.displayName.en}
            onChange={(event) =>
              onChangeActor((current) =>
                setTopLevel(current, { displayName: { ...current.displayName, en: event.target.value } }),
              )
            }
          />
        </FieldRow>
        <FieldRow label="종족" required htmlFor="actor-species">
          <input
            id="actor-species"
            type="text"
            value={identity.species}
            onChange={(event) => onChangeActor((current) => setIdentity(current, { species: event.target.value }))}
          />
        </FieldRow>
        <FieldRow label="성별" required htmlFor="actor-sex">
          <select
            id="actor-sex"
            value={identity.sex}
            onChange={(event) => onChangeActor((current) => setIdentity(current, { sex: event.target.value as Sex }))}
          >
            {SEX_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>
        <FieldRow label="연령대" required htmlFor="actor-age">
          <select
            id="actor-age"
            value={identity.ageGroup}
            onChange={(event) =>
              onChangeActor((current) => setIdentity(current, { ageGroup: event.target.value as AgeGroup }))
            }
          >
            {AGE_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>
        <FieldRow label="역할/직업" required htmlFor="actor-role">
          <input
            id="actor-role"
            type="text"
            value={identity.role}
            onChange={(event) => onChangeActor((current) => setIdentity(current, { role: event.target.value }))}
          />
        </FieldRow>
        <FieldRow label="상태" required htmlFor="actor-status">
          <select
            id="actor-status"
            value={identity.status}
            onChange={(event) =>
              onChangeActor((current) => setIdentity(current, { status: event.target.value as Status }))
            }
          >
            {STATUS_OPTIONS.map((option) => (
              <option key={option} value={option}>
                {option}
              </option>
            ))}
          </select>
        </FieldRow>
      </div>

      <FieldRow label="한 문장 콘셉트" required htmlFor="actor-concept">
        <textarea
          id="actor-concept"
          value={identity.concept}
          onChange={(event) => onChangeActor((current) => setIdentity(current, { concept: event.target.value }))}
        />
      </FieldRow>

      <FieldRow label="별칭 (문서상 작업명 등)" htmlFor="actor-aliases" hint="예: ElfGuardian의 별칭 LeafGlaiveElf">
        <TagListEditor
          id="actor-aliases"
          values={actor.aliases}
          onChange={(values) => onChangeActor((current) => setTopLevel(current, { aliases: values }))}
          placeholder="별칭 입력 후 Enter"
        />
      </FieldRow>

      <FieldRow label="리소스 폴더 경로" htmlFor="actor-resource-path" hint="예: Assets/Art/Character/ElfGuardian">
        <input
          id="actor-resource-path"
          type="text"
          value={actor.resourceFolderPath ?? ""}
          onChange={(event) =>
            onChangeActor((current) => setTopLevel(current, { resourceFolderPath: event.target.value }))
          }
        />
      </FieldRow>

      <FieldRow label="출신 세계" origin="locked" hint="생성 시 고정됩니다. 다른 세계로 옮기려면 새 액터를 만드세요.">
        <input
          type="text"
          disabled
          value={`${world.displayName.ko ?? world.displayName.en} (${world.worldId} v${actor.worldRef.version})`}
        />
      </FieldRow>
    </div>
  );
}

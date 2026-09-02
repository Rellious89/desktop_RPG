using System;
using System.Collections.Generic;
using Character;
using Common;
using Skill;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CharacterArchive
{
    /// <summary>명부의 CharacterInfo 페이지만 소유한다. 패널은 선택 정의와 저장 스냅샷만 전달한다.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterInfoController : MonoBehaviour
    {
        public readonly struct SkillRow
        {
            public SkillRow(CharacterSkillDefinition relation, SkillDefinition skill, bool unlocked)
            {
                Relation = relation;
                Skill = skill;
                Unlocked = unlocked;
            }

            public CharacterSkillDefinition Relation { get; }
            public SkillDefinition Skill { get; }
            public bool Unlocked { get; }
        }

        [Header("Catalogs (Inspector에서만 연결)")]
        [SerializeField] private CharacterCatalog characterCatalog;
        [SerializeField] private SkillCatalog skillCatalog;
        [SerializeField] private CharacterSkillCatalog characterSkillCatalog;

        [Header("Base Info (Inspector에서만 연결)")]
        [SerializeField] private Image characterModelImage;
        [SerializeField] private TMP_Text characterNameText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private TMP_Text originWorldText;

        [Header("Skill Info (Inspector에서만 연결)")]
        [SerializeField] private TMP_Text skillTitleText;
        [SerializeField] private LocalizedTMPText skillTitleLocalizer;
        [SerializeField] private GameObject emptyState;
        [SerializeField] private RectTransform skillContent;
        [SerializeField] private SkillListItemView skillTemplate;

        private readonly CharacterNameBinding nameBinding = new CharacterNameBinding();
        private readonly List<SkillListItemView> itemPool = new List<SkillListItemView>();
        private CharacterDefinition character;
        private SaveData document;
        private LocalizedTextReference originWorldName;
        private LocalizedTextReference skillTitleFormat;
        private Sprite[] previewFrames = Array.Empty<Sprite>();
        private float previewFps;
        private float previewElapsed;
        private int previewIndex;
        private int unlockedCount;
        private int totalCount;

        public CharacterDefinition Character => character;
        public int ActiveItemCount { get; private set; }
        public int PooledItemCount => itemPool.Count;
        public bool HasOriginWorldSubscription => originWorldName != null;
        public bool HasTitleSubscription => skillTitleFormat != null;
        public int PreviewFrameIndex => previewIndex;
        public bool IsPreviewPlaying => previewFrames.Length > 0 && characterModelImage != null && characterModelImage.enabled;

        public bool HasRequiredReferences => characterCatalog != null && skillCatalog != null
            && characterSkillCatalog != null && characterModelImage != null && characterNameText != null
            && levelText != null && originWorldText != null && skillTitleText != null
            && skillTitleLocalizer != null && emptyState != null && skillContent != null && skillTemplate != null;

        public void BindCharacter(CharacterDefinition value, SaveData data)
        {
            bool sameCharacter = ReferenceEquals(character, value);
            character = value;
            document = data;
            if (!isActiveAndEnabled) return;

            // 저장 레벨/해금 결과는 같은 캐릭터를 다시 전달해도 바뀔 수 있다.
            RebindActive(resetPreview: !sameCharacter);
        }

        private void OnEnable() => RebindActive(resetPreview: true);

        private void OnDisable()
        {
            UnbindLocalization();
            HideAllItems();
        }

        private void OnDestroy()
        {
            UnbindLocalization();
            nameBinding.Unbind();
        }

        private void Update() => AdvancePreview(Time.unscaledDeltaTime);

        private void RebindActive(bool resetPreview)
        {
            UnbindLocalization();
            nameBinding.Bind(character, ApplyCharacterName);
            ApplyLevel(ResolveLevel(document, character != null ? character.CharacterId : null));
            BindOriginWorld();
            RefreshSkills();
            // 카운트를 먼저 계산해야 이미 로드된 문자열이 구독 즉시 전달돼도 이전 캐릭터의
            // 숫자로 포맷되지 않는다. 로컬라이즈 결과가 fallback에 다시 덮이는 일도 막는다.
            BindSkillTitleFormat();
            BindPreview(resetPreview);
        }

        private void BindOriginWorld()
        {
            ApplyOriginWorld(string.Empty);
            if (character == null || character.OriginWorld == null || !character.OriginWorld.HasLocalizedName) return;
            originWorldName = character.OriginWorld.LocalizedName;
            originWorldName.StringChanged += ApplyOriginWorld;
        }

        private void BindSkillTitleFormat()
        {
            skillTitleFormat = skillTitleLocalizer != null ? skillTitleLocalizer.TextReference : null;
            if (skillTitleFormat != null && skillTitleFormat.HasReference)
                skillTitleFormat.StringChanged += ApplySkillTitleFormat;
        }

        private void UnbindLocalization()
        {
            nameBinding.Unbind();
            if (originWorldName != null) originWorldName.StringChanged -= ApplyOriginWorld;
            if (skillTitleFormat != null) skillTitleFormat.StringChanged -= ApplySkillTitleFormat;
            originWorldName = null;
            skillTitleFormat = null;
        }

        private void RefreshSkills()
        {
            IReadOnlyList<SkillRow> rows = BuildSkillRows(
                character, characterCatalog, skillCatalog, characterSkillCatalog, document);
            totalCount = rows.Count;
            unlockedCount = 0;
            for (int i = 0; i < rows.Count; i++) if (rows[i].Unlocked) unlockedCount++;
            ApplySkillTitleFallback();

            EnsureItemCount(unlockedCount);
            int visibleIndex = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].Unlocked) continue;
                SkillListItemView item = itemPool[visibleIndex++];
                item.Bind(rows[i].Skill);
                if (!item.gameObject.activeSelf) item.gameObject.SetActive(true);
            }
            for (int i = visibleIndex; i < itemPool.Count; i++)
            {
                itemPool[i].Unbind();
                if (itemPool[i].gameObject.activeSelf) itemPool[i].gameObject.SetActive(false);
            }

            ActiveItemCount = visibleIndex;
            SetActive(emptyState, visibleIndex == 0);
            if (skillTemplate != null && skillTemplate.gameObject.activeSelf) skillTemplate.gameObject.SetActive(false);
            if (skillContent != null) LayoutRebuilder.MarkLayoutForRebuild(skillContent);
        }

        private void EnsureItemCount(int count)
        {
            if (skillTemplate == null || skillContent == null) return;
            while (itemPool.Count < count)
            {
                SkillListItemView item = Instantiate(skillTemplate, skillContent);
                item.name = skillTemplate.name + "_Runtime";
                item.gameObject.SetActive(false);
                itemPool.Add(item);
            }
        }

        private void HideAllItems()
        {
            for (int i = 0; i < itemPool.Count; i++)
            {
                itemPool[i].Unbind();
                if (itemPool[i].gameObject.activeSelf) itemPool[i].gameObject.SetActive(false);
            }
            ActiveItemCount = 0;
        }

        private void BindPreview(bool reset)
        {
            Sprite[] next = PlayablePreviewFrames(character);
            previewFrames = next;
            previewFps = next.Length > 0 ? character.MotionProfile.BaseIdle.AnimationFps : 0f;
            if (reset || previewIndex >= next.Length)
            {
                previewIndex = 0;
                previewElapsed = 0f;
            }

            if (characterModelImage == null) return;
            bool playable = next.Length > 0;
            characterModelImage.enabled = playable;
            characterModelImage.sprite = playable ? next[previewIndex] : null;
        }

        public void AdvancePreview(float unscaledDeltaTime)
        {
            if (!isActiveAndEnabled || previewFrames.Length < 2 || characterModelImage == null
                || !characterModelImage.enabled || unscaledDeltaTime <= 0f) return;

            float frameDuration = 1f / Mathf.Max(0.01f, previewFps);
            previewElapsed += unscaledDeltaTime;
            while (previewElapsed >= frameDuration)
            {
                previewElapsed -= frameDuration;
                previewIndex = (previewIndex + 1) % previewFrames.Length;
            }
            characterModelImage.sprite = previewFrames[previewIndex];
        }

        public static Sprite[] PlayablePreviewFrames(CharacterDefinition definition)
        {
            CharacterMotionProfile profile = definition != null ? definition.MotionProfile : null;
            CharacterMotionProfile.FrameClip clip = profile != null ? profile.BaseIdle : null;
            Sprite[] frames = clip != null ? clip.Frames : null;
            if (frames == null || frames.Length == 0) return Array.Empty<Sprite>();
            for (int i = 0; i < frames.Length; i++) if (frames[i] == null) return Array.Empty<Sprite>();
            return frames;
        }

        public static int ResolveLevel(SaveData data, string characterId)
        {
            if (data != null && data.characters != null && !string.IsNullOrEmpty(characterId))
            {
                for (int i = 0; i < data.characters.Count; i++)
                {
                    CharacterSaveState state = data.characters[i];
                    if (state != null && string.Equals(state.characterId, characterId, StringComparison.Ordinal))
                        return Mathf.Max(CharacterSkillUnlockService.MinimumLevel, state.level);
                }
            }
            return CharacterSkillUnlockService.MinimumLevel;
        }

        public static IReadOnlyList<SkillRow> BuildSkillRows(
            CharacterDefinition definition,
            CharacterCatalog characters,
            SkillCatalog skills,
            CharacterSkillCatalog relations,
            SaveData data)
        {
            var result = new List<SkillRow>();
            if (definition == null || characters == null || skills == null || relations == null) return result;
            string characterId = definition.CharacterId;
            if (!ReferenceEquals(characters.Find(characterId), definition)) return result;

            var unlocks = new CharacterSkillUnlockService(characters, skills, relations, data);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<CharacterSkillDefinition> all = relations.Relations;
            for (int i = 0; i < all.Count; i++)
            {
                CharacterSkillDefinition relation = all[i];
                if (!IsCanonicalRelation(relation, characterId, skills, out SkillDefinition skill)) continue;
                if (!seen.Add(skill.SkillId)) continue;
                result.Add(new SkillRow(relation, skill, unlocks.IsUnlocked(characterId, skill.SkillId)));
            }

            result.Sort((left, right) =>
            {
                int order = left.Relation.DisplayOrder.CompareTo(right.Relation.DisplayOrder);
                return order != 0 ? order : string.Compare(left.Skill.SkillId, right.Skill.SkillId, StringComparison.Ordinal);
            });
            return result;
        }

        private static bool IsCanonicalRelation(
            CharacterSkillDefinition relation, string characterId, SkillCatalog skills, out SkillDefinition canonical)
        {
            canonical = null;
            if (relation == null || !relation.IsValid
                || !string.Equals(relation.CharacterId, characterId, StringComparison.Ordinal)) return false;
            if (relation.Character == null
                || !string.Equals(relation.Character.CharacterId, relation.CharacterId, StringComparison.Ordinal)) return false;
            if (relation.Skill == null
                || !string.Equals(relation.Skill.SkillId, relation.SkillId, StringComparison.Ordinal)) return false;
            canonical = skills.Find(relation.SkillId);
            return canonical != null;
        }

        private void ApplyCharacterName(string value)
        {
            if (characterNameText != null) characterNameText.text = value ?? string.Empty;
        }

        private void ApplyLevel(int level)
        {
            if (levelText != null) levelText.text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "Lv. {0}", level);
        }

        private void ApplyOriginWorld(string value)
        {
            if (originWorldText != null) originWorldText.text = value ?? string.Empty;
        }

        private void ApplySkillTitleFormat(string format)
        {
            if (skillTitleText == null) return;
            skillTitleText.text = string.IsNullOrEmpty(format)
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}/{1}", unlockedCount, totalCount)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, format, unlockedCount, totalCount);
        }

        private void ApplySkillTitleFallback()
        {
            if (skillTitleText != null)
                skillTitleText.text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Skills ({0}/{1})", unlockedCount, totalCount);
        }

        private static void SetActive(GameObject target, bool value)
        {
            if (target != null && target.activeSelf != value) target.SetActive(value);
        }
    }
}

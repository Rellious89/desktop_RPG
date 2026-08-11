using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using NUnit.Framework;
using Skill;
using TableDataEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace SkillEditor.Tests
{
    /// <summary>
    /// 시험이 쓰는 카탈로그/정의를 메모리에만 만들어 두는 공통 도구. <b>프로젝트의 에셋을 만들지도
    /// 고치지도 않는다</b> - 전부 <see cref="ScriptableObject.CreateInstance"/>로 만들고 시험이 끝나면
    /// 지운다.
    ///
    /// 도우미 이름이 <c>CharacterAsset</c>/<c>SkillAsset</c>/<c>Catalog</c>처럼 타입 이름과 어긋나
    /// 있는 것은 일부러다 - <c>Skill</c>과 <c>Character</c>는 이 프로젝트의 <b>네임스페이스 이름</b>이고,
    /// 같은 이름의 메서드를 두면 그 이름이 가려져 타입을 못 찾는다.
    /// </summary>
    public abstract class SkillUnlockTestBase
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void DestroyCreatedAssets()
        {
            foreach (UnityEngine.Object asset in created)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        protected T Track<T>(T asset) where T : UnityEngine.Object
        {
            created.Add(asset);
            return asset;
        }

        /// <param name="playable">CharacterRoster의 목록 검사를 통과해야 하는 경우에만 true. 순수한
        /// 해금 계산에는 모션 프로필이 아무 상관이 없으므로 기본은 false다 - 필요 없는 스프라이트를
        /// 시험마다 만들지 않는다.</param>
        protected CharacterDefinition CharacterAsset(string id, bool playable = false)
        {
            var definition = Track(ScriptableObject.CreateInstance<CharacterDefinition>());
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("characterId").stringValue = id;
            serialized.FindProperty("maxStamina").intValue = 30;
            if (playable) serialized.FindProperty("motionProfile").objectReferenceValue = PlayableProfile();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        /// <summary>재생 가능한 최소 모션 프로필. CharacterRoster가 목록을 만들 때 Base Idle 프레임이
        /// 하나라도 있어야 항목을 남긴다.</summary>
        private CharacterMotionProfile PlayableProfile()
        {
            var profile = Track(ScriptableObject.CreateInstance<CharacterMotionProfile>());

            var texture = Track(new Texture2D(4, 4));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f)));

            var serialized = new SerializedObject(profile);
            SerializedProperty frames = serialized.FindProperty("baseIdle").FindPropertyRelative("frames");
            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        protected SkillDefinition SkillAsset(string id)
        {
            var definition = Track(ScriptableObject.CreateInstance<SkillDefinition>());
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("skillId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        /// <summary>관계 한 줄. 기본은 <b>멀쩡한 줄</b>(참조가 두 식별자와 맞는다)이고, 참조를 일부러
        /// 어긋내거나 비우는 시험만 뒤쪽 인자를 넘긴다.</summary>
        protected CharacterSkillDefinition Relation(
            string characterId,
            string skillId,
            int requiredLevel = 1,
            CharacterDefinition characterRef = null,
            SkillDefinition skillRef = null,
            bool linkCharacter = true,
            bool linkSkill = true)
        {
            var relation = Track(ScriptableObject.CreateInstance<CharacterSkillDefinition>());
            var serialized = new SerializedObject(relation);
            serialized.FindProperty("characterId").stringValue = characterId;
            serialized.FindProperty("skillId").stringValue = skillId;
            serialized.FindProperty("requiredCharacterLevel").intValue = requiredLevel;

            serialized.FindProperty("character").objectReferenceValue =
                linkCharacter ? characterRef ?? CharacterAsset(characterId) : null;
            serialized.FindProperty("skill").objectReferenceValue =
                linkSkill ? skillRef ?? SkillAsset(skillId) : null;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return relation;
        }

        protected CharacterCatalog Catalog(params string[] ids)
        {
            return Catalog(false, ids);
        }

        /// <summary>CharacterRoster의 목록 검사까지 통과해야 하는 구성. 로스터를 세우는 시험만 쓴다.</summary>
        protected CharacterCatalog PlayableCatalog(params string[] ids)
        {
            return Catalog(true, ids);
        }

        private CharacterCatalog Catalog(bool playable, string[] ids)
        {
            var definitions = new CharacterDefinition[ids.Length];
            for (int i = 0; i < ids.Length; i++) definitions[i] = CharacterAsset(ids[i], playable);

            var catalog = Track(ScriptableObject.CreateInstance<CharacterCatalog>());
            Fill(catalog, "characters", definitions);
            catalog.MarkDirty();
            return catalog;
        }

        protected SkillCatalog SkillList(params string[] ids)
        {
            var definitions = new SkillDefinition[ids.Length];
            for (int i = 0; i < ids.Length; i++) definitions[i] = SkillAsset(ids[i]);
            return SkillList(definitions);
        }

        protected SkillCatalog SkillList(params SkillDefinition[] definitions)
        {
            var catalog = Track(ScriptableObject.CreateInstance<SkillCatalog>());
            Fill(catalog, "skills", definitions);
            catalog.MarkDirty();
            return catalog;
        }

        protected CharacterSkillCatalog RelationList(params CharacterSkillDefinition[] relations)
        {
            var catalog = Track(ScriptableObject.CreateInstance<CharacterSkillCatalog>());
            Fill(catalog, "relations", relations);
            catalog.MarkDirty();
            return catalog;
        }

        /// <summary>목록을 만들 때 카탈로그가 남기는 경고/오류를 무시한다 - 걸러진다는 사실 자체가
        /// 그 시험이 확인하려는 것이다.</summary>
        protected CharacterSkillCatalog RelationListQuiet(params CharacterSkillDefinition[] relations)
        {
            CharacterSkillCatalog catalog = RelationList(relations);

            LogAssert.ignoreFailingMessages = true;
            int _ = catalog.Count;
            LogAssert.ignoreFailingMessages = false;

            return catalog;
        }

        private static void Fill(ScriptableObject catalog, string field, UnityEngine.Object[] items)
        {
            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty(field);
            Assert.IsNotNull(list, $"{catalog.GetType().Name}.{field}를 찾지 못했습니다.");

            list.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        protected static SaveData Document(params CharacterSaveState[] states)
        {
            return new SaveData { characters = new List<CharacterSaveState>(states) };
        }

        protected static CharacterSaveState State(string id, int level = 1)
        {
            return new CharacterSaveState { characterId = id, level = level, currentStamina = 10 };
        }

        protected static List<string> Ids(IReadOnlyList<SkillDefinition> skills)
        {
            var ids = new List<string>();
            for (int i = 0; i < skills.Count; i++) ids.Add(skills[i].SkillId);
            return ids;
        }

        protected static string Describe(SaveData document)
        {
            if (document.characters == null) return "(null)";

            var parts = new List<string>();
            foreach (CharacterSaveState state in document.characters)
            {
                parts.Add(state == null ? "(null)" : $"{state.characterId}:{state.level}:{state.currentExp}");
            }

            return string.Join("|", parts);
        }
    }

    /// <summary>
    /// <see cref="CharacterSkillUnlockService"/> 시험.
    ///
    /// 이 서비스가 지켜야 하는 것은 하나로 모인다 - <b>해금은 어디에도 저장되지 않고, 표와 저장된
    /// 레벨로 그때마다 계산되며, 조건이 하나라도 어긋나면 열리지 않고, 어떤 조회도 저장 문서를 바꾸지
    /// 않는다.</b>
    ///
    /// 실제 저장 파일도 씬도 쓰지 않는다 - 저장 문서는 그냥 새 <see cref="SaveData"/> 객체이며
    /// persistentDataPath는 어디에서도 쓰이지 않는다.
    /// </summary>
    public sealed class CharacterSkillUnlockServiceTests : SkillUnlockTestBase
    {
        // ---- 필요 레벨의 경계 ----

        [Test]
        public void 필요_레벨_아래면_잠겨_있다()
        {
            CharacterSkillUnlockService service = Service(level: 2, Relation("CatKnight", "cleave", requiredLevel: 3));

            Assert.IsFalse(service.IsUnlocked("CatKnight", "cleave"));
            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.AreEqual(new[] { "cleave" }, Ids(service.GetLockedSkills("CatKnight")));
        }

        [Test]
        public void 필요_레벨과_같으면_열린다()
        {
            CharacterSkillUnlockService service = Service(level: 3, Relation("CatKnight", "cleave", requiredLevel: 3));

            Assert.IsTrue(service.IsUnlocked("CatKnight", "cleave"), "경계는 '이상'이다.");
            CollectionAssert.AreEqual(new[] { "cleave" }, Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetLockedSkills("CatKnight")));
        }

        [Test]
        public void 필요_레벨_위면_열린다()
        {
            CharacterSkillUnlockService service = Service(level: 9, Relation("CatKnight", "cleave", requiredLevel: 3));

            Assert.IsTrue(service.IsUnlocked("CatKnight", "cleave"));
            CollectionAssert.AreEqual(new[] { "cleave" }, Ids(service.GetUnlockedSkills("CatKnight")));
        }

        [Test]
        public void 열린_것과_잠긴_것은_서로_겹치지_않고_둘을_합치면_전부다()
        {
            CharacterSkillUnlockService service = Service(
                level: 5,
                Relation("CatKnight", "slash", requiredLevel: 1),
                Relation("CatKnight", "cleave", requiredLevel: 5),
                Relation("CatKnight", "storm", requiredLevel: 6),
                Relation("CatKnight", "meteor", requiredLevel: 99));

            CollectionAssert.AreEqual(new[] { "slash", "cleave" }, Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.AreEqual(new[] { "storm", "meteor" }, Ids(service.GetLockedSkills("CatKnight")));
        }

        // ---- 새로 열린 스킬 ----

        [Test]
        public void 한_번에_여러_레벨을_넘으면_그_구간의_스킬이_모두_새로_열린다()
        {
            CharacterSkillUnlockService service = Service(
                level: 4,
                Relation("CatKnight", "slash", requiredLevel: 1),
                Relation("CatKnight", "cleave", requiredLevel: 2),
                Relation("CatKnight", "storm", requiredLevel: 4),
                Relation("CatKnight", "meteor", requiredLevel: 5));

            CollectionAssert.AreEqual(new[] { "cleave", "storm" },
                Ids(service.GetNewlyUnlockedSkills("CatKnight", 1, 4)),
                "이전 < 필요 <= 이후인 것만 - 1은 이미 열려 있었고 5는 아직이다.");
        }

        [Test]
        public void 이미_열린_스킬은_다시_새로_열리지_않는다()
        {
            CharacterSkillUnlockService service = Service(
                level: 9, Relation("CatKnight", "slash", requiredLevel: 2));

            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("CatKnight", 5, 9)),
                "반복해서 자라도 같은 스킬이 계속 새로 열렸다고 나오면 그 신호는 뜻이 없다.");
            Assert.IsTrue(service.IsUnlocked("CatKnight", "slash"), "그래도 열려 있는 것은 맞다.");
        }

        [Test]
        public void 거꾸로거나_같은_구간은_새로_열린_것이_없다()
        {
            CharacterSkillUnlockService service = Service(
                level: 5, Relation("CatKnight", "cleave", requiredLevel: 3));

            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("CatKnight", 5, 5)), "같은 구간");
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("CatKnight", 9, 2)), "거꾸로 간 구간");
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("CatKnight", 0, 0)));
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("CatKnight", -7, 1)),
                "1보다 작은 값은 하한으로 보므로 1에서 1로 간 것과 같다.");
        }

        [Test]
        public void 하한보다_작은_이전_레벨은_1로_보고_그_위만_새로_열린다()
        {
            CharacterSkillUnlockService service = Service(
                level: 3,
                Relation("CatKnight", "slash", requiredLevel: 1),
                Relation("CatKnight", "cleave", requiredLevel: 3));

            CollectionAssert.AreEqual(new[] { "cleave" },
                Ids(service.GetNewlyUnlockedSkills("CatKnight", -5, 3)),
                "하한이 1이므로 필요 1짜리는 이미 열려 있던 것으로 본다.");
        }

        [Test]
        public void 아주_높은_레벨에서도_안전하다()
        {
            CharacterSkillUnlockService service = Service(
                level: int.MaxValue,
                Relation("CatKnight", "cleave", requiredLevel: int.MaxValue));

            Assert.IsTrue(service.IsUnlocked("CatKnight", "cleave"));
            CollectionAssert.AreEqual(new[] { "cleave" },
                Ids(service.GetNewlyUnlockedSkills("CatKnight", 1, int.MaxValue)));
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills(
                "CatKnight", int.MaxValue, int.MaxValue)));
        }

        // ---- 보유와 카탈로그 ----

        [Test]
        public void 보유하지_않은_캐릭터는_아무것도_열리지_않는다()
        {
            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight", "ElfArcher"),
                SkillList("shot"),
                RelationList(Relation("ElfArcher", "shot", requiredLevel: 1)),
                Document(State("CatKnight", level: 99)));

            Assert.IsFalse(service.IsUnlocked("ElfArcher", "shot"), "가지지 않은 캐릭터의 스킬은 열리지 않는다.");
            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills("ElfArcher")));
            CollectionAssert.IsEmpty(Ids(service.GetLockedSkills("ElfArcher")),
                "가지지 않은 캐릭터에게는 잠긴 스킬도 없다 - 그 캐릭터의 목록 자체가 없다.");
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("ElfArcher", 1, 99)));
        }

        [Test]
        public void 카탈로그에_없는_저장_전용_id는_아무것도_열리지_않는다()
        {
            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                SkillList("haunt"),
                RelationList(Relation("GhostHero", "haunt", requiredLevel: 1)),
                Document(State("GhostHero", level: 99)));

            Assert.IsFalse(service.IsUnlocked("GhostHero", "haunt"),
                "보유는 맞지만 지금 빌드의 캐릭터가 아니다.");
            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills("GhostHero")));
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("GhostHero", 1, 99)));
        }

        [Test]
        public void 저장_목록에_같은_id가_두_번_있으면_먼저_나온_항목이_근거다()
        {
            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                SkillList("cleave"),
                RelationList(Relation("CatKnight", "cleave", requiredLevel: 5)),
                Document(State("CatKnight", level: 1), State("CatKnight", level: 99)));

            Assert.IsFalse(service.IsUnlocked("CatKnight", "cleave"),
                "뒤에 있는 항목이 조건을 넘겨도 근거는 먼저 나온 항목 하나다.");
        }

        // ---- 깨진 관계는 스킬이 아니다 ----

        [Test]
        public void 스킬_카탈로그에_없는_스킬은_열리지도_잠기지도_않는다()
        {
            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                SkillList("cleave"),
                RelationList(
                    Relation("CatKnight", "cleave", requiredLevel: 1),
                    Relation("CatKnight", "ghost_skill", requiredLevel: 1)),
                Document(State("CatKnight", level: 99)));

            Assert.IsFalse(service.IsUnlocked("CatKnight", "ghost_skill"));
            CollectionAssert.AreEqual(new[] { "cleave" }, Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetLockedSkills("CatKnight")),
                "관계만 있고 스킬이 없는 줄은 잠긴 스킬이 아니라 스킬이 아니다.");
        }

        [Test]
        public void 관계의_참조가_비어_있으면_스킬이_아니다()
        {
            SkillDefinition cleave = SkillAsset("cleave");
            SkillDefinition storm = SkillAsset("storm");

            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                SkillList(cleave, storm),
                RelationList(
                    Relation("CatKnight", "cleave", requiredLevel: 1, skillRef: cleave, linkSkill: false),
                    Relation("CatKnight", "storm", requiredLevel: 1, skillRef: storm, linkCharacter: false)),
                Document(State("CatKnight", level: 99)));

            Assert.IsFalse(service.IsUnlocked("CatKnight", "cleave"), "스킬 참조가 비었다.");
            Assert.IsFalse(service.IsUnlocked("CatKnight", "storm"), "캐릭터 참조가 비었다.");
            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetLockedSkills("CatKnight")));
        }

        [Test]
        public void 관계의_참조_id가_적힌_id와_어긋나면_스킬이_아니다()
        {
            SkillDefinition cleave = SkillAsset("cleave");
            SkillDefinition storm = SkillAsset("storm");

            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                // 두 스킬 모두 정식 목록에 있다 - 남는 실패 이유는 참조가 어긋난 것 하나뿐이다.
                SkillList(cleave, storm),
                RelationList(
                    // 스킬 참조가 다른 스킬을 가리킨다.
                    Relation("CatKnight", "cleave", requiredLevel: 1, skillRef: SkillAsset("storm")),
                    // 캐릭터 참조가 다른 캐릭터를 가리킨다.
                    Relation("CatKnight", "storm", requiredLevel: 1,
                        characterRef: CharacterAsset("ElfArcher"), skillRef: storm)),
                Document(State("CatKnight", level: 99)));

            Assert.IsFalse(service.IsUnlocked("CatKnight", "cleave"),
                "임포터가 같은 행에서 채우는 연결이 어긋났다면 데이터가 깨진 것이다.");
            Assert.IsFalse(service.IsUnlocked("CatKnight", "storm"));
            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetLockedSkills("CatKnight")));
        }

        [Test]
        public void 식별자가_반쪽인_관계는_애초에_목록에_오르지_않는다()
        {
            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                SkillList("cleave"),
                RelationListQuiet(Relation("CatKnight", string.Empty, requiredLevel: 1)),
                Document(State("CatKnight", level: 99)));

            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetLockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("CatKnight", 0, 99)));
        }

        // ---- 적힌 그대로 비교한다 ----

        [Test]
        public void 대소문자가_다르면_다른_캐릭터이고_다른_스킬이다()
        {
            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                SkillList("cleave"),
                RelationList(Relation("CatKnight", "cleave", requiredLevel: 1)),
                Document(State("CatKnight", level: 99)));

            Assert.IsTrue(service.IsUnlocked("CatKnight", "cleave"), "전제 확인");

            Assert.IsFalse(service.IsUnlocked("catknight", "cleave"), "캐릭터 id의 대소문자를 구분한다.");
            Assert.IsFalse(service.IsUnlocked("CatKnight", "Cleave"), "스킬 id의 대소문자를 구분한다.");
            Assert.IsFalse(service.IsUnlocked("CatKnight ", "cleave"), "공백을 떼지 않는다.");
            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills("catknight")));
        }

        // ---- 순서와 중복 ----

        [Test]
        public void 결과는_관계_목록의_차례를_그대로_따른다()
        {
            // 필요 레벨의 크기와 무관하게 목록에 적힌 차례여야 한다.
            CharacterSkillUnlockService service = Service(
                level: 99,
                Relation("CatKnight", "zeta", requiredLevel: 9),
                Relation("CatKnight", "alpha", requiredLevel: 1),
                Relation("CatKnight", "mid", requiredLevel: 5));

            CollectionAssert.AreEqual(new[] { "zeta", "alpha", "mid" },
                Ids(service.GetUnlockedSkills("CatKnight")));

            // 새로 열린 쪽도 같은 차례다. alpha(필요 1)가 빠지는 것은 순서 문제가 아니라 이전 레벨의
            // 하한이 1이라 <b>이미 열려 있던</b> 스킬이기 때문이다 - 남는 둘의 차례가 목록 그대로여야 한다.
            CollectionAssert.AreEqual(new[] { "zeta", "mid" },
                Ids(service.GetNewlyUnlockedSkills("CatKnight", 0, 99)));
        }

        [Test]
        public void 같은_스킬을_두_번_담지_않는다()
        {
            // 관계 카탈로그가 같은 짝을 이미 거르지만, 그것을 가정하지 않는다 - 밖에서 만든 목록을
            // 넘겨도 받는 쪽이 같은 스킬을 두 번 그리는 일은 없어야 한다.
            SkillDefinition cleave = SkillAsset("cleave");

            // 필요 레벨을 2로 두는 것은 "새로 열린" 조회(이전 < 필요 <= 이후)에서도 중복이 걸러지는지
            // 함께 보기 위해서다 - 필요 1짜리는 이전 레벨의 하한(1)에서 이미 열려 있어 아예 나오지 않는다.
            CharacterSkillDefinition relation = Relation("CatKnight", "cleave", requiredLevel: 2, skillRef: cleave);

            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                SkillList(cleave),
                RelationListQuiet(relation, relation),
                Document(State("CatKnight", level: 99)));

            CollectionAssert.AreEqual(new[] { "cleave" }, Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.AreEqual(new[] { "cleave" }, Ids(service.GetNewlyUnlockedSkills("CatKnight", 0, 99)));
        }

        [Test]
        public void 돌려주는_정의는_정식_스킬_카탈로그의_것이다()
        {
            // 관계가 같은 id의 <b>다른</b> 에셋을 물고 있어도, 받는 쪽은 목록에 있는 그 객체를 봐야 한다.
            SkillDefinition canonical = SkillAsset("cleave");
            SkillDefinition manual = SkillAsset("cleave");
            Assert.AreNotSame(canonical, manual, "전제 확인 - 서로 다른 에셋이어야 한다.");

            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                SkillList(canonical),
                RelationList(Relation("CatKnight", "cleave", requiredLevel: 1, skillRef: manual)),
                Document(State("CatKnight", level: 99)));

            IReadOnlyList<SkillDefinition> unlocked = service.GetUnlockedSkills("CatKnight");
            Assert.AreEqual(1, unlocked.Count);
            Assert.AreSame(canonical, unlocked[0], "받는 쪽은 언제나 정식 카탈로그의 정의를 봐야 한다.");
        }

        // ---- 안전한 기본값 ----

        [Test]
        public void 카탈로그가_없으면_조용히_비어_있다()
        {
            var service = new CharacterSkillUnlockService(null, null, null, null);

            Assert.IsFalse(service.IsUnlocked("CatKnight", "cleave"));
            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetLockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("CatKnight", 1, 99)));
        }

        [Test]
        public void 빈_id와_null_id는_조용히_비어_있다()
        {
            CharacterSkillUnlockService service = Service(level: 99, Relation("CatKnight", "cleave"));

            Assert.IsFalse(service.IsUnlocked(null, "cleave"));
            Assert.IsFalse(service.IsUnlocked("CatKnight", null));
            Assert.IsFalse(service.IsUnlocked(string.Empty, string.Empty));
            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills(null)));
            CollectionAssert.IsEmpty(Ids(service.GetLockedSkills(string.Empty)));
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills(null, 1, 99)));
        }

        [Test]
        public void 프로덕션의_생성된_스킬_카탈로그가_비어_있어도_안전하다()
        {
            // 지금 표에는 스킬도 관계도 한 줄이 없다 - 그 상태가 오류가 아니라 정상이어야 한다.
            var skills = AssetDatabase.LoadAssetAtPath<SkillCatalog>(TableDataPaths.SkillCatalogAssetPath);
            var relations = AssetDatabase.LoadAssetAtPath<CharacterSkillCatalog>(
                TableDataPaths.CharacterSkillCatalogAssetPath);

            Assert.IsNotNull(skills, "생성된 스킬 카탈로그가 없습니다 - Table Data Rebuild를 먼저 실행하세요.");
            Assert.IsNotNull(relations, "생성된 관계 카탈로그가 없습니다.");
            skills.MarkDirty();
            relations.MarkDirty();
            Assert.AreEqual(0, skills.Count, "전제 확인 - 아직 스킬이 없다.");
            Assert.AreEqual(0, relations.Count, "전제 확인 - 아직 관계가 없다.");

            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"), skills, relations, Document(State("CatKnight", level: 99)));

            Assert.IsFalse(service.IsUnlocked("CatKnight", "cleave"));
            CollectionAssert.IsEmpty(Ids(service.GetUnlockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetLockedSkills("CatKnight")));
            CollectionAssert.IsEmpty(Ids(service.GetNewlyUnlockedSkills("CatKnight", 1, 99)));
        }

        // ---- 조회는 아무것도 고치지 않는다 ----

        [Test]
        public void 어떤_조회도_저장_문서를_바꾸거나_항목을_만들지_않는다()
        {
            SaveData document = Document(State("CatKnight", level: 0), State("GhostHero", level: 9));
            document.characters[0].currentExp = -3;

            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight", "ElfArcher"),
                SkillList("cleave"),
                RelationList(
                    Relation("CatKnight", "cleave", requiredLevel: 1),
                    Relation("ElfArcher", "cleave", requiredLevel: 1)),
                document);

            string before = Describe(document);
            int count = document.characters.Count;

            service.IsUnlocked("CatKnight", "cleave");
            service.IsUnlocked("ElfArcher", "cleave");
            service.IsUnlocked("GhostHero", "cleave");
            service.GetUnlockedSkills("CatKnight");
            service.GetLockedSkills("ElfArcher");
            service.GetNewlyUnlockedSkills("CatKnight", 0, 99);
            service.GetNewlyUnlockedSkills("ElfArcher", 0, 99);

            Assert.AreEqual(before, Describe(document), "어긋난 값도 고치지 않는다 - 물어보기만 했다.");
            Assert.AreEqual(count, document.characters.Count, "보유하지 않은 캐릭터의 항목이 생기면 안 된다.");
        }

        [Test]
        public void 하한보다_작은_저장_레벨은_계산할_때만_1로_본다()
        {
            SaveData document = Document(State("CatKnight", level: 0));

            var service = new CharacterSkillUnlockService(
                Catalog("CatKnight"),
                SkillList("slash", "cleave"),
                RelationList(
                    Relation("CatKnight", "slash", requiredLevel: 1),
                    Relation("CatKnight", "cleave", requiredLevel: 2)),
                document);

            Assert.IsTrue(service.IsUnlocked("CatKnight", "slash"), "레벨 0은 계산에서 1로 본다.");
            Assert.IsFalse(service.IsUnlocked("CatKnight", "cleave"));
            Assert.AreEqual(0, document.characters[0].level, "그렇다고 저장 항목을 고치면 안 된다.");
        }

        // ---- 도우미 ----

        /// <summary>CatKnight 하나만 보유한 흔한 구성. 관계에 적힌 스킬은 전부 정식 목록에 담아 둔다.</summary>
        private CharacterSkillUnlockService Service(int level, params CharacterSkillDefinition[] relations)
        {
            var skillIds = new List<string>();
            foreach (CharacterSkillDefinition relation in relations)
            {
                if (!skillIds.Contains(relation.SkillId)) skillIds.Add(relation.SkillId);
            }

            return new CharacterSkillUnlockService(
                Catalog("CatKnight", "ElfArcher"),
                SkillList(skillIds.ToArray()),
                RelationList(relations),
                Document(State("CatKnight", level: level)));
        }
    }

    /// <summary>
    /// <see cref="PlayerProgress"/>가 성장 결과로 스킬 해금을 알리는 방식 시험.
    ///
    /// 못 박는 것은 둘이다 - <b>레벨이 실제로 오른 구간에서 새로 열린 것만 한 번씩 알린다</b>,
    /// 그리고 <b>그 계산이 저장 횟수를 늘리지 않는다</b>(처치 하나당 저장 한 번 규칙).
    ///
    /// 실제 저장 파일을 읽지도 쓰지도 않는다 - 문서를 리플렉션으로 끼워 넣고 저장은 메모리 저장소로
    /// 받아 횟수만 센다. 컴포넌트는 비활성 호스트에 붙여 수명 주기를 시험이 직접 부른다.
    /// </summary>
    public sealed class PlayerProgressSkillUnlockTests : SkillUnlockTestBase
    {
        private static readonly FieldInfo DataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo LoadResultField =
            typeof(SaveSystem).GetField("loadResult", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo LoadedFromFileField =
            typeof(SaveSystem).GetField("loadedFromFile", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ConfigureMethod =
            typeof(SaveSystem).GetMethod("ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<PlayerProgress> live = new List<PlayerProgress>();
        private readonly List<CharacterDefinition> seenCharacters = new List<CharacterDefinition>();
        private readonly List<SkillDefinition> seenSkills = new List<SkillDefinition>();

        private Action<CharacterDefinition, SkillDefinition> unlockHandler;
        private SkillCatalog skills;
        private CharacterSkillCatalog relationCatalog;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(DataField, "SaveSystem.data를 찾지 못했습니다 - 그대로 두면 시험이 실제 저장 파일을 읽습니다.");
            Assert.IsNotNull(LoadResultField, "SaveSystem.loadResult를 찾지 못했습니다.");
            Assert.IsNotNull(LoadedFromFileField, "SaveSystem.loadedFromFile을 찾지 못했습니다.");
            Assert.IsNotNull(ConfigureMethod, "SaveSystem.ConfigureForTests를 찾지 못했습니다.");

            ClearStaticEvents();
            skills = null;
            relationCatalog = null;

            unlockHandler = (character, skill) =>
            {
                seenCharacters.Add(character);
                seenSkills.Add(skill);
            };
            PlayerProgress.OnSkillUnlocked += unlockHandler;
        }

        [TearDown]
        public void CleanUp()
        {
            PlayerProgress.OnSkillUnlocked -= unlockHandler;
            seenCharacters.Clear();
            seenSkills.Clear();

            foreach (PlayerProgress progress in live)
            {
                if (progress != null) Invoke(progress, "OnDisable");
            }
            live.Clear();

            ClearStaticEvents();
            ConfigureMethod.Invoke(null, new object[] { null, null, null });
            SetRosterInstance(null);
        }

        [Test]
        public void 레벨이_오르면_새로_열린_스킬마다_한_번씩_알린다()
        {
            SaveData document = Inject(State("CatKnight", level: 1));
            MemoryStorage storage = UseMemoryStorage();

            Ready(document,
                Relation("CatKnight", "cleave", requiredLevel: 2),
                Relation("CatKnight", "storm", requiredLevel: 3));

            // 레벨 1 -> 3 (경험치 25, 레벨당 10)
            Progress().AddExp(25);

            Assert.AreEqual(3, document.characters[0].level, "전제 확인 - 두 단계 올랐다.");
            CollectionAssert.AreEqual(new[] { "cleave", "storm" }, UnlockedIds(),
                "구간에 걸린 두 스킬이 각각 한 번씩 나와야 한다.");
            Assert.AreEqual(2, seenCharacters.Count);
            Assert.AreEqual("CatKnight", seenCharacters[0].CharacterId);
            Assert.AreEqual(1, storage.WriteCalls, "해금 계산이 저장을 늘리면 안 된다.");
        }

        [Test]
        public void 한_번의_처치로_여러_레벨이_올라도_저장은_한_번이다()
        {
            SaveData document = Inject(State("CatKnight", level: 1));
            MemoryStorage storage = UseMemoryStorage();

            Ready(document,
                Relation("CatKnight", "a", requiredLevel: 2),
                Relation("CatKnight", "b", requiredLevel: 3),
                Relation("CatKnight", "c", requiredLevel: 4));

            Progress(expPerDefeat: 35);
            Defeat("Scarecrow");

            Assert.AreEqual(4, document.characters[0].level);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, UnlockedIds());
            Assert.AreEqual(1, storage.WriteCalls, "레벨이 세 번 올라도 파일 쓰기는 한 번이다.");
        }

        [Test]
        public void 저장이_모든_성장_알림보다_먼저_끝난다()
        {
            SaveData document = Inject(State("CatKnight", level: 1));
            MemoryStorage storage = UseMemoryStorage();
            Ready(document, Relation("CatKnight", "cleave", requiredLevel: 2));

            // 알림을 받은 그 순간의 저장 횟수를 적어 둔다. 0이 하나라도 있으면 "화면에는 보이는데
            // 파일에는 아직 없는" 창에서 구독자가 깨어난 것이다.
            var writesWhenNotified = new List<string>();

            Action<int> onGained = _ => writesWhenNotified.Add($"OnExpGained:{storage.WriteCalls}");
            Action<int> onLevelUp = _ => writesWhenNotified.Add($"OnLevelUp:{storage.WriteCalls}");
            Action onChanged = () => writesWhenNotified.Add($"OnExperienceChanged:{storage.WriteCalls}");
            Action<CharacterDefinition, SkillDefinition> onUnlocked =
                (_, _2) => writesWhenNotified.Add($"OnSkillUnlocked:{storage.WriteCalls}");
            Action<CharacterDefinition> onState =
                _ => writesWhenNotified.Add($"CharacterStateChanged:{storage.WriteCalls}");

            PlayerProgress.OnExpGained += onGained;
            PlayerProgress.OnLevelUp += onLevelUp;
            PlayerProgress.OnExperienceChanged += onChanged;
            PlayerProgress.OnSkillUnlocked += onUnlocked;
            CharacterRoster.CharacterStateChanged += onState;

            try
            {
                Progress(expPerDefeat: 10);
                Defeat("Scarecrow");
            }
            finally
            {
                PlayerProgress.OnExpGained -= onGained;
                PlayerProgress.OnLevelUp -= onLevelUp;
                PlayerProgress.OnExperienceChanged -= onChanged;
                PlayerProgress.OnSkillUnlocked -= onUnlocked;
                CharacterRoster.CharacterStateChanged -= onState;
            }

            CollectionAssert.AreEqual(
                new[]
                {
                    "OnExpGained:1",
                    "OnLevelUp:1",
                    "OnExperienceChanged:1",
                    "OnSkillUnlocked:1",
                    "CharacterStateChanged:1",
                },
                writesWhenNotified,
                "모든 알림은 저장이 끝난 뒤에, 그리고 정해진 차례로 나가야 한다.");

            Assert.AreEqual(1, storage.WriteCalls, "알림이 저장을 더 부르지도 않는다.");
            Assert.AreEqual(2, document.characters[0].level, "전제 확인 - 실제로 레벨이 올랐다.");
        }

        [Test]
        public void 이미_넘긴_뒤의_반복_획득은_해금을_알리지_않는다()
        {
            SaveData document = Inject(State("CatKnight", level: 1));
            UseMemoryStorage();
            Ready(document, Relation("CatKnight", "cleave", requiredLevel: 2));

            PlayerProgress progress = Progress();
            progress.AddExp(10);   // 1 -> 2, 여기서 열린다
            Assert.AreEqual(1, seenSkills.Count, "전제 확인 - 이때 한 번 나온다.");

            progress.AddExp(10);   // 2 -> 3
            progress.AddExp(10);   // 3 -> 4

            Assert.AreEqual(1, seenSkills.Count,
                "이미 조건을 넘긴 뒤에는 아무리 자라도 같은 스킬이 다시 나오면 안 된다.");
            Assert.AreEqual(4, document.characters[0].level, "전제 확인 - 계속 자라기는 했다.");
        }

        [Test]
        public void 레벨이_오르지_않은_성장은_해금을_알리지_않는다()
        {
            // 어긋난 경험치가 정리되기만 하고 레벨은 그대로인 성장.
            SaveData document = Inject(State("CatKnight", level: 5));
            document.characters[0].currentExp = -4;
            UseMemoryStorage();
            Ready(document, Relation("CatKnight", "cleave", requiredLevel: 1));

            Progress().AddExp(1);

            Assert.AreEqual(5, document.characters[0].level, "전제 확인 - 레벨은 그대로다.");
            Assert.AreEqual(1, document.characters[0].currentExp, "전제 확인 - 값은 정리됐다.");
            CollectionAssert.IsEmpty(UnlockedIds(),
                "값의 정리는 조건을 넘은 것이 아니다 - 저장 파일을 고칠 때마다 같은 스킬이 나오면 안 된다.");
        }

        [Test]
        public void 캐릭터_교체는_해금을_알리지_않는다()
        {
            SaveData document = Inject(State("CatKnight", level: 1), State("ElfArcher", level: 99));
            UseMemoryStorage();
            CharacterRoster roster = Ready(document, Relation("ElfArcher", "shot", requiredLevel: 2));

            Progress();
            SwitchCurrentTo(roster, "ElfArcher");

            CollectionAssert.IsEmpty(UnlockedIds(),
                "교체는 조건을 넘은 것이 아니다 - 갈아탈 때마다 그 캐릭터의 스킬이 다시 열린다고 나오면 안 된다.");
        }

        [Test]
        public void 줄_캐릭터가_없으면_해금도_없다()
        {
            SaveData document = Inject(State("CatKnight", level: 1));
            UseMemoryStorage();
            CharacterRoster roster = Ready(document, Relation("CatKnight", "cleave", requiredLevel: 2));
            SetPrivate(roster, "current", null);

            PlayerProgress progress = Progress();
            progress.AddExp(50);
            Defeat("Scarecrow");

            Assert.AreEqual(1, document.characters[0].level, "전제 확인 - 아무도 자라지 않았다.");
            CollectionAssert.IsEmpty(UnlockedIds());
        }

        [Test]
        public void 관계_카탈로그가_비어_있으면_아무것도_알리지_않는다()
        {
            SaveData document = Inject(State("CatKnight", level: 1));
            MemoryStorage storage = UseMemoryStorage();
            Ready(document);   // 관계 없음 - 지금 프로덕션 표와 같은 상태다.

            Progress().AddExp(50);

            Assert.AreEqual(6, document.characters[0].level, "성장은 평소대로 일어난다.");
            CollectionAssert.IsEmpty(UnlockedIds(), "표에 관계가 없으면 알릴 것도 없다.");
            Assert.AreEqual(1, storage.WriteCalls);
        }

        [Test]
        public void 카탈로그를_연결하지_않아도_성장은_그대로_동작한다()
        {
            SaveData document = Inject(State("CatKnight", level: 1));
            MemoryStorage storage = UseMemoryStorage();

            // 체크포인트 C에서는 씬을 건드리지 않으므로 이 칸이 비어 있는 것이 실제 상태다.
            Ready(document, skipCatalogs: true,
                relations: new[] { Relation("CatKnight", "cleave", requiredLevel: 2) });

            Progress().AddExp(50);

            Assert.AreEqual(6, document.characters[0].level);
            CollectionAssert.IsEmpty(UnlockedIds(), "연결되지 않은 카탈로그는 조용히 '없음'이다.");
            Assert.AreEqual(1, storage.WriteCalls);
        }

        [Test]
        public void 자란_캐릭터의_관계만_열린다()
        {
            SaveData document = Inject(State("CatKnight", level: 1), State("ElfArcher", level: 1));
            UseMemoryStorage();
            Ready(document,
                Relation("CatKnight", "cleave", requiredLevel: 2),
                Relation("ElfArcher", "shot", requiredLevel: 2));

            Progress().AddExp(10);

            CollectionAssert.AreEqual(new[] { "cleave" }, UnlockedIds(),
                "지금 자란 캐릭터의 관계만 본다 - 다른 캐릭터의 스킬이 함께 열리면 안 된다.");
        }

        // ---- 도우미 ----

        private List<string> UnlockedIds()
        {
            var ids = new List<string>();
            foreach (SkillDefinition skill in seenSkills) ids.Add(skill.SkillId);
            return ids;
        }

        private CharacterRoster Ready(SaveData document, params CharacterSkillDefinition[] relations)
        {
            return Ready(document, false, relations);
        }

        private CharacterRoster Ready(SaveData document, bool skipCatalogs, CharacterSkillDefinition[] relations)
        {
            var skillIds = new List<string>();
            foreach (CharacterSkillDefinition relation in relations)
            {
                if (!skillIds.Contains(relation.SkillId)) skillIds.Add(relation.SkillId);
            }

            skills = skipCatalogs ? null : SkillList(skillIds.ToArray());
            relationCatalog = skipCatalogs ? null : RelationList(relations);

            var host = new GameObject("RosterTestHost");
            Track(host);
            host.SetActive(false);

            CharacterRoster roster = host.AddComponent<CharacterRoster>();
            CharacterCatalog catalog = PlayableCatalog("CatKnight", "ElfArcher");
            SetPrivate(roster, "catalog", catalog);
            SetPrivate(roster, "owned", new OwnedCharacterCollection(catalog, document));

            Invoke(roster, "BuildUsableEntries");
            SetPrivate(roster, "current", FindEntry(roster, "CatKnight"));
            SetRosterInstance(roster);
            return roster;
        }

        /// <summary>PlayerProgress를 비활성 호스트에 붙이고 Awake/OnEnable/Start를 직접 부른다.</summary>
        private PlayerProgress Progress(int expPerDefeat = 1)
        {
            var host = new GameObject("PlayerProgressTestHost");
            Track(host);
            host.SetActive(false);

            PlayerProgress progress = host.AddComponent<PlayerProgress>();
            SetPrivate(progress, "expPerTargetDefeat", expPerDefeat);
            SetPrivate(progress, "expToNextLevel", 10);
            SetPrivate(progress, "skillCatalog", skills);
            SetPrivate(progress, "characterSkillCatalog", relationCatalog);

            Invoke(progress, "Awake");
            Invoke(progress, "OnEnable");
            live.Add(progress);
            Invoke(progress, "Start");
            return progress;
        }

        private static void Defeat(string targetId)
        {
            var handler = (Action<string>)GetStaticEvent(typeof(Target), "AnyTargetDefeated");
            handler?.Invoke(targetId);
        }

        private static void SwitchCurrentTo(CharacterRoster roster, string characterId)
        {
            CharacterDefinition next = FindEntry(roster, characterId);
            Assert.IsNotNull(next, $"'{characterId}'가 로스터 목록에 없습니다.");

            SetPrivate(roster, "current", next);
            var handler = (Action<CharacterDefinition>)GetStaticEvent(
                typeof(CharacterRoster), "CurrentCharacterChanged");
            handler?.Invoke(next);
        }

        private static CharacterDefinition FindEntry(CharacterRoster roster, string characterId)
        {
            foreach (CharacterRoster.Entry entry in roster.Entries)
            {
                if (entry.definition != null && entry.definition.CharacterId == characterId) return entry.definition;
            }
            return null;
        }

        private static SaveData Inject(params CharacterSaveState[] states)
        {
            var document = new SaveData { characters = new List<CharacterSaveState>(states) };
            DataField.SetValue(null, document);
            LoadResultField.SetValue(null, SaveLoadResult.NewGame(document));
            LoadedFromFileField.SetValue(null, false);
            return document;
        }

        private static MemoryStorage UseMemoryStorage()
        {
            SaveData document = SaveSystem.Data;
            var status = (SaveLoadResult)LoadResultField.GetValue(null);
            var loaded = (bool)LoadedFromFileField.GetValue(null);

            var storage = new MemoryStorage();
            ConfigureMethod.Invoke(null, new object[] { storage, null, null });

            DataField.SetValue(null, document);
            LoadResultField.SetValue(null, status);
            LoadedFromFileField.SetValue(null, loaded);
            return storage;
        }

        private sealed class MemoryStorage : ISaveStorage
        {
            public int WriteCalls;

            public bool WritesBlocked => false;

            public string BlockedReason => null;

            public SaveReadResult ReadPrimary() => SaveReadResult.Missing("memory://primary");

            public SaveReadResult ReadBackup() => SaveReadResult.Missing("memory://backup");

            public SaveWriteResult Write(string text)
            {
                WriteCalls++;
                return SaveWriteResult.Written(backupKept: true);
            }

            public SaveQuarantineResult QuarantinePrimary(string reason) =>
                SaveQuarantineResult.Moved("memory://quarantine");
        }

        private static void ClearStaticEvents()
        {
            ClearStaticEvent(typeof(Target), "AnyTargetDefeated");
            ClearStaticEvent(typeof(CharacterRoster), "CurrentCharacterChanged");
            ClearStaticEvent(typeof(CharacterRoster), "CharacterStateChanged");
            ClearStaticEvent(typeof(PlayerProgress), "OnProgressInitialized");
            ClearStaticEvent(typeof(PlayerProgress), "OnCurrentCharacterSynchronized");
            ClearStaticEvent(typeof(PlayerProgress), "OnExperienceChanged");
            ClearStaticEvent(typeof(PlayerProgress), "OnExpGained");
            ClearStaticEvent(typeof(PlayerProgress), "OnLevelUp");
            ClearStaticEvent(typeof(PlayerProgress), "OnSkillUnlocked");
        }

        private static void ClearStaticEvent(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"{type.Name}.{name}의 뒷단 필드를 찾지 못했습니다.");
            field.SetValue(null, null);
        }

        private static Delegate GetStaticEvent(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"{type.Name}.{name}의 뒷단 필드를 찾지 못했습니다.");
            return (Delegate)field.GetValue(null);
        }

        private static void SetRosterInstance(CharacterRoster roster)
        {
            typeof(CharacterRoster)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetSetMethod(nonPublic: true)
                .Invoke(null, new object[] { roster });
        }

        private static object Invoke(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(
                name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{target.GetType().Name}.{name}을 찾지 못했습니다.");
            return method.Invoke(target, null);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{field}를 찾지 못했습니다.");
            info.SetValue(target, value);
        }
    }
}

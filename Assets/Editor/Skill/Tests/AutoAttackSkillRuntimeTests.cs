using Character;
using Common;
using NUnit.Framework;
using Skill;
using TableDataEditor;
using UnityEditor;
using UnityEngine;

namespace SkillEditor.Tests
{
    /// <summary>실제 파일/시간을 쓰지 않고 자동 공격 스킬의 선택과 세션 쿨다운만 고정한다.</summary>
    public sealed class AutoAttackSkillRuntimeTests : SkillUnlockTestBase
    {
        private sealed class FakeTime : AutoAttackSkillRuntime.ITimeSource
        {
            public double NowSeconds { get; set; }
        }

        private readonly FakeTime time = new FakeTime();

        [SetUp]
        public void ResetTime()
        {
            time.NowSeconds = 0d;
        }

        [Test]
        public void 레벨_미달은_선택되지_않고_레벨이_오르면_최초_관찰부터_준비_상태다()
        {
            CharacterSaveState state = State("CatKnight", level: 4);
            SkillDefinition skill = AttackSkill("catknight_skill_01", cooldown: 10f);
            AutoAttackSkillRuntime runtime = Runtime(
                Document(state),
                new[] { skill },
                OrderedRelations(RelationFor("CatKnight", skill, requiredLevel: 5, displayOrder: 10)));

            Assert.IsFalse(runtime.TrySelectReady("CatKnight", out _));

            state.level = 5;
            Assert.IsTrue(runtime.TrySelectReady("CatKnight", out SkillDefinition selected));
            Assert.AreSame(skill, selected, "잠겨 있던 동안 쿨다운이 소비되면 안 된다.");
        }

        [Test]
        public void 선택_조회는_소비하지_않고_실제_시작_표시부터_쿨다운이_흐른다()
        {
            SkillDefinition skill = AttackSkill("claw", cooldown: 10f);
            AutoAttackSkillRuntime runtime = Runtime(
                Document(State("CatKnight", level: 5)),
                new[] { skill },
                OrderedRelations(RelationFor("CatKnight", skill)));

            Assert.IsTrue(runtime.TrySelectReady("CatKnight", out _));
            time.NowSeconds = 50d;
            Assert.IsTrue(runtime.TrySelectReady("CatKnight", out _), "관찰만으로 쿨다운을 시작하면 안 된다.");

            runtime.MarkStarted("CatKnight", skill);
            Assert.IsFalse(runtime.TrySelectReady("CatKnight", out _));
            time.NowSeconds = 59.999d;
            Assert.IsFalse(runtime.TrySelectReady("CatKnight", out _));
            time.NowSeconds = 60d;
            Assert.IsTrue(runtime.TrySelectReady("CatKnight", out SkillDefinition selected));
            Assert.AreSame(skill, selected);
        }

        [Test]
        public void 우선순위는_관계_display_order_이후_skill_id_Ordinal이고_쿨다운_중인_항목은_건너뛴다()
        {
            SkillDefinition later = AttackSkill("zeta", cooldown: 10f);
            SkillDefinition sameOrderLaterId = AttackSkill("beta", cooldown: 10f);
            SkillDefinition first = AttackSkill("alpha", cooldown: 10f);
            AutoAttackSkillRuntime runtime = Runtime(
                Document(State("CatKnight", level: 99)),
                new[] { later, sameOrderLaterId, first },
                // 일부러 목록 순서를 뒤섞어도 런타임 정책이 정답을 고른다.
                OrderedRelations(
                    RelationFor("CatKnight", later, displayOrder: 20),
                    RelationFor("CatKnight", sameOrderLaterId, displayOrder: 10),
                    RelationFor("CatKnight", first, displayOrder: 10)));

            Assert.IsTrue(runtime.TrySelectReady("CatKnight", out SkillDefinition selected));
            Assert.AreSame(first, selected);

            runtime.MarkStarted("CatKnight", first);
            Assert.IsTrue(runtime.TrySelectReady("CatKnight", out selected));
            Assert.AreSame(sameOrderLaterId, selected, "첫 항목이 쿨다운이면 다음 준비 항목을 써야 한다.");
        }

        [Test]
        public void 캐릭터별_쿨다운은_독립이고_필드에서_빠진_동안에도_경과한다()
        {
            SkillDefinition shared = AttackSkill("shared_slash", cooldown: 10f);
            AutoAttackSkillRuntime runtime = Runtime(
                Document(State("CatKnight", level: 5), State("ElfArcher", level: 5)),
                new[] { shared },
                OrderedRelations(
                    RelationFor("CatKnight", shared),
                    RelationFor("ElfArcher", shared)));

            runtime.MarkStarted("CatKnight", shared);
            time.NowSeconds = 1d;
            Assert.IsTrue(runtime.TrySelectReady("ElfArcher", out _), "다른 캐릭터 쿨다운은 독립이다.");
            runtime.MarkStarted("ElfArcher", shared);

            time.NowSeconds = 9.999d;
            Assert.IsFalse(runtime.TrySelectReady("CatKnight", out _));
            time.NowSeconds = 10d;
            Assert.IsTrue(runtime.TrySelectReady("CatKnight", out _), "교체 중에도 CatKnight 시간은 흘렀다.");
            Assert.IsFalse(runtime.TrySelectReady("ElfArcher", out _), "ElfArcher는 1초 뒤 시작했다.");
            time.NowSeconds = 11d;
            Assert.IsTrue(runtime.TrySelectReady("ElfArcher", out _));
        }

        [Test]
        public void 알_수_없는_캐릭터와_실행_데이터_누락은_조용히_일반_공격으로_폴백할_수_있다()
        {
            SkillDefinition wrongBehavior = AttackSkill("passive", cooldown: 10f, behavior: "passive");
            SkillDefinition missingMotion = AttackSkill("missing_motion", cooldown: 10f, withMotion: false);
            SkillDefinition emptyMotion = AttackSkill("empty_motion", cooldown: 10f, playableMotion: false);
            SkillDefinition badCooldown = AttackSkill("bad_cooldown", cooldown: 0f);

            AutoAttackSkillRuntime runtime = Runtime(
                Document(State("CatKnight", level: 99)),
                new[] { wrongBehavior, missingMotion, emptyMotion, badCooldown },
                OrderedRelations(
                    RelationFor("CatKnight", wrongBehavior),
                    RelationFor("CatKnight", missingMotion),
                    RelationFor("CatKnight", emptyMotion),
                    RelationFor("CatKnight", badCooldown)));

            Assert.IsFalse(runtime.TrySelectReady("Unknown", out _));
            Assert.IsFalse(runtime.TrySelectReady("CatKnight", out _));
            Assert.DoesNotThrow(() => runtime.MarkStarted("CatKnight", missingMotion));
        }

        [Test]
        public void 새_런타임_객체는_같은_실행_데이터라도_준비_상태로_다시_시작한다()
        {
            SaveData document = Document(State("CatKnight", level: 5));
            SkillDefinition skill = AttackSkill("claw", cooldown: 10f);
            CharacterSkillCatalog relations = OrderedRelations(RelationFor("CatKnight", skill));
            SkillCatalog skills = SkillList(skill);
            CharacterCatalog characters = Catalog("CatKnight");

            var first = new AutoAttackSkillRuntime(characters, skills, relations, document, time);
            first.MarkStarted("CatKnight", skill);
            Assert.IsFalse(first.TrySelectReady("CatKnight", out _));

            var restarted = new AutoAttackSkillRuntime(characters, skills, relations, document, time);
            Assert.IsTrue(restarted.TrySelectReady("CatKnight", out _), "저장/마이그레이션 없이 실행 재시작 시 초기화한다.");
        }

        [Test]
        public void 프로덕션_CatKnight_샘플은_레벨5부터_10초_공격_스킬로_선택된다()
        {
            CharacterCatalog characters = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(
                TableDataPaths.CharacterCatalogAssetPath);
            SkillCatalog skills = AssetDatabase.LoadAssetAtPath<SkillCatalog>(TableDataPaths.SkillCatalogAssetPath);
            CharacterSkillCatalog relations = AssetDatabase.LoadAssetAtPath<CharacterSkillCatalog>(
                TableDataPaths.CharacterSkillCatalogAssetPath);

            Assert.IsNotNull(characters);
            Assert.IsNotNull(skills);
            Assert.IsNotNull(relations);

            var runtime = new AutoAttackSkillRuntime(
                characters,
                skills,
                relations,
                Document(State("CatKnight", level: 5)),
                time);

            Assert.IsTrue(runtime.TrySelectReady("CatKnight", out SkillDefinition selected));
            Assert.AreEqual("catknight_skill_01", selected.SkillId);
            Assert.AreEqual(10f, selected.CooldownSeconds);
            Assert.AreEqual("CatKnight_Skill_01", selected.AttackMotion.name);
        }

        private AutoAttackSkillRuntime Runtime(
            SaveData document,
            SkillDefinition[] skillDefinitions,
            CharacterSkillCatalog relationDefinitions)
        {
            return new AutoAttackSkillRuntime(
                Catalog("CatKnight", "ElfArcher"),
                SkillList(skillDefinitions),
                relationDefinitions,
                document,
                time);
        }

        private SkillDefinition AttackSkill(
            string id,
            float cooldown,
            string behavior = AutoAttackSkillRuntime.AttackMotionBehaviorKey,
            bool withMotion = true,
            bool playableMotion = true)
        {
            SkillDefinition skill = SkillAsset(id);
            var serialized = new SerializedObject(skill);
            serialized.FindProperty("behaviorKey").stringValue = behavior;
            serialized.FindProperty("cooldownSeconds").floatValue = cooldown;
            if (withMotion) serialized.FindProperty("attackMotion").objectReferenceValue = Motion(playableMotion);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return skill;
        }

        private AttackMotionDefinition Motion(bool playable)
        {
            var motion = Track(ScriptableObject.CreateInstance<AttackMotionDefinition>());
            if (!playable) return motion;

            var texture = Track(new Texture2D(2, 2));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f)));
            var serialized = new SerializedObject(motion);
            SerializedProperty frames = serialized.FindProperty("frames");
            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return motion;
        }

        private CharacterSkillDefinition RelationFor(
            string characterId,
            SkillDefinition skill,
            int requiredLevel = 1,
            int displayOrder = 0)
        {
            CharacterSkillDefinition relation = Relation(
                characterId,
                skill.SkillId,
                requiredLevel,
                skillRef: skill);
            var serialized = new SerializedObject(relation);
            serialized.FindProperty("displayOrder").intValue = displayOrder;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return relation;
        }

        private CharacterSkillCatalog OrderedRelations(params CharacterSkillDefinition[] relations)
        {
            return RelationList(relations);
        }
    }
}

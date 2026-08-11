using System;
using System.Collections.Generic;
using Character;
using Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CharacterEditor.Tests
{
    /// <summary>
    /// 보유 경계(<see cref="OwnedCharacterCollection"/>) 시험.
    ///
    /// <b>디스크도 씬도 건드리지 않는다.</b> 카탈로그와 저장 문서를 메모리에서 만들어 넣으므로
    /// persistentDataPath도 <see cref="SaveSystem"/>도 지나가지 않는다 - 이 클래스가 저장소를 모르게
    /// 만든 이유가 바로 여기 있다.
    ///
    /// 확인하는 계약은 셋이다.
    /// 1. 읽는 동작은 저장 문서를 <b>한 항목도</b> 바꾸지 않는다.
    /// 2. 보유 판정은 CharacterId의 Ordinal 완전 일치이고, 목록의 차례는 카탈로그가 정한다.
    /// 3. 항목을 더하는 경로는 <see cref="OwnedCharacterCollection.InitializeNewGame"/> 하나뿐이며
    ///    여러 번 불러도 결과가 같다.
    /// </summary>
    public sealed class OwnedCharacterCollectionTests
    {
        private static readonly string[] SixIds =
        {
            "CatKnight", "ElfArcher", "Barbarian", "ElfGuardian", "RabbitHealer", "CatMage",
        };

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object asset in created)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }

            created.Clear();
        }

        // ---- 카탈로그 노출 ----

        [Test]
        public void AllCharacters_ExposesEveryActiveCatalogEntry()
        {
            OwnedCharacterCollection collection = New(Catalog(SixIds), Document());

            CollectionAssert.AreEqual(SixIds, IdsOf(collection.AllCharacters),
                "카탈로그가 담은 활성 캐릭터 전체가 그 차례 그대로 보여야 한다.");
        }

        [Test]
        public void AllCharacters_IsIndependentOfOwnership()
        {
            OwnedCharacterCollection collection = New(Catalog(SixIds), Document());

            Assert.AreEqual(6, collection.AllCharacters.Count, "보유가 하나도 없어도 카탈로그는 여섯이다.");
            Assert.AreEqual(0, collection.OwnedCount);
        }

        [Test]
        public void NullCatalogOrDocument_MeansNothingIsOwned()
        {
            Assert.AreEqual(0, New(null, Document()).AllCharacters.Count);
            Assert.AreEqual(0, New(null, Document()).OwnedCount);
            Assert.AreEqual(0, New(Catalog(SixIds), null).OwnedCount);
            Assert.IsFalse(New(Catalog(SixIds), null).IsOwned("CatKnight"));
        }

        // ---- 보유 목록 ----

        [Test]
        public void OwnedCharacters_IsTheSavedSubsetInCatalogOrder()
        {
            // 저장 목록의 차례(CatMage가 먼저)와 무관하게 카탈로그 차례로 나와야 한다.
            SaveData document = Document(State("CatMage"), State("ElfArcher"));
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            CollectionAssert.AreEqual(new[] { "ElfArcher", "CatMage" }, IdsOf(collection.OwnedCharacters),
                "보유 목록의 차례는 카탈로그(표의 display_order)가 정한다.");
            Assert.AreEqual(2, collection.OwnedCount);
        }

        [Test]
        public void IsOwned_IsOrdinalExact()
        {
            OwnedCharacterCollection collection = New(Catalog(SixIds), Document(State("Barbarian")));

            Assert.IsTrue(collection.IsOwned("Barbarian"));
            Assert.IsFalse(collection.IsOwned("barbarian"), "대소문자가 다르면 다른 캐릭터다.");
            Assert.IsFalse(collection.IsOwned("BARBARIAN"));
            Assert.IsFalse(collection.IsOwned(" Barbarian"));
            Assert.IsFalse(collection.IsOwned((string)null));
            Assert.IsFalse(collection.IsOwned(string.Empty));
        }

        [Test]
        public void IsOwned_ByDefinition_ComparesIdNotReference()
        {
            CharacterCatalog catalog = Catalog(SixIds);
            OwnedCharacterCollection collection = New(catalog, Document(State("Barbarian")));

            // 같은 id를 가진 <b>다른 에셋</b>(수동 에셋에 해당). 참조로 비교하면 여기서 false가 된다.
            CharacterDefinition other = Definition("Barbarian");

            Assert.IsTrue(collection.IsOwned(other), "판정 근거는 CharacterId이지 에셋 참조가 아니다.");
            Assert.IsFalse(collection.IsOwned((CharacterDefinition)null));
        }

        [Test]
        public void SaveOnlyIds_ArePreservedButNeverAppearAsOwnedCharacters()
        {
            SaveData document = Document(State("CatKnight"), State("GhostHero"));
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            CollectionAssert.AreEqual(new[] { "CatKnight" }, IdsOf(collection.OwnedCharacters),
                "카탈로그에 없는 id는 지금 빌드에서 쓸 수 없으므로 목록에 나오지 않는다.");
            Assert.IsTrue(collection.IsOwned("GhostHero"), "그래도 보유 사실 자체는 남아 있다.");
            Assert.AreEqual(2, document.characters.Count, "모르는 id를 지우면 안 된다.");
            Assert.AreEqual("GhostHero", document.characters[1].characterId);
        }

        [Test]
        public void DuplicateSavedStates_YieldTheCharacterOnceAndTheFirstState()
        {
            SaveData document = Document(
                State("CatMage", level: 7, stamina: 3),
                State("CatMage", level: 1, stamina: 0));

            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            CollectionAssert.AreEqual(new[] { "CatMage" }, IdsOf(collection.OwnedCharacters),
                "카탈로그를 훑으므로 중복이 있어도 한 번만 나온다.");
            Assert.IsTrue(collection.TryGetState("CatMage", out CharacterSaveState state));
            Assert.AreEqual(7, state.level, "중복이면 먼저 나온 항목을 쓴다.");
            Assert.AreEqual(2, document.characters.Count, "중복을 합치거나 지우지 않는다.");
        }

        [Test]
        public void NullSavedEntries_AreSkippedButKept()
        {
            SaveData document = Document(null, State("CatKnight"), null);
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            CollectionAssert.AreEqual(new[] { "CatKnight" }, IdsOf(collection.OwnedCharacters));
            Assert.AreEqual(3, document.characters.Count, "null 항목을 치우는 것은 이 클래스의 일이 아니다.");
            Assert.IsNull(document.characters[0]);
        }

        // ---- 읽기는 문서를 바꾸지 않는다 ----

        [Test]
        public void ReadingNeverCreatesOrRemovesState()
        {
            SaveData document = Document(State("CatKnight", level: 4, stamina: 9), State("GhostHero"));
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            string before = Describe(document);

            // 있는 것, 없는 것, 모르는 것, 대소문자만 다른 것을 모두 물어본다.
            _ = collection.AllCharacters;
            _ = collection.OwnedCharacters;
            _ = collection.OwnedCount;
            _ = collection.IsOwned("CatKnight");
            _ = collection.IsOwned("ElfArcher");
            _ = collection.IsOwned("catknight");
            _ = collection.IsOwned("GhostHero");
            _ = collection.Find("CatMage");
            collection.TryGetState("CatKnight", out _);
            collection.TryGetState("ElfArcher", out _);
            collection.TryGetState("nope", out _);

            Assert.AreEqual(before, Describe(document),
                "조회만으로 저장 목록의 개수나 내용이 달라지면 안 된다.");
        }

        [Test]
        public void TryGetState_ForUnownedReturnsFalseWithoutCreating()
        {
            SaveData document = Document(State("CatKnight"));
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            Assert.IsFalse(collection.TryGetState("ElfArcher", out CharacterSaveState state));
            Assert.IsNull(state);
            Assert.AreEqual(1, document.characters.Count, "조회가 캐릭터를 지급하면 안 된다.");
        }

        [Test]
        public void TryGetState_ReturnsTheLiveStateSoCallersCanEditStamina()
        {
            SaveData document = Document(State("CatKnight", level: 2, stamina: 5));
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            Assert.IsTrue(collection.TryGetState("CatKnight", out CharacterSaveState state));
            state.currentStamina = 1;

            Assert.AreEqual(1, document.characters[0].currentStamina, "사본이 아니라 그 항목이어야 한다.");
        }

        [Test]
        public void OwnedCharacters_ReturnsAnIndependentSnapshotThatCannotChangeBehindTheCaller()
        {
            // 재사용 버퍼를 돌려주면, 다음 조회가 <b>이미 건네준 목록</b>을 비우고 다시 채운다 -
            // 목록을 받아 두고 나중에 훑는 코드가 그때 다른 답을 보게 된다.
            SaveData document = Document(State("CatKnight"));
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            IReadOnlyList<CharacterDefinition> first = collection.OwnedCharacters;
            CollectionAssert.AreEqual(new[] { "CatKnight" }, IdsOf(first));

            // 보유가 늘어난 뒤 다시 묻는다.
            document.characters.Add(State("CatMage"));
            IReadOnlyList<CharacterDefinition> second = collection.OwnedCharacters;

            CollectionAssert.AreEqual(new[] { "CatKnight" }, IdsOf(first),
                "먼저 받은 목록은 나중 조회에 흔들리지 않아야 한다.");
            CollectionAssert.AreEqual(new[] { "CatKnight", "CatMage" }, IdsOf(second),
                "새 조회는 지금 상태를 보여 준다.");
            Assert.AreNotSame(first, second, "조회마다 독립적인 목록이어야 한다.");
        }

        [Test]
        public void OwnedCharacters_SnapshotSurvivesOwnershipRemovalToo()
        {
            SaveData document = Document(State("CatKnight"), State("CatMage"));
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            IReadOnlyList<CharacterDefinition> before = collection.OwnedCharacters;
            Assert.AreEqual(2, before.Count);

            document.characters.Clear();

            Assert.AreEqual(2, before.Count, "먼저 받은 스냅샷은 그대로 남는다.");
            Assert.AreEqual(0, collection.OwnedCharacters.Count, "새 조회는 비어 있다.");
        }

        // ---- 새 게임 초기 지급 ----

        [Test]
        public void InitializeNewGame_AddsOnlyInitiallyOwnedDefinitions()
        {
            CharacterCatalog catalog = Catalog(
                Definition("CatKnight", initiallyOwned: true),
                Definition("ElfArcher", initiallyOwned: false),
                Definition("Barbarian", initiallyOwned: true));

            SaveData document = Document();
            Assert.AreEqual(2, New(catalog, document).InitializeNewGame());

            CollectionAssert.AreEqual(new[] { "CatKnight", "Barbarian" }, IdsOf(document.characters),
                "initially_owned가 꺼진 캐릭터는 새 게임에서도 주지 않는다.");

            foreach (CharacterSaveState state in document.characters)
            {
                Assert.AreEqual(1, state.level, $"{state.characterId}의 레벨");
                Assert.AreEqual(-1, state.currentStamina,
                    $"{state.characterId}의 행동력 - -1(아직 초기화되지 않음)이어야 한다.");
            }
        }

        [Test]
        public void InitializeNewGame_AddsEachIdExactlyOnce()
        {
            SaveData document = Document();
            New(Catalog(SixIds), document).InitializeNewGame();

            CollectionAssert.AreEqual(SixIds, IdsOf(document.characters));
            Assert.AreEqual(6, document.characters.Count);
        }

        [Test]
        public void InitializeNewGame_IsIdempotent()
        {
            SaveData document = Document();
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            Assert.AreEqual(6, collection.InitializeNewGame());
            string afterFirst = Describe(document);

            Assert.AreEqual(0, collection.InitializeNewGame(), "두 번째 호출은 아무것도 더하지 않는다.");
            Assert.AreEqual(afterFirst, Describe(document));
        }

        [Test]
        public void InitializeNewGame_PreservesExistingStatesExactly()
        {
            SaveData document = Document(State("Barbarian", level: 9, stamina: 2));
            OwnedCharacterCollection collection = New(Catalog(SixIds), document);

            Assert.AreEqual(5, collection.InitializeNewGame(), "이미 있는 하나를 뺀 다섯만 더한다.");

            Assert.AreEqual("Barbarian", document.characters[0].characterId, "있던 항목이 자리를 지킨다.");
            Assert.AreEqual(9, document.characters[0].level, "진행 값을 초기값으로 덮어쓰면 안 된다.");
            Assert.AreEqual(2, document.characters[0].currentStamina);
            Assert.AreEqual(6, document.characters.Count);
        }

        [Test]
        public void InitializeNewGame_PreservesNullDuplicateAndUnknownEntries()
        {
            SaveData document = Document(
                null,
                State("GhostHero", level: 3, stamina: 4),
                State("CatMage", level: 5, stamina: 1),
                State("CatMage", level: 1, stamina: 0));

            OwnedCharacterCollection collection = New(Catalog(SixIds), document);
            Assert.AreEqual(5, collection.InitializeNewGame(), "CatMage만 이미 있으므로 다섯을 더한다.");

            Assert.IsNull(document.characters[0], "null 항목을 치우지 않는다.");
            Assert.AreEqual("GhostHero", document.characters[1].characterId, "모르는 id를 지우지 않는다.");
            Assert.AreEqual("CatMage", document.characters[2].characterId);
            Assert.AreEqual(5, document.characters[2].level);
            Assert.AreEqual("CatMage", document.characters[3].characterId, "중복을 합치지 않는다.");
            Assert.AreEqual(9, document.characters.Count);
        }

        [Test]
        public void InitializeNewGame_IsOrdinalExactSoCaseVariantsAreSeparate()
        {
            // 'barbarian'을 가지고 있어도 'Barbarian'은 없는 것이므로 새로 지급된다.
            SaveData document = Document(State("barbarian", level: 4, stamina: 8));
            New(Catalog(SixIds), document).InitializeNewGame();

            Assert.AreEqual(7, document.characters.Count);
            Assert.AreEqual("barbarian", document.characters[0].characterId);
            Assert.AreEqual(4, document.characters[0].level, "다른 키의 진행을 건드리지 않는다.");
            CollectionAssert.Contains(IdsOf(document.characters), "Barbarian");
        }

        [Test]
        public void InitializeNewGame_CreatesTheListOnlyWhenItHasSomethingToAdd()
        {
            SaveData document = Document();
            document.characters = null;

            New(Catalog(SixIds), document).InitializeNewGame();
            Assert.IsNotNull(document.characters, "지급할 것이 있으면 목록을 만든다.");
            Assert.AreEqual(6, document.characters.Count);

            SaveData empty = Document();
            empty.characters = null;
            Assert.AreEqual(0, New(Catalog(new CharacterDefinition[0]), empty).InitializeNewGame());
            Assert.IsNull(empty.characters, "더할 것이 없으면 목록을 만들 이유도 없다.");
        }

        // ---- 도우미 ----

        private static OwnedCharacterCollection New(CharacterCatalog catalog, SaveData document)
        {
            return new OwnedCharacterCollection(catalog, document);
        }

        private CharacterCatalog Catalog(params string[] ids)
        {
            var definitions = new CharacterDefinition[ids.Length];
            for (int i = 0; i < ids.Length; i++) definitions[i] = Definition(ids[i], initiallyOwned: true);
            return Catalog(definitions);
        }

        private CharacterCatalog Catalog(params CharacterDefinition[] definitions)
        {
            var catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            created.Add(catalog);

            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("characters");
            list.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();
            return catalog;
        }

        /// <summary>메모리에만 있는 정의. 디스크에는 아무것도 남지 않는다.</summary>
        private CharacterDefinition Definition(string id, bool initiallyOwned = true, int maxStamina = 30)
        {
            var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(definition);

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("characterId").stringValue = id;
            serialized.FindProperty("initiallyOwned").boolValue = initiallyOwned;
            serialized.FindProperty("maxStamina").intValue = maxStamina;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private static SaveData Document(params CharacterSaveState[] states)
        {
            return new SaveData { characters = new List<CharacterSaveState>(states) };
        }

        private static CharacterSaveState State(string id, int level = 1, int stamina = -1)
        {
            return new CharacterSaveState { characterId = id, level = level, currentStamina = stamina };
        }

        private static List<string> IdsOf(IReadOnlyList<CharacterDefinition> definitions)
        {
            var ids = new List<string>();
            foreach (CharacterDefinition definition in definitions) ids.Add(definition?.CharacterId);
            return ids;
        }

        private static List<string> IdsOf(List<CharacterSaveState> states)
        {
            var ids = new List<string>();
            foreach (CharacterSaveState state in states) ids.Add(state?.characterId);
            return ids;
        }

        /// <summary>저장 목록 전체를 한 문자열로. "바뀌지 않았다"를 개수만이 아니라 내용까지 본다.</summary>
        private static string Describe(SaveData document)
        {
            if (document.characters == null) return "(null)";

            var parts = new List<string>();
            foreach (CharacterSaveState state in document.characters)
            {
                parts.Add(state == null
                    ? "(null)"
                    : $"{state.characterId}:{state.level}:{state.currentStamina}");
            }

            return string.Join("|", parts);
        }
    }
}

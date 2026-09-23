using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using Dungeon;
using Enemy;
using Field;
using NUnit.Framework;
using Recovery;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DungeonEditor.Tests
{
    public sealed class DungeonPartyRestEventControllerTests
    {
        private const string ScenePath = "Assets/Scenes/desktopScene_ReSize.unity";
        private const string CampfirePrefabPath = "Assets/Art/Environment/campfire/campfire.prefab";
        private const string ActorOutlineMaterialPath = "Assets/Materials/ActorOuterOutline.mat";
        private static readonly int GrayscaleAmountId = Shader.PropertyToID("_GrayscaleAmount");
        private static readonly int TestPropertyId = Shader.PropertyToID("_RestGrayscaleTestValue");

        private static readonly FieldInfo DataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo LoadResultField =
            typeof(SaveSystem).GetField("loadResult", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ConfigureSaveMethod =
            typeof(SaveSystem).GetMethod("ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private DungeonPartyRestEventController subscribedController;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(DataField);
            Assert.IsNotNull(LoadResultField);
            Assert.IsNotNull(ConfigureSaveMethod);
        }

        [TestCase(31, 0.3f, 10)]
        [TestCase(30, 0.3f, 9)]
        [TestCase(1, 0.01f, 1)]
        [TestCase(0, 0.3f, 0)]
        [TestCase(100, 2f, 100)]
        [TestCase(100, -1f, 1)]
        public void CalculateRequiredStamina_UsesCeilingAndControllerRatioClamp(
            int maximum,
            float ratio,
            int expected)
        {
            Assert.AreEqual(
                expected,
                DungeonPartyRestEventController.CalculateRequiredStamina(maximum, ratio));
        }

        [TearDown]
        public void TearDown()
        {
            if (subscribedController != null)
                Invoke(subscribedController, "OnDisable");

            ConfigureSaveMethod.Invoke(null, new object[] { null, null, null });

            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
            }

            created.Clear();
            subscribedController = null;
        }

        [Test]
        public void RecoverySlotsChanged_UsesOnlyCombatAvailableMemberForRestAndPresentation()
        {
            CharacterDefinition active = Definition("CatKnight", 30);
            CharacterDefinition recoveringA = Definition("ElfArcher", 30);
            CharacterDefinition recoveringB = Definition("CatMage", 30);
            SaveData data = Inject(
                State(active.CharacterId, 0),
                State(recoveringA.CharacterId, 14),
                State(recoveringB.CharacterId, 22));
            CharacterRoster roster = ReadyRoster(data, active, recoveringA, recoveringB);
            SetPrivate(roster, "current", active);

            DungeonPartyRestEventController controller = Controller(roster, active, out SpriteRenderer[] restRenderers);
            Invoke(controller, "OnEnable");
            subscribedController = controller;

            // 회복 등록 전에는 행동력이 남은 두 파티원이 있으므로 모닥불이 시작되지 않는다.
            RecoveryService.NotifyRosterChangedAfterExternalSave();
            Assert.IsFalse(controller.IsResting);

            data.recoverySlots = new List<RecoverySlotSaveState>
            {
                new RecoverySlotSaveState { characterId = recoveringA.CharacterId },
                new RecoverySlotSaveState { characterId = recoveringB.CharacterId },
            };

            // 회복소의 기존 변경 신호만으로 즉시 재평가한다. 회복 중 캐릭터의 양수 행동력은
            // 발생 조건을 막지 않고, 휴식 연출에도 나타나지 않아야 한다.
            RecoveryService.NotifyRosterChangedAfterExternalSave();

            Assert.IsTrue(controller.IsResting);
            CollectionAssert.AreEqual(new[] { active.CharacterId }, BufferedIds(controller));
            Assert.AreSame(active.MotionProfile.BaseIdle.Frames[0], restRenderers[0].sprite);
            Assert.IsTrue(restRenderers[0].enabled);
            Assert.IsFalse(restRenderers[1].enabled);
            Assert.IsFalse(restRenderers[2].enabled);

            // 회복 중인 두 명이 아직 슬롯에 있을 때도 복귀 기준은 전투 가능 멤버 한 명만 본다.
            data.characters[0].currentStamina = active.MaxStamina;
            RecoveryService.NotifyRosterChangedAfterExternalSave();
            Assert.IsFalse(controller.IsResting);
        }

        [Test]
        public void RestSlotStatusQuery_PreservesPartyOrderAndRecoveryExclusion()
        {
            CharacterDefinition first = Definition("CatKnight", 31);
            CharacterDefinition recovering = Definition("ElfArcher", 60);
            CharacterDefinition third = Definition("CatMage", 50);
            SaveData data = Inject(
                State(first.CharacterId, 0),
                State(recovering.CharacterId, 0),
                State(third.CharacterId, 0));
            data.recoverySlots = new List<RecoverySlotSaveState>
            {
                new RecoverySlotSaveState { characterId = recovering.CharacterId },
            };

            CharacterRoster roster = ReadyRoster(data, first, recovering, third);
            SetPrivate(roster, "current", first);
            DungeonPartyRestEventController controller = Controller(roster, first, out _);
            Assert.That(controller.ResumeStaminaRatio, Is.EqualTo(0.3f).Within(0.0001f));
            Invoke(controller, "OnEnable");
            subscribedController = controller;

            RecoveryService.NotifyRosterChangedAfterExternalSave();
            Assert.IsTrue(controller.IsResting);

            data.characters[0].currentStamina = 9;
            data.characters[1].currentStamina = 55;
            data.characters[2].currentStamina = 20;

            Assert.IsTrue(controller.TryGetRestSlotStatus(
                0,
                out DungeonPartyRestEventController.RestSlotStatus firstStatus));
            Assert.AreEqual(first.CharacterId, firstStatus.CharacterId);
            Assert.AreEqual(9, firstStatus.CurrentStamina);
            Assert.AreEqual(10, firstStatus.RequiredStamina);

            Assert.IsTrue(controller.TryGetRestSlotStatus(
                1,
                out DungeonPartyRestEventController.RestSlotStatus secondStatus));
            Assert.AreEqual(third.CharacterId, secondStatus.CharacterId,
                "회복 중인 가운데 파티원은 기존 partyBuffer 정책대로 빠져야 합니다.");
            Assert.AreEqual(20, secondStatus.CurrentStamina);
            Assert.AreEqual(15, secondStatus.RequiredStamina);

            Assert.IsFalse(controller.TryGetRestSlotStatus(2, out _));
            Assert.IsFalse(controller.TryGetRestSlotStatus(-1, out _));
        }

        [Test]
        public void RecoverySlotRemoval_ReevaluatesResumeAgainstNewEligibleParty()
        {
            CharacterDefinition active = Definition("CatKnight", 30);
            CharacterDefinition recovering = Definition("ElfArcher", 30);
            SaveData data = Inject(State(active.CharacterId, 0), State(recovering.CharacterId, 1));
            data.recoverySlots = new List<RecoverySlotSaveState>
            {
                new RecoverySlotSaveState { characterId = recovering.CharacterId },
            };

            CharacterRoster roster = ReadyRoster(data, active, recovering);
            SetPrivate(roster, "current", active);
            DungeonPartyRestEventController controller = Controller(roster, active, out _);
            Invoke(controller, "OnEnable");
            subscribedController = controller;

            RecoveryService.NotifyRosterChangedAfterExternalSave();
            Assert.IsTrue(controller.IsResting);

            data.characters[0].currentStamina = active.MaxStamina;
            data.recoverySlots.Clear();
            RecoveryService.NotifyRosterChangedAfterExternalSave();

            Assert.IsTrue(controller.IsResting,
                "합류해 다시 유효해진 파티원의 행동력이 복귀 기준 미만이면 휴식을 끝내면 안 된다.");
            CollectionAssert.AreEqual(new[] { active.CharacterId, recovering.CharacterId }, BufferedIds(controller));

            data.characters[1].currentStamina = recovering.MaxStamina;
            RecoveryService.NotifyRosterChangedAfterExternalSave();
            Assert.IsFalse(controller.IsResting);
        }

        [Test]
        public void CampfireLifecycle_CreatesOneConfiguredInstanceAndReusesItAcrossRestEvents()
        {
            CharacterDefinition active = Definition("CatKnight", 30);
            SaveData data = Inject(State(active.CharacterId, 0));
            CharacterRoster roster = ReadyRoster(data, active);
            SetPrivate(roster, "current", active);
            DungeonPartyRestEventController controller = Controller(roster, active, out _);

            var prefab = new GameObject("CampfirePrefab");
            created.Add(prefab);
            SetPrivate(controller, "campfirePrefab", prefab);
            SetPrivate(controller, "campfireLocalPosition", new Vector3(1f, 2f, 3f));
            SetPrivate(controller, "campfireLocalEulerAngles", new Vector3(0f, 0f, 25f));
            SetPrivate(controller, "campfireLocalScale", new Vector3(2f, 3f, 1f));

            Invoke(controller, "StartRest");

            GameObject firstInstance = (GameObject)GetPrivate(controller, "campfireInstance");
            GameObject root = (GameObject)GetPrivate(controller, "restEventRoot");
            Assert.IsNotNull(firstInstance);
            Assert.AreSame(root.transform, firstInstance.transform.parent);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), firstInstance.transform.localPosition);
            Assert.That(firstInstance.transform.localEulerAngles.z, Is.EqualTo(25f).Within(0.01f));
            Assert.AreEqual(new Vector3(2f, 3f, 1f), firstInstance.transform.localScale);
            Assert.IsTrue(firstInstance.activeInHierarchy);
            Assert.AreEqual(4, root.transform.childCount);

            Invoke(controller, "StopRest", false);
            Assert.IsFalse(firstInstance.activeSelf);

            Invoke(controller, "StartRest");
            GameObject secondInstance = (GameObject)GetPrivate(controller, "campfireInstance");
            Assert.AreSame(firstInstance, secondInstance);
            Assert.AreEqual(4, root.transform.childCount, "반복 휴식 시 모닥불 인스턴스가 누적되면 안 됩니다.");
            Assert.IsTrue(secondInstance.activeInHierarchy);
        }

        [Test]
        public void MissingCampfirePrefab_DoesNotPreventRestEvent()
        {
            CharacterDefinition active = Definition("CatKnight", 30);
            SaveData data = Inject(State(active.CharacterId, 0));
            CharacterRoster roster = ReadyRoster(data, active);
            SetPrivate(roster, "current", active);
            DungeonPartyRestEventController controller = Controller(roster, active, out _);

            LogAssert.Expect(LogType.Warning,
                "[DungeonPartyRestEvent] Campfire Prefab이 지정되지 않았습니다. 모닥불 없이 캐릭터 휴식 이벤트를 계속 진행합니다.");
            Invoke(controller, "StartRest");

            Assert.IsTrue(controller.IsResting);
            Assert.IsNull(GetPrivate(controller, "campfireInstance"));
        }

        [Test]
        public void RestLifecycle_GrayscalesCurrentAndStandbyWithoutChangingSharedMaterials_ThenRestoresBlocks()
        {
            CharacterDefinition active = Definition("CatKnight", 30);
            SaveData data = Inject(State(active.CharacterId, 0));
            CharacterRoster roster = ReadyRoster(data, active);
            SetPrivate(roster, "current", active);
            DungeonPartyRestEventController controller = Controller(roster, active, out _);

            MonsterEncounterQueue queue = MonsterQueue(
                out SpriteRenderer currentRenderer,
                out SpriteRenderer standbyRenderer,
                out _,
                out _);
            var decoration = new GameObject("MonsterAttachedDecoration");
            decoration.transform.SetParent(currentRenderer.transform, false);
            SpriteRenderer decorationRenderer = decoration.AddComponent<SpriteRenderer>();
            created.Add(decoration);
            SetPrivate(controller, "monsterEncounterQueue", queue);
            SetPrivate(controller, "grayscaleAmount", 0.75f);

            Material actorMaterial = AssetDatabase.LoadAssetAtPath<Material>(ActorOutlineMaterialPath);
            Assert.IsNotNull(actorMaterial);
            Assert.IsTrue(actorMaterial.HasProperty(GrayscaleAmountId));
            currentRenderer.sharedMaterial = actorMaterial;
            standbyRenderer.sharedMaterial = actorMaterial;

            var originalCurrentBlock = new MaterialPropertyBlock();
            originalCurrentBlock.SetFloat(TestPropertyId, 0.42f);
            currentRenderer.SetPropertyBlock(originalCurrentBlock);
            Material currentMaterialBeforeRest = currentRenderer.sharedMaterial;
            Material standbyMaterialBeforeRest = standbyRenderer.sharedMaterial;

            Invoke(controller, "StartRest");

            Assert.That(ReadRendererFloat(currentRenderer, GrayscaleAmountId), Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(ReadRendererFloat(standbyRenderer, GrayscaleAmountId), Is.EqualTo(0.75f).Within(0.0001f));
            Assert.IsTrue(RendererPropertyBlockIsEmpty(decorationRenderer),
                "몬스터에 붙은 장식은 휴식 중에도 변경되지 않아야 합니다.");
            Assert.That(ReadRendererFloat(currentRenderer, TestPropertyId), Is.EqualTo(0.42f).Within(0.0001f),
                "기존 렌더러별 프로퍼티는 휴식 효과와 함께 보존되어야 합니다.");
            Assert.AreSame(currentMaterialBeforeRest, currentRenderer.sharedMaterial);
            Assert.AreSame(standbyMaterialBeforeRest, standbyRenderer.sharedMaterial);

            Invoke(controller, "StopRest", false);

            Assert.That(ReadRendererFloat(currentRenderer, GrayscaleAmountId), Is.Zero.Within(0.0001f));
            Assert.That(ReadRendererFloat(standbyRenderer, GrayscaleAmountId), Is.Zero.Within(0.0001f));
            Assert.That(ReadRendererFloat(currentRenderer, TestPropertyId), Is.EqualTo(0.42f).Within(0.0001f));
            Assert.IsTrue(RendererPropertyBlockIsEmpty(standbyRenderer),
                "원래 프로퍼티 블록이 없던 렌더러는 휴식 종료 후 빈 상태로 정확히 돌아가야 합니다.");
            Assert.AreSame(currentMaterialBeforeRest, currentRenderer.sharedMaterial);
            Assert.AreSame(standbyMaterialBeforeRest, standbyRenderer.sharedMaterial);
        }

        [Test]
        public void MonsterQueuePresentationEvents_RefreshTargetsWithoutAccumulation_AndDisableRestoresAll()
        {
            CharacterDefinition active = Definition("CatKnight", 30);
            SaveData data = Inject(State(active.CharacterId, 0));
            CharacterRoster roster = ReadyRoster(data, active);
            SetPrivate(roster, "current", active);
            DungeonPartyRestEventController controller = Controller(roster, active, out _);

            MonsterEncounterQueue queue = MonsterQueue(
                out SpriteRenderer firstRenderer,
                out SpriteRenderer secondRenderer,
                out TargetCombatController firstMonster,
                out TargetCombatController secondMonster);
            TargetCombatController replacementMonster = Monster("RestMonsterReplacement", out SpriteRenderer replacementRenderer);
            Material actorMaterial = AssetDatabase.LoadAssetAtPath<Material>(ActorOutlineMaterialPath);
            Assert.IsNotNull(actorMaterial);
            firstRenderer.sharedMaterial = actorMaterial;
            secondRenderer.sharedMaterial = actorMaterial;
            replacementRenderer.sharedMaterial = actorMaterial;
            SetPrivate(controller, "monsterEncounterQueue", queue);
            Invoke(controller, "OnEnable");
            subscribedController = controller;
            Invoke(controller, "StartRest");

            Assert.That(ReadRendererFloat(firstRenderer, GrayscaleAmountId), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(ReadRendererFloat(secondRenderer, GrayscaleAmountId), Is.EqualTo(1f).Within(0.0001f));

            // 실제 대기열과 같은 순서: 기존 Current는 Exiting으로 빠지고, 기존 Standby가 Current가 된 뒤
            // 다음 슬롯이 나중에 다시 채워진다. 각 이벤트 시점마다 대상 집합을 새로 계산해야 한다.
            SetPrivate(queue, "currentSlot", null);
            SetPrivate(queue, "standbySlot", null);
            SetPrivate(queue, "exitingSlot", firstMonster);
            RaiseQueueEvent(queue, "ExitingStarted", firstMonster);
            Assert.That(ReadRendererFloat(firstRenderer, GrayscaleAmountId), Is.Zero.Within(0.0001f));
            Assert.That(ReadRendererFloat(secondRenderer, GrayscaleAmountId), Is.Zero.Within(0.0001f));

            SetPrivate(queue, "currentSlot", secondMonster);
            RaiseQueueEvent(queue, "CurrentPromoted", secondMonster);
            Assert.That(ReadRendererFloat(secondRenderer, GrayscaleAmountId), Is.EqualTo(1f).Within(0.0001f));

            SetPrivate(queue, "standbySlot", replacementMonster);
            RaiseQueueEvent(queue, "StandbyRefilled", replacementMonster);
            RaiseQueueEvent(queue, "StandbyRefilled", replacementMonster);
            Assert.That(ReadRendererFloat(replacementRenderer, GrayscaleAmountId), Is.EqualTo(1f).Within(0.0001f));
            Assert.AreEqual(2, ((System.Collections.ICollection)GetPrivate(controller, "grayscaleRendererStates")).Count,
                "같은 갱신 이벤트가 반복돼도 렌더러 상태가 누적되면 안 됩니다.");

            Invoke(controller, "OnDisable");
            subscribedController = null;

            Assert.That(ReadRendererFloat(firstRenderer, GrayscaleAmountId), Is.Zero.Within(0.0001f));
            Assert.That(ReadRendererFloat(secondRenderer, GrayscaleAmountId), Is.Zero.Within(0.0001f));
            Assert.That(ReadRendererFloat(replacementRenderer, GrayscaleAmountId), Is.Zero.Within(0.0001f));
        }

        [Test]
        public void RestLateUpdate_ReappliesGrayscaleAfterAnotherPresentationClearsRendererState()
        {
            CharacterDefinition active = Definition("CatKnight", 30);
            SaveData data = Inject(State(active.CharacterId, 0));
            CharacterRoster roster = ReadyRoster(data, active);
            SetPrivate(roster, "current", active);
            DungeonPartyRestEventController controller = Controller(roster, active, out _);
            MonsterEncounterQueue queue = MonsterQueue(
                out SpriteRenderer currentRenderer,
                out _,
                out _,
                out _);
            SetPrivate(controller, "monsterEncounterQueue", queue);

            Material actorMaterial = AssetDatabase.LoadAssetAtPath<Material>(ActorOutlineMaterialPath);
            Assert.IsNotNull(actorMaterial);
            currentRenderer.sharedMaterial = actorMaterial;
            Material defaultMaterial = new Material(Shader.Find("Sprites/Default"));
            created.Add(defaultMaterial);

            Invoke(controller, "StartRest");
            currentRenderer.sharedMaterial = defaultMaterial;
            currentRenderer.SetPropertyBlock(null);

            Invoke(controller, "LateUpdate");

            Assert.AreSame(actorMaterial, currentRenderer.sharedMaterial,
                "다른 표시 시스템이 Material을 바꿔도 기존 공용 셰이더를 재사용해야 합니다.");
            Assert.That(ReadRendererFloat(currentRenderer, GrayscaleAmountId), Is.EqualTo(1f).Within(0.0001f));

            Invoke(controller, "StopRest", false);
            Assert.AreSame(actorMaterial, currentRenderer.sharedMaterial);
            Assert.IsTrue(RendererPropertyBlockIsEmpty(currentRenderer));
        }

        [Test]
        public void SceneWiring_AssignsCampfirePrefabWithoutAuthoredEventInstance()
        {
            GameObject expectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CampfirePrefabPath);
            Assert.IsNotNull(expectedPrefab, CampfirePrefabPath);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                DungeonPartyRestEventController controller = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    controller = root.GetComponentInChildren<DungeonPartyRestEventController>(true);
                    if (controller != null) break;
                }

                Assert.IsNotNull(controller, "씬에서 DungeonPartyRestEventController를 찾지 못했습니다.");
                DungeonCombatRules combatRules = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    combatRules = root.GetComponent<DungeonCombatRules>();
                    if (combatRules != null) break;
                }
                Assert.IsNotNull(combatRules, "던전 전투 규칙 관리 오브젝트가 씬 루트에 있어야 합니다.");
                var rulesSerialized = new SerializedObject(combatRules);
                Assert.That(rulesSerialized.FindProperty("minimumStaminaRatio").floatValue,
                    Is.EqualTo(0.3f).Within(0.0001f));
                Assert.AreSame(expectedPrefab, GetPrivate(controller, "campfirePrefab"));
                MonsterEncounterQueue queue = (MonsterEncounterQueue)GetPrivate(controller, "monsterEncounterQueue");
                Assert.IsNotNull(queue, "휴식 컨트롤러의 Monster Encounter Queue 씬 참조가 비어 있습니다.");
                Assert.That((float)GetPrivate(controller, "grayscaleAmount"), Is.EqualTo(1f).Within(0.0001f));

                TargetCombatController firstMonster = (TargetCombatController)GetPrivate(queue, "firstSlot");
                TargetCombatController secondMonster = (TargetCombatController)GetPrivate(queue, "secondSlot");
                Assert.IsNotNull(firstMonster);
                Assert.IsNotNull(secondMonster);
                AssertRendererSupportsGrayscale(firstMonster.GetComponent<SpriteRenderer>());
                AssertRendererSupportsGrayscale(secondMonster.GetComponent<SpriteRenderer>());

                var restRoot = (GameObject)GetPrivate(controller, "restEventRoot");
                Assert.IsNotNull(restRoot);
                Assert.AreEqual(3, restRoot.transform.childCount,
                    "Rest Event Root에는 캐릭터 슬롯만 남고, 모닥불은 런타임에 생성되어야 합니다.");
                for (int i = 0; i < restRoot.transform.childCount; i++)
                {
                    GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(
                        restRoot.transform.GetChild(i).gameObject);
                    Assert.AreNotSame(expectedPrefab, source,
                        "씬에 모닥불 프리팹 인스턴스가 직접 배치되어 있으면 런타임 인스턴스와 중복됩니다.");
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private MonsterEncounterQueue MonsterQueue(
            out SpriteRenderer currentRenderer,
            out SpriteRenderer standbyRenderer,
            out TargetCombatController currentMonster,
            out TargetCombatController standbyMonster)
        {
            var queueHost = new GameObject("RestMonsterQueue");
            queueHost.SetActive(false);
            MonsterEncounterQueue queue = queueHost.AddComponent<MonsterEncounterQueue>();
            created.Add(queueHost);

            currentMonster = Monster("RestMonsterCurrent", out currentRenderer);
            standbyMonster = Monster("RestMonsterStandby", out standbyRenderer);
            SetPrivate(queue, "currentSlot", currentMonster);
            SetPrivate(queue, "standbySlot", standbyMonster);
            return queue;
        }

        private TargetCombatController Monster(string objectName, out SpriteRenderer renderer)
        {
            var host = new GameObject(objectName);
            host.SetActive(false);
            renderer = host.AddComponent<SpriteRenderer>();
            TargetCombatController controller = host.AddComponent<TargetCombatController>();
            created.Add(host);
            return controller;
        }

        private static void RaiseQueueEvent(
            MonsterEncounterQueue queue,
            string eventName,
            TargetCombatController monster)
        {
            FieldInfo eventField = typeof(MonsterEncounterQueue).GetField(
                eventName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(eventField, $"MonsterEncounterQueue.{eventName} 이벤트 저장 필드를 찾지 못했습니다.");
            var callback = eventField.GetValue(queue) as Action<TargetCombatController>;
            Assert.IsNotNull(callback, $"MonsterEncounterQueue.{eventName} 이벤트에 휴식 컨트롤러가 구독되지 않았습니다.");
            callback(monster);
        }

        private static float ReadRendererFloat(SpriteRenderer renderer, int propertyId)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetFloat(propertyId);
        }

        private static bool RendererPropertyBlockIsEmpty(SpriteRenderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.isEmpty;
        }

        private static void AssertRendererSupportsGrayscale(SpriteRenderer renderer)
        {
            Assert.IsNotNull(renderer);
            Assert.IsNotNull(renderer.sharedMaterial);
            Assert.IsNotNull(renderer.sharedMaterial.shader);
            Assert.IsTrue(renderer.sharedMaterial.HasProperty(GrayscaleAmountId),
                $"{renderer.name}의 공유 셰이더가 _GrayscaleAmount를 지원하지 않습니다.");
        }

        private DungeonPartyRestEventController Controller(
            CharacterRoster roster,
            CharacterDefinition playerDefinition,
            out SpriteRenderer[] restRenderers)
        {
            var fieldHost = new GameObject("RestEventFieldMode");
            fieldHost.SetActive(false);
            FieldModeManager fieldMode = fieldHost.AddComponent<FieldModeManager>();
            SetPrivate(fieldMode, "<CurrentMode>k__BackingField", FieldMode.Dungeon);
            created.Add(fieldHost);

            var playerHost = new GameObject("RestEventPlayer");
            playerHost.SetActive(false);
            SpriteRenderer playerRenderer = playerHost.AddComponent<SpriteRenderer>();
            PlayerCharacterAnimator animator = playerHost.AddComponent<PlayerCharacterAnimator>();
            created.Add(playerHost);

            var root = new GameObject("RestEventRoot");
            root.SetActive(false);
            created.Add(root);

            var slots = new Transform[3];
            restRenderers = new SpriteRenderer[3];
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = new GameObject($"RestSlot{i}");
                slots[i] = slot.transform;
                restRenderers[i] = slot.AddComponent<SpriteRenderer>();
                slot.transform.SetParent(root.transform, false);
                created.Add(slot);
            }

            var host = new GameObject("RestEventController");
            host.SetActive(false);
            var controller = host.AddComponent<DungeonPartyRestEventController>();
            created.Add(host);

            SetPrivate(controller, "fieldModeManager", fieldMode);
            SetPrivate(controller, "roster", roster);
            SetPrivate(controller, "playerAnimator", animator);
            SetPrivate(controller, "playerRenderer", playerRenderer);
            SetPrivate(controller, "restEventRoot", root);
            SetPrivate(controller, "characterSlots", slots);
            Invoke(controller, "BuildViews");

            Assert.AreSame(playerDefinition, roster.Current);
            return controller;
        }

        private CharacterRoster ReadyRoster(SaveData data, params CharacterDefinition[] definitions)
        {
            CharacterCatalog catalog = ScriptableObject.CreateInstance<CharacterCatalog>();
            created.Add(catalog);
            var serializedCatalog = new SerializedObject(catalog);
            SerializedProperty characters = serializedCatalog.FindProperty("characters");
            characters.arraySize = definitions.Length;
            for (int i = 0; i < definitions.Length; i++)
                characters.GetArrayElementAtIndex(i).objectReferenceValue = definitions[i];
            serializedCatalog.ApplyModifiedPropertiesWithoutUndo();
            catalog.MarkDirty();

            var host = new GameObject("RestEventRoster");
            host.SetActive(false);
            CharacterRoster roster = host.AddComponent<CharacterRoster>();
            created.Add(host);
            SetPrivate(roster, "catalog", catalog);
            SetPrivate(roster, "owned", new OwnedCharacterCollection(catalog, data));
            Invoke(roster, "BuildUsableEntries");
            return roster;
        }

        private CharacterDefinition Definition(string id, int maxStamina)
        {
            var texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            var profile = ScriptableObject.CreateInstance<CharacterMotionProfile>();
            var serializedProfile = new SerializedObject(profile);
            SerializedProperty frames = serializedProfile.FindProperty("baseIdle").FindPropertyRelative("frames");
            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = sprite;
            serializedProfile.ApplyModifiedPropertiesWithoutUndo();

            var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            var serializedDefinition = new SerializedObject(definition);
            serializedDefinition.FindProperty("characterId").stringValue = id;
            serializedDefinition.FindProperty("maxStamina").intValue = maxStamina;
            serializedDefinition.FindProperty("motionProfile").objectReferenceValue = profile;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();

            created.Add(texture);
            created.Add(sprite);
            created.Add(profile);
            created.Add(definition);
            return definition;
        }

        private static CharacterSaveState State(string id, int stamina)
        {
            return new CharacterSaveState { characterId = id, level = 1, currentStamina = stamina };
        }

        private static SaveData Inject(params CharacterSaveState[] states)
        {
            var data = new SaveData
            {
                characters = new List<CharacterSaveState>(states),
                partyCharacterIds = new List<string>(Array.ConvertAll(states, state => state.characterId)),
                recoverySlots = new List<RecoverySlotSaveState>(),
            };
            DataField.SetValue(null, data);
            LoadResultField.SetValue(null, SaveLoadResult.NewGame(data));
            return data;
        }

        private static string[] BufferedIds(DungeonPartyRestEventController controller)
        {
            var buffer = (List<CharacterDefinition>)GetPrivate(controller, "partyBuffer");
            return buffer.ConvertAll(definition => definition.CharacterId).ToArray();
        }

        private static object Invoke(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{target.GetType().Name}.{methodName}을 찾지 못했습니다.");
            return method.Invoke(target, arguments);
        }

        private static object GetPrivate(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName}을 찾지 못했습니다.");
            return field.GetValue(target);
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{fieldName}을 찾지 못했습니다.");
            field.SetValue(target, value);
        }
    }
}

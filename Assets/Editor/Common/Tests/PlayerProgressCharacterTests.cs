using System;
using System.Collections.Generic;
using System.Reflection;
using Character;
using Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CommonEditor.Tests
{
    /// <summary>
    /// <see cref="PlayerProgress"/>가 <b>계정 전역 레벨</b>이 아니라 <b>지금 전투 중인 캐릭터</b>를
    /// 성장시키는지 확인한다.
    ///
    /// <b>실제 저장 파일을 읽지도 쓰지도 않는다.</b> 저장 문서를 리플렉션으로 직접 끼워 넣고, 저장은
    /// 메모리 저장소로 받아 <b>몇 번 썼는지</b>만 센다 - persistentDataPath는 어디에서도 쓰이지 않는다.
    /// 씬도 만들지 않는다. 컴포넌트는 <b>비활성 호스트</b>에 붙여 Unity가 수명 주기를 대신 돌리지
    /// 못하게 하고, 시험이 부르고 싶은 단계(Awake/OnEnable/Start)만 골라 부른다 - 그래야 "Awake
    /// 순서가 달라도 같은 결과인가"를 시험이 직접 정할 수 있다.
    ///
    /// 확인하는 것은 하나로 모인다 - <b>경험치는 처치 시점에 실제로 투입돼 있던 보유 캐릭터에게만
    /// 가고, 줄 대상이 없으면 아무 항목도 생기지 않으며, 그래도 누적 킬카운트는 오른다.</b>
    /// </summary>
    public sealed class PlayerProgressCharacterTests
    {
        private static readonly string[] SixIds =
        {
            "CatKnight", "ElfArcher", "Barbarian", "ElfGuardian", "RabbitHealer", "CatMage",
        };

        private static readonly FieldInfo DataField =
            typeof(SaveSystem).GetField("data", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo LoadResultField =
            typeof(SaveSystem).GetField("loadResult", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo LoadedFromFileField =
            typeof(SaveSystem).GetField("loadedFromFile", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo ConfigureMethod =
            typeof(SaveSystem).GetMethod("ConfigureForTests", BindingFlags.NonPublic | BindingFlags.Static);

        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private readonly List<PlayerProgress> enabledProgress = new List<PlayerProgress>();

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(DataField, "SaveSystem.data를 찾지 못했습니다 - 그대로 두면 시험이 실제 저장 파일을 읽습니다.");
            Assert.IsNotNull(LoadResultField, "SaveSystem.loadResult를 찾지 못했습니다.");
            Assert.IsNotNull(LoadedFromFileField, "SaveSystem.loadedFromFile을 찾지 못했습니다.");
            Assert.IsNotNull(ConfigureMethod, "SaveSystem.ConfigureForTests를 찾지 못했습니다.");

            ClearStaticEvents();
            ResetStatics();
        }

        [TearDown]
        public void TearDown()
        {
            // 구독을 반드시 끊는다 - 남겨 두면 다음 시험의 처치 이벤트를 이 시험의 컴포넌트가 받는다.
            foreach (PlayerProgress progress in enabledProgress)
            {
                if (progress != null) Invoke(progress, "OnDisable");
            }
            enabledProgress.Clear();

            ClearStaticEvents();
            ResetStatics();

            // 실제 저장소/문서로 되돌린다 - 이 시험의 주입이 다음 시험으로 새면 안 된다.
            ConfigureMethod.Invoke(null, new object[] { null, null, null });

            foreach (UnityEngine.Object asset in created)
            {
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            }

            created.Clear();
            SetRosterInstance(null);
        }

        // ---- 보유한 현재 캐릭터에게만 간다 ----

        [Test]
        public void 처치하면_지금_전투_중인_캐릭터에게만_경험치가_쌓인다()
        {
            SaveData document = Inject(State("CatKnight"), State("ElfArcher"));
            MemoryStorage storage = UseMemoryStorage();
            ReadyRoster(current: "CatKnight");
            ReadyProgress();

            Defeat("Scarecrow");

            Assert.AreEqual(1, document.characters[0].currentExp, "투입된 캐릭터만 자란다.");
            Assert.AreEqual(0, document.characters[1].currentExp, "다른 캐릭터는 그대로여야 한다.");
            Assert.AreEqual(1, PlayerProgress.TotalKillCount);
            Assert.AreEqual(1, document.totalKillCount, "누적 킬은 문서에도 실린다.");
            Assert.AreEqual(1, storage.WriteCalls, "처치 하나당 이 컴포넌트의 저장은 한 번이다.");
            Assert.AreEqual(1, PlayerProgress.CurrentExp);
            Assert.AreEqual(1, PlayerProgress.CurrentLevel);
        }

        [Test]
        public void 투입된_캐릭터가_없어도_정상적인_처치는_누적_킬을_올린다()
        {
            SaveData document = Inject(State("CatKnight"));
            MemoryStorage storage = UseMemoryStorage();
            ReadyRoster(current: null);
            ReadyProgress();

            string before = Describe(document);
            Defeat("Scarecrow");

            Assert.AreEqual(1, PlayerProgress.TotalKillCount, "처치는 계정이 한 일이므로 누적 킬은 오른다.");
            Assert.AreEqual(1, document.totalKillCount);
            Assert.AreEqual(before, Describe(document), "줄 대상이 없으면 캐릭터 항목은 한 글자도 바뀌지 않는다.");
            Assert.AreEqual(1, storage.WriteCalls);
        }

        [Test]
        public void 로스터가_아예_없는_씬에서도_처치가_안전하게_지나간다()
        {
            SaveData document = Inject();
            MemoryStorage storage = UseMemoryStorage();
            SetRosterInstance(null);
            ReadyProgress();

            Defeat("Scarecrow");

            Assert.AreEqual(1, PlayerProgress.TotalKillCount);
            Assert.AreEqual(0, document.characters.Count, "캐릭터 항목이 생기면 안 된다.");
            Assert.AreEqual(1, PlayerProgress.CurrentLevel, "표시는 안전한 기본값이다.");
            Assert.AreEqual(0, PlayerProgress.CurrentExp);
            Assert.AreEqual(1, storage.WriteCalls);
        }

        [Test]
        public void 보유가_사라진_캐릭터에게는_주지_않고_항목도_되살리지_않는다()
        {
            SaveData document = Inject(State("CatKnight"));
            MemoryStorage storage = UseMemoryStorage();
            ReadyRoster(current: "CatKnight");
            ReadyProgress();

            // Awake 뒤에 보유가 사라졌다 - 목록(usableEntries)에는 그대로 남아 있는 상태다.
            document.characters.Clear();

            Defeat("Scarecrow");

            Assert.AreEqual(0, document.characters.Count, "잃은 캐릭터에게 주려다 항목을 되살리면 안 된다.");
            Assert.AreEqual(1, PlayerProgress.TotalKillCount);
            Assert.AreEqual(1, storage.WriteCalls);
        }

        [Test]
        public void 카탈로그에_없는_저장_전용_id가_투입돼_있어도_주지_않는다()
        {
            SaveData document = Inject(State("CatKnight"), State("GhostHero", level: 9, exp: 4));
            UseMemoryStorage();
            CharacterRoster roster = ReadyRoster(current: null);

            // 예전 빌드에서 남은 id를 가리키는 정의가 어떤 경로로든 current에 들어간 상황이다.
            SetPrivate(roster, "current", Definition("GhostHero"));
            ReadyProgress();

            Defeat("Scarecrow");

            Assert.AreEqual(9, document.characters[1].level, "감춰 둔 값은 한 글자도 바뀌지 않아야 한다.");
            Assert.AreEqual(4, document.characters[1].currentExp);
            Assert.AreEqual(1, PlayerProgress.TotalKillCount);
        }

        [Test]
        public void 카탈로그가_없는_과도기_구성에서는_캐릭터_경험치가_없다()
        {
            SaveData document = Inject();
            MemoryStorage storage = UseMemoryStorage();

            CharacterDefinition catKnight = Definition("CatKnight");
            CharacterRoster roster = LegacyRoster(catKnight);
            SetPrivate(roster, "current", catKnight);
            SetRosterInstance(roster);
            ReadyProgress();

            Defeat("Scarecrow");

            Assert.AreEqual(0, document.characters.Count,
                "보유라는 개념이 없는 구성에서 항목을 만들면 그것이 곧 캐릭터 지급이다.");
            Assert.AreEqual(1, PlayerProgress.TotalKillCount, "그래도 누적 킬은 오른다.");
            Assert.AreEqual(1, storage.WriteCalls);
        }

        // ---- 걸러야 하는 이벤트 ----

        [Test]
        public void 같은_프레임의_같은_id는_킬도_경험치도_한_번만_올린다()
        {
            SaveData document = Inject(State("CatKnight"));
            MemoryStorage storage = UseMemoryStorage();
            ReadyRoster(current: "CatKnight");
            ReadyProgress();

            Defeat("Scarecrow");
            LogAssert.ignoreFailingMessages = true;
            Defeat("Scarecrow");
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(1, PlayerProgress.TotalKillCount, "중복은 킬카운트도 올리지 않는다.");
            Assert.AreEqual(1, document.characters[0].currentExp, "중복은 경험치도 올리지 않는다.");
            Assert.AreEqual(1, storage.WriteCalls, "무시한 이벤트는 저장하지도 않는다.");
        }

        [Test]
        public void 같은_프레임이라도_다른_몬스터는_각각_처리된다()
        {
            SaveData document = Inject(State("CatKnight"));
            UseMemoryStorage();
            ReadyRoster(current: "CatKnight");
            ReadyProgress();

            Defeat("Scarecrow_A");
            Defeat("Scarecrow_B");

            Assert.AreEqual(2, PlayerProgress.TotalKillCount);
            Assert.AreEqual(2, document.characters[0].currentExp);
        }

        [Test]
        public void 빈_targetId는_킬도_경험치도_올리지_않는다()
        {
            SaveData document = Inject(State("CatKnight"));
            MemoryStorage storage = UseMemoryStorage();
            ReadyRoster(current: "CatKnight");
            ReadyProgress();

            LogAssert.ignoreFailingMessages = true;
            Defeat(null);
            Defeat(string.Empty);
            LogAssert.ignoreFailingMessages = false;

            Assert.AreEqual(0, PlayerProgress.TotalKillCount, "가릴 수 없는 이벤트는 아무것도 올리지 않는다.");
            Assert.AreEqual(0, document.characters[0].currentExp);
            Assert.AreEqual(0, storage.WriteCalls);

            // 걸러진 이벤트가 중복 필터의 상태를 오염시키지도 않는다.
            Defeat("Scarecrow");
            Assert.AreEqual(1, PlayerProgress.TotalKillCount, "정상적인 처치는 그 뒤에도 그대로 처리된다.");
        }

        // ---- 행동력과 서로 간섭하지 않는다 ----

        [Test]
        public void 행동력이_0이_되는_마지막_처치도_경험치를_받는다_행동력이_먼저_처리돼도()
        {
            SaveData document = Inject(State("CatKnight", stamina: 1));
            UseMemoryStorage();
            CharacterRoster roster = ReadyRoster(current: "CatKnight");
            ReadyProgress(subscribeFirst: false, roster: roster);

            Defeat("Scarecrow");

            Assert.AreEqual(0, document.characters[0].currentStamina, "전제 확인 - 마지막 한 방이었다.");
            Assert.AreEqual(1, document.characters[0].currentExp,
                "행동력이 먼저 0이 됐다고 보상이 사라지면 안 된다.");
        }

        [Test]
        public void 행동력이_0이_되는_마지막_처치도_경험치를_받는다_경험치가_먼저_처리돼도()
        {
            SaveData document = Inject(State("CatKnight", stamina: 1));
            UseMemoryStorage();
            CharacterRoster roster = ReadyRoster(current: "CatKnight");
            ReadyProgress(subscribeFirst: true, roster: roster);

            Defeat("Scarecrow");

            Assert.AreEqual(0, document.characters[0].currentStamina);
            Assert.AreEqual(1, document.characters[0].currentExp,
                "구독 순서가 결과를 바꾸면 안 된다.");
        }

        [Test]
        public void 경험치_저장과_행동력_저장은_한_덩어리로_묶지_않는다()
        {
            Inject(State("CatKnight", stamina: 5));
            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = ReadyRoster(current: "CatKnight");
            ReadyProgress(subscribeFirst: false, roster: roster);

            Defeat("Scarecrow");

            Assert.AreEqual(2, storage.WriteCalls,
                "행동력(로스터)과 성장(PlayerProgress)은 서로 다른 주인이 각자 한 번씩 저장한다 - " +
                "둘을 한 거래로 합치면 한쪽의 실패가 다른 쪽을 되돌린다.");
        }

        // ---- 여러 레벨이 올라도 저장은 한 번 ----

        [Test]
        public void 한_번의_처치로_여러_레벨이_올라도_저장은_한_번이다()
        {
            SaveData document = Inject(State("CatKnight"));
            MemoryStorage storage = UseMemoryStorage();
            ReadyRoster(current: "CatKnight");
            ReadyProgress(expPerDefeat: 35);

            var gained = new List<int>();
            var levels = new List<int>();
            int changedCount = 0;

            Action<int> onGained = gained.Add;
            Action<int> onLevelUp = levels.Add;
            Action onChanged = () => changedCount++;

            PlayerProgress.OnExpGained += onGained;
            PlayerProgress.OnLevelUp += onLevelUp;
            PlayerProgress.OnExperienceChanged += onChanged;

            try
            {
                Defeat("Scarecrow");
            }
            finally
            {
                PlayerProgress.OnExpGained -= onGained;
                PlayerProgress.OnLevelUp -= onLevelUp;
                PlayerProgress.OnExperienceChanged -= onChanged;
            }

            Assert.AreEqual(4, document.characters[0].level, "10짜리 레벨을 35로 세 번 넘긴다.");
            Assert.AreEqual(5, document.characters[0].currentExp, "남은 경험치는 이월된다.");
            Assert.AreEqual(1, storage.WriteCalls, "레벨이 세 번 올라도 파일 쓰기는 한 번이다.");

            CollectionAssert.AreEqual(new[] { 35 }, gained, "지급 신호는 한 번, 실제로 받아들인 양이다.");
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, levels, "레벨업은 오른 횟수만큼, 새 레벨 값으로.");
            Assert.AreEqual(1, changedCount, "성장 하나에 값 변경 신호는 한 번이다.");
        }

        // ---- 예전 전역 값은 손대지 않는다 ----

        [Test]
        public void 예전_전역_레벨과_경험치는_한_글자도_바뀌지_않는다()
        {
            SaveData document = Inject(State("CatKnight"));
            document.currentLevel = 7;
            document.currentExp = 240;
            document.totalKillCount = 133;
            SetLoaded(document);

            UseMemoryStorage();
            ReadyRoster(current: "CatKnight");
            PlayerProgress progress = ReadyProgress();

            Defeat("Scarecrow");
            Invoke(progress, "OnApplicationQuit");

            Assert.AreEqual(7, document.currentLevel, "예전 전역 레벨은 보존한다 - 읽지도 쓰지도 않는다.");
            Assert.AreEqual(240, document.currentExp, "예전 전역 경험치도 그대로다.");
            Assert.AreEqual(134, document.totalKillCount, "누적 킬만 계정 값으로 남는다.");
            Assert.AreEqual(1, document.characters[0].currentExp, "성장은 캐릭터 항목에만 쌓인다.");
        }

        [Test]
        public void 표시값은_예전_전역_레벨이_아니라_캐릭터의_레벨이다()
        {
            SaveData document = Inject(State("CatKnight", level: 3, exp: 4));
            document.currentLevel = 77;
            document.currentExp = 999;
            SetLoaded(document);

            ReadyRoster(current: "CatKnight");
            ReadyProgress();

            Assert.AreEqual(3, PlayerProgress.CurrentLevel, "화면에 나오는 것은 캐릭터의 레벨이다.");
            Assert.AreEqual(4, PlayerProgress.CurrentExp);
        }

        // ---- 캐릭터마다 따로 쌓인다 ----

        [Test]
        public void 캐릭터마다_경험치가_서로_섞이지_않고_따로_쌓인다()
        {
            SaveData document = Inject(State("CatKnight"), State("ElfArcher", level: 5, exp: 2));
            UseMemoryStorage();
            CharacterRoster roster = ReadyRoster(current: "CatKnight");
            ReadyProgress();

            Defeat("Scarecrow_A");
            SwitchCurrentTo(roster, "ElfArcher");
            Defeat("Scarecrow_B");

            Assert.AreEqual(1, document.characters[0].currentExp, "고양이 기사가 받은 것은 하나뿐이다.");
            Assert.AreEqual(1, document.characters[0].level);
            Assert.AreEqual(3, document.characters[1].currentExp, "궁수는 자기 값에서 이어 받는다.");
            Assert.AreEqual(5, document.characters[1].level);
        }

        [Test]
        public void 교체_뒤의_처치는_새_캐릭터에게_간다()
        {
            SaveData document = Inject(State("CatKnight"), State("ElfArcher"));
            UseMemoryStorage();
            CharacterRoster roster = ReadyRoster(current: "CatKnight");
            ReadyProgress();

            SwitchCurrentTo(roster, "ElfArcher");
            Defeat("Scarecrow");

            Assert.AreEqual(0, document.characters[0].currentExp, "교체 전 캐릭터는 더 받지 않는다.");
            Assert.AreEqual(1, document.characters[1].currentExp);
            Assert.AreEqual(1, PlayerProgress.CurrentExp, "표시도 새 캐릭터를 따른다.");
        }

        // ---- 교체는 획득이 아니다 ----

        [Test]
        public void 교체는_획득_신호_없이_즉시_동기화_신호만_보낸다()
        {
            Inject(State("CatKnight", level: 3, exp: 4), State("ElfArcher", level: 12, exp: 7));
            CharacterRoster roster = ReadyRoster(current: "CatKnight");
            ReadyProgress();

            int syncCount = 0;
            int changedCount = 0;
            var gained = new List<int>();
            var levels = new List<int>();

            Action onSync = () => syncCount++;
            Action onChanged = () => changedCount++;
            Action<int> onGained = gained.Add;
            Action<int> onLevelUp = levels.Add;

            PlayerProgress.OnCurrentCharacterSynchronized += onSync;
            PlayerProgress.OnExperienceChanged += onChanged;
            PlayerProgress.OnExpGained += onGained;
            PlayerProgress.OnLevelUp += onLevelUp;

            try
            {
                SwitchCurrentTo(roster, "ElfArcher");
            }
            finally
            {
                PlayerProgress.OnCurrentCharacterSynchronized -= onSync;
                PlayerProgress.OnExperienceChanged -= onChanged;
                PlayerProgress.OnExpGained -= onGained;
                PlayerProgress.OnLevelUp -= onLevelUp;
            }

            Assert.AreEqual(1, syncCount, "교체는 즉시 동기화 신호 하나로 알린다.");
            Assert.AreEqual(0, changedCount, "교체는 값이 자란 것이 아니다.");
            CollectionAssert.IsEmpty(gained, "교체로 경험치를 얻은 것이 아니다.");
            CollectionAssert.IsEmpty(levels,
                "레벨 3에서 12로 갈아탄 것을 레벨업으로 알리면 연출이 아홉 번 쏟아진다.");

            Assert.AreEqual(12, PlayerProgress.CurrentLevel, "그래도 값은 그 자리에서 새 캐릭터의 것이 된다.");
            Assert.AreEqual(7, PlayerProgress.CurrentExp);
        }

        [Test]
        public void 표시_쪽은_교체_신호를_즉시_동기화_경로로_받는다()
        {
            // 표시가 이 신호를 <b>획득 경로</b>로 받으면 진행 중이던 연출이 취소되지 않는다.
            // 구독 대상이 실제로 즉시 동기화 메서드인지 이름으로 못 박는다.
            var host = new GameObject("DisplayTestHost");
            created.Add(host);
            host.SetActive(false);

            PlayerProgressDisplay display = host.AddComponent<PlayerProgressDisplay>();
            MethodInfo sync = typeof(PlayerProgressDisplay).GetMethod(
                "SyncImmediately", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(sync, "PlayerProgressDisplay.SyncImmediately를 찾지 못했습니다.");

            Delegate before = GetStaticEvent(typeof(PlayerProgress), "OnCurrentCharacterSynchronized");
            Assert.IsNull(before, "전제 확인 - 아직 아무도 구독하지 않았다.");

            Invoke(display, "OnEnable");
            try
            {
                Delegate after = GetStaticEvent(typeof(PlayerProgress), "OnCurrentCharacterSynchronized");
                Assert.IsNotNull(after, "표시가 교체 신호를 구독해야 한다.");

                var targets = new List<string>();
                foreach (Delegate handler in after.GetInvocationList()) targets.Add(handler.Method.Name);

                CollectionAssert.Contains(targets, "SyncImmediately",
                    "교체는 연출을 취소하고 즉시 맞추는 경로로 받아야 한다.");
                CollectionAssert.DoesNotContain(targets, "Refresh",
                    "획득 경로로 받으면 레벨 차이만큼 레벨업 연출이 재생된다.");
            }
            finally
            {
                Invoke(display, "OnDisable");
            }

            Assert.IsNull(GetStaticEvent(typeof(PlayerProgress), "OnCurrentCharacterSynchronized"),
                "OnDisable에서 구독을 끊어야 한다.");
        }

        // ---- Awake 순서에 기대지 않는다 ----

        [Test]
        public void 로스터보다_먼저_깨어나_교체_신호를_놓쳐도_Start가_현재_캐릭터를_맞춘다()
        {
            Inject(State("CatKnight", level: 6, exp: 3));

            // PlayerProgress가 먼저 깨어난다 - 아직 로스터도 투입된 캐릭터도 없다.
            PlayerProgress progress = Progress();
            Invoke(progress, "Awake");
            Assert.AreEqual(1, PlayerProgress.CurrentLevel, "전제 확인 - 아직 볼 캐릭터가 없다.");

            Invoke(progress, "OnEnable");
            enabledProgress.Add(progress);

            // 로스터가 그 뒤에 깨어나 시작 캐릭터를 투입한다. 이 시험은 <b>신호를 일부러 보내지
            // 않는다</b> - 실제로 순서가 반대면 구독 전에 발생해 아무도 듣지 못하기 때문이다.
            ReadyRoster(current: "CatKnight");

            int syncCount = 0;
            Action onSync = () => syncCount++;
            PlayerProgress.OnCurrentCharacterSynchronized += onSync;

            try
            {
                Invoke(progress, "Start");
            }
            finally
            {
                PlayerProgress.OnCurrentCharacterSynchronized -= onSync;
            }

            Assert.AreEqual(6, PlayerProgress.CurrentLevel, "놓친 신호를 Start가 되찾아야 한다.");
            Assert.AreEqual(3, PlayerProgress.CurrentExp);
            Assert.AreEqual(1, syncCount, "되찾은 값도 즉시 동기화로 알린다.");
        }

        [Test]
        public void 로스터보다_나중에_깨어나면_Awake_그_자리에서_이미_맞는다()
        {
            Inject(State("CatKnight", level: 6, exp: 3));
            ReadyRoster(current: "CatKnight");

            PlayerProgress progress = Progress();
            Invoke(progress, "Awake");

            Assert.AreEqual(6, PlayerProgress.CurrentLevel, "먼저 준비된 캐릭터는 Awake에서 바로 보인다.");
            Assert.AreEqual(3, PlayerProgress.CurrentExp);
            Assert.IsTrue(PlayerProgress.IsInitialized);

            Invoke(progress, "OnEnable");
            enabledProgress.Add(progress);
            Invoke(progress, "Start");

            Assert.AreEqual(6, PlayerProgress.CurrentLevel, "Start가 다시 맞춰도 결과는 같다.");
            Assert.AreEqual(3, PlayerProgress.CurrentExp);
        }

        // ---- AddExp의 계약 ----

        [Test]
        public void AddExp는_현재_보유_캐릭터에게_적용되고_바뀔_때만_저장한다()
        {
            SaveData document = Inject(State("CatKnight"));
            MemoryStorage storage = UseMemoryStorage();
            ReadyRoster(current: "CatKnight");
            PlayerProgress progress = ReadyProgress();

            progress.AddExp(3);

            Assert.AreEqual(3, document.characters[0].currentExp);
            Assert.AreEqual(1, storage.WriteCalls);
            Assert.AreEqual(0, PlayerProgress.TotalKillCount, "AddExp는 킬카운트와 무관하다.");
        }

        [Test]
        public void AddExp는_0_이하와_줄_캐릭터가_없을_때_아무_일도_하지_않는다()
        {
            SaveData document = Inject(State("CatKnight"));
            MemoryStorage storage = UseMemoryStorage();
            CharacterRoster roster = ReadyRoster(current: "CatKnight");
            PlayerProgress progress = ReadyProgress();

            var gained = new List<int>();
            Action<int> onGained = gained.Add;
            PlayerProgress.OnExpGained += onGained;

            try
            {
                progress.AddExp(0);
                progress.AddExp(-5);

                SetPrivate(roster, "current", null);
                progress.AddExp(7);
            }
            finally
            {
                PlayerProgress.OnExpGained -= onGained;
            }

            Assert.AreEqual(0, document.characters[0].currentExp);
            Assert.AreEqual(0, storage.WriteCalls, "달라진 것이 없으면 파일을 쓰지 않는다.");
            CollectionAssert.IsEmpty(gained, "달라진 것이 없으면 알리지도 않는다.");
        }

        // ---- 표시값의 뜻 ----

        [Test]
        public void ExpToNextLevel은_남은_양이_아니라_필요_총량이다()
        {
            Inject(State("CatKnight", level: 2, exp: 7));
            ReadyRoster(current: "CatKnight");
            ReadyProgress();

            Assert.AreEqual(10, PlayerProgress.ExpToNextLevel,
                "EXP 바의 분모다 - 남은 3을 넣으면 7/3이 되어 비율이 1을 넘는다.");
            Assert.AreEqual(7, PlayerProgress.CurrentExp);
        }

        [Test]
        public void 어긋난_저장값은_보이는_자리에서만_안전해지고_문서는_그대로다()
        {
            SaveData document = Inject(State("CatKnight", level: 0, exp: -3));
            ReadyRoster(current: "CatKnight");
            ReadyProgress();

            Assert.AreEqual(1, PlayerProgress.CurrentLevel, "표시는 하한 아래로 내려가지 않는다.");
            Assert.AreEqual(0, PlayerProgress.CurrentExp, "음수 경험치를 그대로 그리지 않는다.");

            Assert.AreEqual(0, document.characters[0].level, "조회가 저장 항목을 고치면 안 된다.");
            Assert.AreEqual(-3, document.characters[0].currentExp);
        }

        // ---- 성장은 로스터의 알림 경로를 지난다 ----

        [Test]
        public void 성장하면_로스터의_상태_변경이_정식_정의로_한_번_나간다()
        {
            Inject(State("CatKnight"));
            UseMemoryStorage();
            CharacterRoster roster = ReadyRoster(current: "CatKnight");
            PlayerProgress progress = ReadyProgress();

            CharacterDefinition canonical = roster.Entries[0].definition;

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            try
            {
                progress.AddExp(3);
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }

            Assert.AreEqual(1, seen.Count, "교체 패널·회복소의 레벨 표시가 이 신호로 갱신된다.");
            Assert.AreSame(canonical, seen[0], "알림은 언제나 목록에 있는 그 객체여야 한다.");
        }

        [Test]
        public void 줄_캐릭터가_없으면_상태_변경도_나가지_않는다()
        {
            Inject(State("CatKnight"));
            UseMemoryStorage();
            ReadyRoster(current: null);
            PlayerProgress progress = ReadyProgress();

            var seen = new List<CharacterDefinition>();
            Action<CharacterDefinition> handler = seen.Add;
            CharacterRoster.CharacterStateChanged += handler;

            try
            {
                progress.AddExp(3);
                Defeat("Scarecrow");
            }
            finally
            {
                CharacterRoster.CharacterStateChanged -= handler;
            }

            CollectionAssert.IsEmpty(seen, "바뀐 캐릭터가 없으면 알릴 것도 없다.");
        }

        // ---- 도우미 ----

        /// <summary>저장 문서를 직접 끼워 넣는다 - 저장소가 아예 불리지 않으므로 실제 파일을 읽지 않는다.</summary>
        private static SaveData Inject(params CharacterSaveState[] states)
        {
            var document = new SaveData { characters = new List<CharacterSaveState>(states) };
            DataField.SetValue(null, document);
            LoadResultField.SetValue(null, SaveLoadResult.NewGame(document));
            LoadedFromFileField.SetValue(null, false);
            return document;
        }

        /// <summary>"저장 파일에서 그대로 읽어 온" 상태로 바꾼다 - 그래야 Awake가 Inspector 시작값으로
        /// 문서를 덮어쓰지 않는다.</summary>
        private static void SetLoaded(SaveData document)
        {
            LoadResultField.SetValue(null, SaveLoadResult.Loaded(document, SaveData.CurrentSaveVersion));
            LoadedFromFileField.SetValue(null, true);
        }

        /// <summary>저장을 <b>메모리에만</b> 받아 두는 저장소를 끼운다. 파일도 폴더도 만들지 않으므로
        /// persistentDataPath는 어디에서도 쓰이지 않는다 - 그러면서 "정말로 저장했는가"를 셀 수 있다.</summary>
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

        // ---- 처치 이벤트 ----

        /// <summary>Target.AnyTargetDefeated를 <b>실제 이벤트 그대로</b> 발생시킨다 - 구독자의 순서가
        /// 결과에 영향을 주는지 시험하려면 구독 목록을 그대로 지나가야 한다.</summary>
        private static void Defeat(string targetId)
        {
            var handler = (Action<string>)GetStaticEvent(typeof(Target), "AnyTargetDefeated");
            handler?.Invoke(targetId);
        }

        private static Delegate GetStaticEvent(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"{type.Name}.{name}의 뒷단 필드를 찾지 못했습니다.");
            return (Delegate)field.GetValue(null);
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
        }

        private static void ClearStaticEvent(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, $"{type.Name}.{name}의 뒷단 필드를 찾지 못했습니다.");
            field.SetValue(null, null);
        }

        private static void ResetStatics()
        {
            SetStaticProperty("IsInitialized", false);
            SetStaticProperty("TotalKillCount", 0);
            SetStaticProperty("CurrentLevel", CharacterProgressionService.MinimumLevel);
            SetStaticProperty("CurrentExp", 0);
            SetStaticProperty("ExpToNextLevel", CharacterProgressionService.DefaultExperiencePerLevel);
        }

        private static void SetStaticProperty(string name, object value)
        {
            PropertyInfo property = typeof(PlayerProgress).GetProperty(
                name, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(property, $"PlayerProgress.{name}을 찾지 못했습니다.");
            property.GetSetMethod(nonPublic: true).Invoke(null, new[] { value });
        }

        // ---- 컴포넌트 만들기 ----

        /// <summary>PlayerProgress 컴포넌트. <b>호스트를 비활성으로 먼저 만든 뒤</b> 붙인다 - 활성
        /// GameObject에 붙이면 Unity가 Awake/OnEnable을 그 자리에서 불러, 이 시험이 순서를 정할 수
        /// 없게 된다.</summary>
        private PlayerProgress Progress(int expPerDefeat = 1, int expToNextLevel = 10)
        {
            var host = new GameObject("PlayerProgressTestHost");
            created.Add(host);
            host.SetActive(false);

            PlayerProgress progress = host.AddComponent<PlayerProgress>();
            SetPrivate(progress, "expPerTargetDefeat", expPerDefeat);
            SetPrivate(progress, "expToNextLevel", expToNextLevel);
            return progress;
        }

        /// <summary>Awake -> OnEnable -> Start를 순서대로 부른다(로스터가 이미 준비된 흔한 경우).</summary>
        private PlayerProgress ReadyProgress(int expPerDefeat = 1)
        {
            PlayerProgress progress = Progress(expPerDefeat);
            Invoke(progress, "Awake");
            Invoke(progress, "OnEnable");
            enabledProgress.Add(progress);
            Invoke(progress, "Start");
            return progress;
        }

        /// <summary>구독 순서를 시험이 정한다. <paramref name="subscribeFirst"/>가 true면 경험치 쪽이
        /// 행동력 쪽보다 먼저 처치 이벤트를 받는다.</summary>
        private PlayerProgress ReadyProgress(bool subscribeFirst, CharacterRoster roster, int expPerDefeat = 1)
        {
            // 로스터가 먼저 붙어 있던 구독을 걷어 내고, 원하는 순서대로 다시 붙인다.
            ClearStaticEvent(typeof(Target), "AnyTargetDefeated");

            PlayerProgress progress = Progress(expPerDefeat);
            Invoke(progress, "Awake");

            if (subscribeFirst)
            {
                Invoke(progress, "OnEnable");
                Invoke(roster, "OnEnable");
            }
            else
            {
                Invoke(roster, "OnEnable");
                Invoke(progress, "OnEnable");
            }

            enabledProgress.Add(progress);
            Invoke(progress, "Start");
            return progress;
        }

        /// <summary>
        /// Awake가 하는 일 중 <b>목록 구성/행동력 정규화/시작 캐릭터 투입</b>만 재현한 로스터.
        /// 호스트는 계속 비활성이므로 Unity의 수명 주기는 돌지 않는다.
        /// </summary>
        private CharacterRoster ReadyRoster(string current)
        {
            var host = new GameObject("RosterTestHost");
            created.Add(host);
            host.SetActive(false);

            CharacterRoster roster = host.AddComponent<CharacterRoster>();
            SetPrivate(roster, "catalog", Catalog(SixIds));
            SetPrivate(roster, "owned", new OwnedCharacterCollection(
                (CharacterCatalog)GetPrivate(roster, "catalog"), SaveSystem.Data));

            Invoke(roster, "BuildUsableEntries");
            Invoke(roster, "NormalizeOwnedStamina");

            if (current != null) SetPrivate(roster, "current", FindEntry(roster, current));

            SetRosterInstance(roster);
            return roster;
        }

        /// <summary>카탈로그가 없는 과도기 구성의 로스터.</summary>
        private CharacterRoster LegacyRoster(params CharacterDefinition[] definitions)
        {
            var host = new GameObject("LegacyRosterTestHost");
            created.Add(host);
            host.SetActive(false);

            CharacterRoster roster = host.AddComponent<CharacterRoster>();

            var entries = new List<CharacterRoster.Entry>();
            foreach (CharacterDefinition definition in definitions)
            {
                entries.Add(new CharacterRoster.Entry { definition = definition });
            }

            SetPrivate(roster, "entries", entries);
            Invoke(roster, "BuildUsableEntries");
            return roster;
        }

        /// <summary>교체를 흉내 낸다 - 실제 교체가 하는 일 중 이 시험에 필요한 두 가지(current를 옮기고
        /// 그 사실을 알리는 것)만 재현한다. 런타임 액터가 없으므로 TrySwitchTo는 쓸 수 없다.</summary>
        private static void SwitchCurrentTo(CharacterRoster roster, string characterId)
        {
            CharacterDefinition next = FindEntry(roster, characterId);
            Assert.IsNotNull(next, $"'{characterId}'가 로스터 목록에 없습니다.");

            SetPrivate(roster, "current", next);
            RaiseCurrentCharacterChanged(next);
        }

        private static void RaiseCurrentCharacterChanged(CharacterDefinition definition)
        {
            var handler = (Action<CharacterDefinition>)GetStaticEvent(
                typeof(CharacterRoster), "CurrentCharacterChanged");
            handler?.Invoke(definition);
        }

        private static CharacterDefinition FindEntry(CharacterRoster roster, string characterId)
        {
            foreach (CharacterRoster.Entry entry in roster.Entries)
            {
                if (entry.definition != null && entry.definition.CharacterId == characterId) return entry.definition;
            }
            return null;
        }

        private static void SetRosterInstance(CharacterRoster roster)
        {
            typeof(CharacterRoster)
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                .GetSetMethod(nonPublic: true)
                .Invoke(null, new object[] { roster });
        }

        // ---- 리플렉션 ----

        private static object Invoke(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(
                name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method,
                $"{target.GetType().Name}.{name}을 찾지 못했습니다 - 이름이 바뀌었다면 시험도 함께 고치세요.");
            return method.Invoke(target, null);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{field}를 찾지 못했습니다.");
            info.SetValue(target, value);
        }

        private static object GetPrivate(object target, string field)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"{target.GetType().Name}.{field}를 찾지 못했습니다.");
            return info.GetValue(target);
        }

        // ---- 에셋 ----

        private CharacterCatalog Catalog(params string[] ids)
        {
            var definitions = new CharacterDefinition[ids.Length];
            for (int i = 0; i < ids.Length; i++) definitions[i] = Definition(ids[i]);

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

        private CharacterDefinition Definition(string id, int maxStamina = 30)
        {
            var definition = ScriptableObject.CreateInstance<CharacterDefinition>();
            created.Add(definition);

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("characterId").stringValue = id;
            serialized.FindProperty("initiallyOwned").boolValue = true;
            serialized.FindProperty("maxStamina").intValue = maxStamina;
            serialized.FindProperty("motionProfile").objectReferenceValue = Profile();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        private CharacterMotionProfile Profile()
        {
            var profile = ScriptableObject.CreateInstance<CharacterMotionProfile>();
            created.Add(profile);

            var texture = new Texture2D(4, 4);
            created.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            created.Add(sprite);

            var serialized = new SerializedObject(profile);
            SerializedProperty frames = serialized.FindProperty("baseIdle").FindPropertyRelative("frames");
            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private static CharacterSaveState State(string id, int level = 1, int exp = 0, int stamina = 10)
        {
            return new CharacterSaveState
            {
                characterId = id,
                level = level,
                currentExp = exp,
                currentStamina = stamina,
            };
        }

        private static string Describe(SaveData document)
        {
            if (document.characters == null) return "(null)";

            var parts = new List<string>();
            foreach (CharacterSaveState state in document.characters)
            {
                parts.Add(state == null
                    ? "(null)"
                    : $"{state.characterId}:{state.level}:{state.currentExp}:{state.currentStamina}");
            }

            return string.Join("|", parts);
        }
    }
}

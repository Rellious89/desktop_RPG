using System;
using Character;
using Common;
using NUnit.Framework;

namespace CharacterEditor.Tests
{
    /// <summary>
    /// 경험치·레벨 계산(<see cref="CharacterProgressionService"/>) 시험.
    ///
    /// <b>디스크도 씬도 저장소도 지나가지 않는다.</b> 계산이 넘겨받은 저장 항목 하나만 고치므로
    /// persistentDataPath도 <see cref="SaveSystem"/>도 필요 없다 - 규칙을 순수하게 떼어 둔 이유가
    /// 바로 이것이다.
    ///
    /// 확인하는 계약은 다섯이다.
    /// 1. 레벨 하나에 필요한 경험치는 <b>고정</b>이고 하한이 있다.
    /// 2. 0 이하의 요청은 <b>아무 일도 하지 않는다</b>(정규화조차 하지 않는다).
    /// 3. 남는 경험치는 이월되고 한 번에 여러 레벨이 오를 수 있다.
    /// 4. 어긋난 값은 <b>명시적 정규화</b>나 <b>양수 적용</b>에서만 고쳐진다.
    /// 5. 아주 큰 값에도 음수로 돌아 나가거나 멈추지 않는다.
    /// </summary>
    public sealed class CharacterProgressionServiceTests
    {
        private static CharacterProgressionService Service(int perLevel = 10)
        {
            return new CharacterProgressionService(perLevel);
        }

        private static CharacterSaveState State(string id = "CatKnight", int level = 1, int exp = 0, int stamina = 30)
        {
            return new CharacterSaveState
            {
                characterId = id,
                level = level,
                currentExp = exp,
                currentStamina = stamina,
            };
        }

        // ---- 필요 경험치 ----

        [Test]
        public void 레벨당_필요_경험치는_기본이_10이다()
        {
            Assert.AreEqual(10, CharacterProgressionService.DefaultExperiencePerLevel);
            Assert.AreEqual(10, new CharacterProgressionService().ExperiencePerLevel);
        }

        [Test]
        public void 필요_경험치는_1보다_작아질_수_없다()
        {
            // 0이나 음수를 그대로 받으면 나눗셈이 성립하지 않거나 경험치 1로 레벨이 끝없이 오른다.
            Assert.AreEqual(1, Service(0).ExperiencePerLevel);
            Assert.AreEqual(1, Service(-5).ExperiencePerLevel);
            Assert.AreEqual(1, Service(int.MinValue).ExperiencePerLevel);
            Assert.AreEqual(1, CharacterProgressionService.MinimumExperiencePerLevel);
        }

        [Test]
        public void 필요_경험치는_레벨이_올라도_그대로다()
        {
            CharacterProgressionService service = Service();
            CharacterSaveState state = State(level: 1);

            service.Grant(state, 10);
            Assert.AreEqual(2, state.level);
            Assert.AreEqual(10, service.GetRequiredExperience(state.level), "레벨 2에서도 필요량은 같다.");

            service.Grant(state, 10);
            Assert.AreEqual(3, state.level);
            Assert.AreEqual(10, service.GetRequiredExperience(state.level), "레벨 3에서도 같다(성장 곡선 없음).");
        }

        [Test]
        public void 필요_총량과_남은_양은_서로_다른_값을_말한다()
        {
            // 계정 진행도 쪽 PlayerProgress.ExpToNextLevel이 이미 <b>총량</b>을 뜻하므로, 같은 말이
            // 두 곳에서 다른 값을 가리키지 않게 이름을 갈라 두었다.
            CharacterProgressionService service = Service();
            CharacterSaveState state = State(level: 3, exp: 4);

            Assert.AreEqual(10, service.GetRequiredExperience(state.level), "필요 총량은 언제나 10이다.");
            Assert.AreEqual(6, service.ExperienceRemainingToNextLevel(state), "남은 양은 모아 둔 값을 뺀 나머지다.");
        }

        [Test]
        public void 필요_총량은_레벨과_무관하게_같고_설정한_값을_따른다()
        {
            CharacterProgressionService service = Service();

            foreach (int level in new[] { 1, 2, 50, 9999, int.MaxValue })
            {
                Assert.AreEqual(10, service.GetRequiredExperience(level), $"레벨 {level}");
            }

            Assert.AreEqual(3, Service(3).GetRequiredExperience(1), "설정한 필요량을 그대로 돌려준다.");
            Assert.AreEqual(1, Service(0).GetRequiredExperience(1), "하한이 적용된 값을 돌려준다.");
        }

        [Test]
        public void 필요_총량_조회는_어떤_값도_고치지_않는다()
        {
            // 앞으로 성장 곡선이 붙을 자리이므로, 지금부터 순수한 조회임을 못 박아 둔다.
            CharacterProgressionService service = Service();
            CharacterSaveState state = State(level: -4, exp: -9, stamina: 12);

            Assert.AreEqual(10, service.GetRequiredExperience(state.level),
                "1보다 작은 레벨도 계산할 때만 하한으로 본다.");

            Assert.AreEqual(-4, state.level, "조회가 저장 항목을 고치면 안 된다.");
            Assert.AreEqual(-9, state.currentExp);
            Assert.AreEqual(12, state.currentStamina);
        }

        [Test]
        public void 남은_양은_모으는_동안_줄어들다_레벨이_오르면_다시_총량이_된다()
        {
            CharacterProgressionService service = Service();
            CharacterSaveState state = State(level: 1, exp: 0);

            Assert.AreEqual(10, service.ExperienceRemainingToNextLevel(state));

            service.Grant(state, 7);
            Assert.AreEqual(3, service.ExperienceRemainingToNextLevel(state));

            service.Grant(state, 3);
            Assert.AreEqual(2, state.level);
            Assert.AreEqual(10, service.ExperienceRemainingToNextLevel(state),
                "레벨이 올라 진행도가 0이 되면 다시 총량만큼 남는다.");
        }

        // ---- 0 이하의 요청은 아무 일도 하지 않는다 ----

        [Test]
        public void 경험치_0을_주면_아무것도_바뀌지_않는다()
        {
            CharacterSaveState state = State(level: 3, exp: 4);
            CharacterProgressionResult result = Service().Grant(state, 0);

            Assert.IsFalse(result.Changed);
            Assert.AreEqual(0, result.ExperienceAdded);
            Assert.AreEqual(0, result.LevelsGained);
            Assert.AreEqual(3, state.level);
            Assert.AreEqual(4, state.currentExp);
        }

        [Test]
        public void 음수_경험치는_무시한다()
        {
            CharacterSaveState state = State(level: 3, exp: 4);

            foreach (int amount in new[] { -1, -10, int.MinValue })
            {
                CharacterProgressionResult result = Service().Grant(state, amount);

                Assert.IsFalse(result.Changed, $"{amount}");
                Assert.AreEqual(3, state.level, $"{amount} - 경험치를 빼앗지 않는다.");
                Assert.AreEqual(4, state.currentExp, $"{amount}");
            }
        }

        [Test]
        public void 무시된_요청은_어긋난_값도_고치지_않는다()
        {
            // "아무것도 주지 않는 요청"이 값을 조용히 고치면, 보상 0인 경로가 지나갈 때마다 상태가 달라진다.
            CharacterSaveState state = State(level: -5, exp: -3);
            CharacterProgressionResult result = Service().Grant(state, 0);

            Assert.IsFalse(result.Changed);
            Assert.AreEqual(-5, state.level, "정규화는 명시적으로 부를 때만 한다.");
            Assert.AreEqual(-3, state.currentExp);
            Assert.AreEqual(-5, result.PreviousLevel);
            Assert.AreEqual(-5, result.NewLevel);
        }

        // ---- 기본 적립과 이월 ----

        [Test]
        public void 필요량에_못_미치면_레벨은_그대로이고_경험치만_쌓인다()
        {
            CharacterSaveState state = State(level: 1, exp: 0);
            CharacterProgressionResult result = Service().Grant(state, 4);

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(4, result.ExperienceAdded);
            Assert.AreEqual(1, state.level);
            Assert.AreEqual(4, state.currentExp);
            Assert.AreEqual(0, result.LevelsGained);
            Assert.IsFalse(result.LeveledUp);
            Assert.AreEqual(6, Service().ExperienceRemainingToNextLevel(state), "10 중 4를 모았으니 6이 남는다.");
        }

        [Test]
        public void 필요량을_정확히_채우면_레벨이_하나_오르고_경험치는_0이_된다()
        {
            CharacterSaveState state = State(level: 1, exp: 0);
            CharacterProgressionResult result = Service().Grant(state, 10);

            Assert.AreEqual(2, state.level);
            Assert.AreEqual(0, state.currentExp);
            Assert.AreEqual(1, result.LevelsGained);
            Assert.IsTrue(result.LeveledUp);
        }

        [Test]
        public void 남는_경험치는_다음_레벨로_이월된다()
        {
            CharacterSaveState state = State(level: 1, exp: 8);
            CharacterProgressionResult result = Service().Grant(state, 5);

            // 8 + 5 = 13 -> 레벨 하나(10)를 쓰고 3이 남는다.
            Assert.AreEqual(2, state.level);
            Assert.AreEqual(3, state.currentExp, "넘긴 만큼은 버려지지 않는다.");
            Assert.AreEqual(1, result.LevelsGained);
        }

        [Test]
        public void 한_번에_여러_레벨이_오를_수_있다()
        {
            CharacterSaveState state = State(level: 1, exp: 0);
            CharacterProgressionResult result = Service().Grant(state, 35);

            Assert.AreEqual(4, state.level, "35 = 레벨 셋(30) + 나머지 5");
            Assert.AreEqual(5, state.currentExp);
            Assert.AreEqual(3, result.LevelsGained);
        }

        [Test]
        public void 이월은_이미_모아_둔_값과_합쳐서_계산한다()
        {
            CharacterSaveState state = State(level: 7, exp: 9);
            Service().Grant(state, 21);

            // 9 + 21 = 30 -> 레벨 셋, 나머지 0
            Assert.AreEqual(10, state.level);
            Assert.AreEqual(0, state.currentExp);
        }

        // ---- 결과 모델 ----

        [Test]
        public void 결과는_이전과_이후_값을_함께_담는다()
        {
            CharacterSaveState state = State("ElfArcher", level: 2, exp: 3);
            CharacterProgressionResult result = Service().Grant(state, 9);

            Assert.AreEqual("ElfArcher", result.CharacterId, "어느 캐릭터의 결과인지 알 수 있어야 한다.");
            Assert.AreEqual(9, result.ExperienceAdded);
            Assert.AreEqual(2, result.PreviousLevel);
            Assert.AreEqual(3, result.PreviousExp);
            Assert.AreEqual(3, result.NewLevel);
            Assert.AreEqual(2, result.NewExp);
            Assert.AreEqual(1, result.LevelsGained);
            Assert.IsTrue(result.Changed);
        }

        [Test]
        public void 결과는_저장_항목과_같은_값을_말한다()
        {
            CharacterSaveState state = State(level: 1, exp: 0);
            CharacterProgressionResult result = Service().Grant(state, 27);

            Assert.AreEqual(state.level, result.NewLevel);
            Assert.AreEqual(state.currentExp, result.NewExp);
        }

        // ---- 정규화 ----

        [Test]
        public void 명시적_정규화는_1보다_작은_레벨과_음수_경험치를_고친다()
        {
            CharacterSaveState state = State(level: 0, exp: -7);
            CharacterProgressionResult result = Service().Normalize(state);

            Assert.AreEqual(1, state.level);
            Assert.AreEqual(0, state.currentExp);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(0, result.PreviousLevel, "이전 값은 고치기 전의 원본이어야 한다.");
            Assert.AreEqual(-7, result.PreviousExp);
            Assert.AreEqual(0, result.ExperienceAdded, "정규화는 경험치를 더하지 않는다.");
        }

        [Test]
        public void 정규화는_이미_넘겨_모인_경험치를_레벨로_바꾼다()
        {
            CharacterSaveState state = State(level: 2, exp: 25);
            CharacterProgressionResult result = Service().Normalize(state);

            Assert.AreEqual(4, state.level, "25 = 레벨 둘(20) + 나머지 5");
            Assert.AreEqual(5, state.currentExp);
            Assert.AreEqual(2, result.LevelsGained);
        }

        [Test]
        public void 정규화는_멀쩡한_값을_건드리지_않는다()
        {
            CharacterSaveState state = State(level: 4, exp: 6);
            CharacterProgressionResult result = Service().Normalize(state);

            Assert.IsFalse(result.Changed);
            Assert.AreEqual(4, state.level);
            Assert.AreEqual(6, state.currentExp);
        }

        [Test]
        public void 정규화는_여러_번_해도_결과가_같다()
        {
            CharacterSaveState state = State(level: -2, exp: 34);
            CharacterProgressionService service = Service();

            service.Normalize(state);
            int level = state.level;
            int exp = state.currentExp;

            service.Normalize(state);

            Assert.AreEqual(level, state.level);
            Assert.AreEqual(exp, state.currentExp);
        }

        [Test]
        public void 양수를_넣으면_어긋난_값을_먼저_고치고_적용한다()
        {
            CharacterSaveState state = State(level: 0, exp: -4);
            CharacterProgressionResult result = Service().Grant(state, 12);

            // 정규화(레벨 1 / 경험치 0) 뒤에 12를 넣으므로 레벨 하나 + 나머지 2다.
            Assert.AreEqual(2, state.level);
            Assert.AreEqual(2, state.currentExp);
            Assert.AreEqual(0, result.PreviousLevel, "이전 값은 손대기 전의 원본이다.");
            Assert.AreEqual(-4, result.PreviousExp);
            Assert.AreEqual(1, result.LevelsGained);
        }

        [Test]
        public void 정규화로_레벨이_내려가도_얻은_레벨은_음수가_되지_않는다()
        {
            CharacterSaveState state = State(level: -100, exp: 0);
            CharacterProgressionResult result = Service().Normalize(state);

            Assert.AreEqual(1, state.level);
            Assert.AreEqual(0, result.LevelsGained, "'얻은 레벨'은 음수가 될 수 없다.");
            Assert.IsTrue(result.Changed);
        }

        [Test]
        public void 읽기만_하는_조회는_값을_고치지_않는다()
        {
            CharacterSaveState state = State(level: -3, exp: -9);
            int remaining = Service().ExperienceRemainingToNextLevel(state);

            Assert.AreEqual(10, remaining, "어긋난 값은 계산할 때만 하한으로 본다.");
            Assert.AreEqual(-3, state.level, "조회가 저장 항목을 고치면 안 된다.");
            Assert.AreEqual(-9, state.currentExp);
        }

        // ---- 담을 수 있는 마지막 자리 ----
        //
        // int.MaxValue는 기획이 정한 최대 레벨이 아니라 저장 칸의 한계다. 표현할 수 있는 마지막 자리는
        // 레벨 int.MaxValue / 경험치 (필요량 - 1)이며, 그 너머로 넘어온 경험치는 <b>받아들이지 않는다</b> -
        // 받은 척하면 "더해진 양"이 거짓말이 된다.

        [Test]
        public void 마지막_자리에서_양수를_넣어도_아무것도_바뀌지_않는다()
        {
            // 1이라도 받아들이면 표현할 수 있는 자리를 넘어간다. 가장 작은 양수가 경계를 가장 날카롭게 짚는다.
            CharacterSaveState state = State(level: int.MaxValue, exp: 9);
            CharacterProgressionResult result = Service().Grant(state, 1);

            Assert.AreEqual(int.MaxValue, state.level);
            Assert.AreEqual(9, state.currentExp);
            Assert.IsFalse(result.Changed, "담을 자리가 없으면 상태가 달라지지 않는다.");
            Assert.AreEqual(0, result.ExperienceAdded, "받아들이지 못한 양을 받았다고 적으면 안 된다.");
            Assert.AreEqual(0, result.LevelsGained);
        }

        [Test]
        public void 마지막_자리_직전에서는_담을_수_있는_만큼만_받아들인다()
        {
            // 레벨 (max-1) / 경험치 0에서 마지막 자리(레벨 max / 경험치 9)까지는 10 + 9 = 19다.
            CharacterSaveState state = State(level: int.MaxValue - 1, exp: 0);
            CharacterProgressionResult result = Service().Grant(state, int.MaxValue);

            Assert.AreEqual(19, result.ExperienceAdded, "실제로 받아들인 양만 적는다.");
            Assert.AreEqual(int.MaxValue, state.level);
            Assert.AreEqual(9, state.currentExp);
            Assert.AreEqual(1, result.LevelsGained);
            Assert.IsTrue(result.Changed);
        }

        [Test]
        public void 포화_뒤에_다시_넣어도_계속_그대로다()
        {
            CharacterSaveState state = State(level: int.MaxValue - 1, exp: 0);
            CharacterProgressionService service = Service();

            service.Grant(state, int.MaxValue);
            Assert.AreEqual(int.MaxValue, state.level);
            Assert.AreEqual(9, state.currentExp);

            for (int i = 0; i < 3; i++)
            {
                CharacterProgressionResult again = service.Grant(state, int.MaxValue);

                Assert.IsFalse(again.Changed, $"{i + 1}번째 반복");
                Assert.AreEqual(0, again.ExperienceAdded, $"{i + 1}번째 반복");
                Assert.AreEqual(int.MaxValue, state.level);
                Assert.AreEqual(9, state.currentExp);
            }
        }

        [Test]
        public void 진행은_양수를_넣는_동안_결코_뒤로_가지_않는다()
        {
            // 예전 구현은 상한 근처에서 레벨이 잘리며 진행이 되레 줄었다. 진행량 하나로 다루면
            // 그 자리가 없다 - 레벨은 절대 내려가지 않고, 같은 레벨이면 경험치도 내려가지 않는다.
            CharacterSaveState state = State(level: int.MaxValue - 2, exp: 3);
            CharacterProgressionService service = Service();

            foreach (int amount in new[] { 1, 7, int.MaxValue, 5, int.MaxValue })
            {
                int beforeLevel = state.level;
                int beforeExp = state.currentExp;

                service.Grant(state, amount);

                Assert.GreaterOrEqual(state.level, beforeLevel, $"{amount} - 레벨이 내려갔다.");
                if (state.level == beforeLevel)
                {
                    Assert.GreaterOrEqual(state.currentExp, beforeExp, $"{amount} - 진행이 뒤로 갔다.");
                }

                Assert.GreaterOrEqual(state.currentExp, 0);
                Assert.Less(state.currentExp, 10);
            }

            Assert.AreEqual(int.MaxValue, state.level, "끝에는 마지막 자리에 서 있어야 한다.");
            Assert.AreEqual(9, state.currentExp);
        }

        [Test]
        public void 마지막_레벨의_어긋난_경험치는_정규화가_마지막_자리로_내린다()
        {
            CharacterSaveState state = State(level: int.MaxValue, exp: 50);
            CharacterProgressionResult result = Service().Normalize(state);

            Assert.AreEqual(int.MaxValue, state.level);
            Assert.AreEqual(9, state.currentExp, "필요량 이상은 마지막 자리(필요량 - 1)로 내려온다.");
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(0, result.ExperienceAdded, "정규화는 경험치를 더하지 않는다.");
            Assert.AreEqual(0, result.LevelsGained, "레벨은 이미 마지막이라 오를 수 없다.");
        }

        [Test]
        public void 마지막_자리의_멀쩡한_값은_정규화가_건드리지_않는다()
        {
            CharacterSaveState state = State(level: int.MaxValue, exp: 9);
            CharacterProgressionResult result = Service().Normalize(state);

            Assert.AreEqual(int.MaxValue, state.level);
            Assert.AreEqual(9, state.currentExp);
            Assert.IsFalse(result.Changed);
            Assert.AreEqual(0, result.LevelsGained);
        }

        [Test]
        public void 마지막_레벨에서_남은_양은_표현할_수_있는_여유다()
        {
            CharacterProgressionService service = Service();

            Assert.AreEqual(0, service.ExperienceRemainingToNextLevel(State(level: int.MaxValue, exp: 9)),
                "마지막 자리에서는 더 담을 여유가 없다.");
            Assert.AreEqual(9, service.ExperienceRemainingToNextLevel(State(level: int.MaxValue, exp: 0)));
            Assert.AreEqual(4, service.ExperienceRemainingToNextLevel(State(level: int.MaxValue, exp: 5)));
            Assert.AreEqual(0, service.ExperienceRemainingToNextLevel(State(level: int.MaxValue, exp: 50)),
                "어긋난 값에서도 음수를 돌려주지 않는다.");
        }

        [Test]
        public void 마지막_레벨의_남은_양_조회도_값을_고치지_않는다()
        {
            CharacterSaveState state = State(level: int.MaxValue, exp: 50);
            Service().ExperienceRemainingToNextLevel(state);

            Assert.AreEqual(int.MaxValue, state.level, "조회가 저장 항목을 고치면 안 된다.");
            Assert.AreEqual(50, state.currentExp);
        }

        [Test]
        public void int_MaxValue를_넣어도_음수로_돌아_나가지_않는다()
        {
            CharacterSaveState state = State(level: 1, exp: 9);
            CharacterProgressionResult result = Service().Grant(state, int.MaxValue);

            // 레벨 1 / 경험치 9에서는 마지막 자리까지 여유가 넉넉하므로 요청한 만큼 다 받아들인다.
            Assert.AreEqual(int.MaxValue, result.ExperienceAdded);
            Assert.Greater(state.level, 1);
            Assert.GreaterOrEqual(state.currentExp, 0, "경험치가 음수가 되면 안 된다.");
            Assert.Less(state.currentExp, 10, "나머지는 언제나 필요량보다 작다.");
            Assert.IsTrue(result.Changed);
            Assert.Greater(result.LevelsGained, 0);
        }

        [Test]
        public void 필요량이_1이어도_아주_큰_값에서_멈추지_않는다()
        {
            // 필요량이 가장 작을 때가 레벨이 가장 많이 오르는 경우다 - 반복문으로 올렸다면 여기서 멈춘다.
            CharacterSaveState state = State(level: 1, exp: 0);
            CharacterProgressionResult result = Service(1).Grant(state, int.MaxValue);

            Assert.AreEqual(int.MaxValue, state.level);
            Assert.AreEqual(0, state.currentExp, "필요량이 1이면 마지막 자리의 진행도는 0이다.");
            Assert.AreEqual(int.MaxValue - 1, result.ExperienceAdded,
                "레벨 1에서 마지막 레벨까지는 (max - 1)칸이다.");
            Assert.IsTrue(result.Changed);
        }

        [Test]
        public void 어긋난_큰_경험치도_정규화가_감당한다()
        {
            CharacterSaveState state = State(level: int.MaxValue - 3, exp: int.MaxValue);
            Service().Normalize(state);

            Assert.AreEqual(int.MaxValue, state.level);
            Assert.AreEqual(9, state.currentExp, "넘친 만큼은 마지막 자리에서 멈춘다.");
        }

        [Test]
        public void 저장_칸의_한계는_기획상의_최대_레벨이_아니다()
        {
            // 이름과 문서가 그 뜻을 담고 있는지까지 못 박는다 - 나중에 진짜 최대 레벨이 생기면
            // 그것은 이 상수가 아니라 기획 데이터가 정해야 한다.
            Assert.AreEqual(int.MaxValue, CharacterProgressionService.MaxRepresentableLevel);

            CharacterSaveState state = State(level: 5000, exp: 0);
            Service().Grant(state, 1000);

            Assert.AreEqual(5100, state.level, "한계에 한참 못 미치는 구간에는 어떤 상한도 없다.");
        }

        // ---- 다루는 값의 경계 ----

        [Test]
        public void 저장_항목이_없으면_조용히_넘어가지_않는다()
        {
            CharacterProgressionService service = Service();

            Assert.Throws<ArgumentNullException>(() => service.Grant(null, 10));
            Assert.Throws<ArgumentNullException>(() => service.Normalize(null));
            Assert.Throws<ArgumentNullException>(() => service.ExperienceRemainingToNextLevel(null));
        }

        [Test]
        public void 경험치_계산은_다른_진행_값을_건드리지_않는다()
        {
            // 행동력과 id는 이 계산의 관심사가 아니다 - 여기서 함께 바뀌면 소유가 다른 값이 섞인다.
            CharacterSaveState state = State("RabbitHealer", level: 2, exp: 1, stamina: 17);

            Service().Grant(state, 44);
            Service().Normalize(state);

            Assert.AreEqual("RabbitHealer", state.characterId, "id를 건드리면 저장 항목의 키가 달라진다.");
            Assert.AreEqual(17, state.currentStamina, "행동력은 회복소와 로스터의 값이다.");
        }

        [Test]
        public void 같은_요청을_두_번_넣으면_두_번_쌓인다()
        {
            // 멱등이 아니다 - 경험치는 "지금 상태"가 아니라 "일어난 일"을 더하는 값이다.
            CharacterSaveState state = State(level: 1, exp: 0);
            CharacterProgressionService service = Service();

            service.Grant(state, 6);
            service.Grant(state, 6);

            Assert.AreEqual(2, state.level);
            Assert.AreEqual(2, state.currentExp);
        }
    }
}

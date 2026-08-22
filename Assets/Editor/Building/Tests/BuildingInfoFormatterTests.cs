using System.Collections.Generic;
using Building;
using NUnit.Framework;

namespace BuildingEditor.Tests
{
    /// <summary>
    /// <see cref="BuildingInfoFormatter"/>의 순수 함수 시험. 씬도 컴포넌트도 만들지 않는다 -
    /// 여기서 확인하는 것은 "숫자를 어떻게 늘어놓는가"와 "조각을 어떤 순서로 잇는가"뿐이다.
    /// </summary>
    public sealed class BuildingInfoFormatterTests
    {
        // ---- 건설 시간 ----

        [TestCase(0L, "00:00:00")]
        [TestCase(1L, "00:00:01")]
        [TestCase(59L, "00:00:59")]
        [TestCase(60L, "00:01:00")]
        [TestCase(3599L, "00:59:59")]
        [TestCase(3600L, "01:00:00")]
        [TestCase(86399L, "23:59:59")]
        public void 하루_안쪽_시간은_두자리_시계로_표시된다(long seconds, string expected)
        {
            Assert.AreEqual(expected, BuildingInfoFormatter.FormatBuildTime(seconds));
        }

        [TestCase(86400L, "24:00:00")]
        [TestCase(90000L, "25:00:00")]
        [TestCase(360000L, "100:00:00")]
        [TestCase(90061L, "25:01:01")]
        public void 하루를_넘겨도_되감기지_않는다(long seconds, string expected)
        {
            Assert.AreEqual(expected, BuildingInfoFormatter.FormatBuildTime(seconds),
                "24시간에서 되감긴 값은 '곧 끝난다'로 잘못 읽힌다");
        }

        [TestCase(-1L)]
        [TestCase(long.MinValue)]
        public void 음수_시간은_예외없이_0으로_표시된다(long seconds)
        {
            Assert.AreEqual(BuildingInfoFormatter.ZeroTime, BuildingInfoFormatter.FormatBuildTime(seconds));
        }

        // ---- 금액 ----

        [TestCase(0L, "0")]
        [TestCase(999L, "999")]
        [TestCase(1000L, "1,000")]
        [TestCase(2000L, "2,000")]
        [TestCase(1234567L, "1,234,567")]
        public void 금액은_언제나_쉼표로_천단위를_끊는다(long amount, string expected)
        {
            Assert.AreEqual(expected, BuildingInfoFormatter.FormatAmount(amount),
                "실행 환경의 지역 설정과 무관하게 쉼표로 고정되어야 한다");
        }

        // ---- 비용 조립 ----

        [Test]
        public void 비용_한_조각은_금액_뒤에_이름을_붙인다()
        {
            var component = new BuildingInfoFormatter.CostComponent("2,000", "Jewel");

            Assert.IsTrue(component.IsComplete);
            Assert.AreEqual("2,000 Jewel", component.ToDisplayString());
        }

        [Test]
        public void 여러_조각은_같은_구분자로_이어진다()
        {
            var components = new List<BuildingInfoFormatter.CostComponent>
            {
                new BuildingInfoFormatter.CostComponent("2,000", "Jewel"),
                new BuildingInfoFormatter.CostComponent("3", "Wood"),
                new BuildingInfoFormatter.CostComponent("10", "Stone"),
            };

            Assert.AreEqual("2,000 Jewel, 3 Wood, 10 Stone",
                BuildingInfoFormatter.ComposeCost(components),
                "아이템 비용이 늘어나도 조립 규칙은 이 한 곳만 바뀐다");
        }

        [Test]
        public void 아직_번역이_오지_않은_조각은_건너뛴다()
        {
            var components = new List<BuildingInfoFormatter.CostComponent>
            {
                new BuildingInfoFormatter.CostComponent("2,000", null),
                new BuildingInfoFormatter.CostComponent("3", "Wood"),
            };

            Assert.AreEqual("3 Wood", BuildingInfoFormatter.ComposeCost(components),
                "반쪽짜리 조각을 화면에 내보내지 않는다");
        }

        [Test]
        public void 비용이_없으면_빈_문자열이고_null이_아니다()
        {
            Assert.AreEqual(string.Empty, BuildingInfoFormatter.ComposeCost(null));
            Assert.AreEqual(string.Empty,
                BuildingInfoFormatter.ComposeCost(new List<BuildingInfoFormatter.CostComponent>()));
        }

        // ---- 설명 조립 ----

        [Test]
        public void 설명은_기능_시간_비용_순서로_채워진다()
        {
            string result = BuildingInfoFormatter.ComposeDescription(
                "Unlock - {0}\n\nTime - {1}\nCost - {2}",
                "Mercenary", "00:01:00", "2,000 Jewel", out bool failed);

            Assert.IsFalse(failed);
            Assert.AreEqual("Unlock - Mercenary\n\nTime - 00:01:00\nCost - 2,000 Jewel", result);
        }

        [Test]
        public void 자리표시자가_맞지_않으면_틀을_그대로_돌려주고_알린다()
        {
            string result = BuildingInfoFormatter.ComposeDescription(
                "Unlock - {0} {3}", "Mercenary", "00:01:00", "2,000 Jewel", out bool failed);

            Assert.IsTrue(failed, "호출한 쪽이 로그를 한 번 남길 수 있어야 한다");
            Assert.AreEqual("Unlock - {0} {3}", result, "화면이 비는 대신 저작된 틀이 보여야 한다");
        }

        [TestCase(null)]
        [TestCase("")]
        public void 틀이_비어_있으면_대체_문구를_지어내지_않는다(string format)
        {
            string result = BuildingInfoFormatter.ComposeDescription(
                format, "Mercenary", "00:01:00", "2,000 Jewel", out bool failed);

            Assert.IsFalse(failed);
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public void 값이_null이어도_예외없이_빈칸으로_채운다()
        {
            string result = BuildingInfoFormatter.ComposeDescription(
                "[{0}][{1}][{2}]", null, null, null, out bool failed);

            Assert.IsFalse(failed);
            Assert.AreEqual("[][][]", result);
        }
    }
}

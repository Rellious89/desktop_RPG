using Building;
using NUnit.Framework;
using UnityEngine;

namespace BuildingEditor.Tests
{
    /// <summary>
    /// 건물 팝업이 놓일 자리를 정하는 <b>순수 계산</b> 시험. 씬도 캔버스도 만들지 않으므로 화면 크기,
    /// 폰트, 레이아웃 재계산 시점 같은 것에 결과가 흔들리지 않는다 - "어느 후보가 이겼는가"와
    /// "화면 밖으로 나가지 않는가"만 정확한 숫자로 확인한다.
    ///
    /// 좌표는 전부 팝업 부모의 로컬 좌표이며 y는 위가 크다(Unity UI와 같다). 네 후보는 모두 버튼
    /// 사각형 <b>바깥</b>이므로, 기대값도 버튼의 변에서 여백만큼 떨어진 자리로 적는다.
    /// </summary>
    public sealed class BuildingPopupPlacementTests
    {
        private const float Margin = 8f;

        /// <summary>넉넉한 화면. 안쪽 범위는 여백을 뺀 (-192, -192)~(192, 192)다.</summary>
        private static readonly Rect Bounds = Rect.MinMaxRect(-200f, -200f, 200f, 200f);

        private static readonly Vector2 Size = new Vector2(100f, 80f);

        [Test]
        public void 자리가_넉넉하면_오른쪽_위를_고른다()
        {
            Rect source = Rect.MinMaxRect(-10f, -10f, 10f, 10f);

            Vector2 min = BuildingPopupPlacement.Solve(source, Size, Bounds, Margin, out BuildingPopupSide side);

            Assert.AreEqual(BuildingPopupSide.RightAbove, side);
            Assert.AreEqual(source.xMax + Margin, min.x, 0.001f, "버튼 오른쪽 변 바깥에서 여백만큼 띄운다");
            Assert.AreEqual(source.yMax + Margin, min.y, 0.001f, "버튼 윗변에서 여백만큼 띄운다");
        }

        [Test]
        public void 오른쪽이_모자라면_왼쪽_위로_넘긴다()
        {
            Rect source = Rect.MinMaxRect(150f, -10f, 170f, 10f);

            Vector2 min = BuildingPopupPlacement.Solve(source, Size, Bounds, Margin, out BuildingPopupSide side);

            Assert.AreEqual(BuildingPopupSide.LeftAbove, side);
            Assert.AreEqual(source.xMin - Margin - Size.x, min.x, 0.001f,
                "버튼 왼쪽 변 바깥에서 여백만큼 띄운다 - 오른쪽 변이 버튼 왼쪽 변보다 왼쪽이다");
            Assert.AreEqual(source.yMax + Margin, min.y, 0.001f);
            AssertInsideBounds(min, Size);
        }

        [Test]
        public void 위쪽이_모자라면_아래_후보로_내려간다()
        {
            Rect source = Rect.MinMaxRect(-10f, 150f, 10f, 170f);

            Vector2 min = BuildingPopupPlacement.Solve(source, Size, Bounds, Margin, out BuildingPopupSide side);

            Assert.AreEqual(BuildingPopupSide.RightBelow, side);
            Assert.AreEqual(source.xMax + Margin, min.x, 0.001f);
            Assert.AreEqual(source.yMin - Margin - Size.y, min.y, 0.001f, "버튼 아랫변에서 여백만큼 띄운다");
            AssertInsideBounds(min, Size);
        }

        [Test]
        public void 오른쪽도_위쪽도_모자라면_왼쪽_아래를_고른다()
        {
            Rect source = Rect.MinMaxRect(150f, 150f, 170f, 170f);

            Vector2 min = BuildingPopupPlacement.Solve(source, Size, Bounds, Margin, out BuildingPopupSide side);

            Assert.AreEqual(BuildingPopupSide.LeftBelow, side);
            Assert.AreEqual(source.xMin - Margin - Size.x, min.x, 0.001f);
            Assert.AreEqual(source.yMin - Margin - Size.y, min.y, 0.001f);
            AssertInsideBounds(min, Size);
        }

        [Test]
        public void 위가_가능하면_아래는_보지_않는다()
        {
            // 화면 한가운데라 위에도 아래에도 자리가 있다. 순서가 지켜지면 언제나 위를 고른다.
            Rect source = Rect.MinMaxRect(-10f, -10f, 10f, 10f);

            Vector2 min = BuildingPopupPlacement.Solve(source, Size, Bounds, Margin, out BuildingPopupSide side);

            Assert.AreEqual(BuildingPopupSide.RightAbove, side);
            Assert.Greater(min.y, source.yMax, "아래 후보로 내려가면 안 된다");
        }

        [Test]
        public void 아래로_내려가도_오른쪽을_먼저_시도한다()
        {
            // 위쪽만 모자란 자리 - 아래의 두 후보는 <b>둘 다</b> 들어간다.
            Rect source = Rect.MinMaxRect(-10f, 150f, 10f, 170f);

            Vector2 min = BuildingPopupPlacement.Solve(source, Size, Bounds, Margin, out BuildingPopupSide side);

            Assert.AreEqual(BuildingPopupSide.RightBelow, side,
                "아래에서도 오른쪽 후보가 왼쪽 후보보다 먼저다");
            Assert.AreEqual(source.xMax + Margin, min.x, 0.001f);
        }

        [Test]
        public void 네_후보가_모두_실패하면_화면_안으로_당긴다()
        {
            // 팝업(100x80)이 겨우 들어가는 화면. 어느 후보도 통째로 들어가지 않는다.
            Rect bounds = Rect.MinMaxRect(0f, 0f, 120f, 100f);
            Rect source = Rect.MinMaxRect(50f, 40f, 70f, 60f);

            Vector2 min = BuildingPopupPlacement.Solve(source, Size, bounds, Margin, out BuildingPopupSide side);

            Assert.AreEqual(BuildingPopupSide.Clamped, side);
            AssertInsideBounds(min, Size, bounds);
        }

        [Test]
        public void 화면이_팝업보다_좁으면_안쪽_경계에_붙인다()
        {
            Rect bounds = Rect.MinMaxRect(0f, 0f, 50f, 50f);
            Rect source = Rect.MinMaxRect(20f, 20f, 30f, 30f);

            Vector2 min = BuildingPopupPlacement.Solve(source, Size, bounds, Margin, out BuildingPopupSide side);

            Assert.AreEqual(BuildingPopupSide.Clamped, side);
            Assert.AreEqual(bounds.xMin + Margin, min.x, 0.001f,
                "당길 자리가 없으면 최소 경계에 붙인다 - 뒤집힌 범위로 Clamp하면 반대쪽이 잘린다");
            Assert.AreEqual(bounds.yMin + Margin, min.y, 0.001f);
        }

        [Test]
        public void 팝업이_높아지면_같은_자리에서도_후보가_바뀐다()
        {
            // 경고가 켜져 팝업이 높아지는 상황을 그대로 흉내낸다 - 자리를 정하기 전에 높이가
            // 확정되어야 하는 이유가 이것이다.
            Rect source = Rect.MinMaxRect(-10f, 60f, 10f, 80f);

            BuildingPopupPlacement.Solve(source, new Vector2(100f, 100f), Bounds, Margin,
                out BuildingPopupSide shortSide);
            BuildingPopupPlacement.Solve(source, new Vector2(100f, 130f), Bounds, Margin,
                out BuildingPopupSide tallSide);

            Assert.AreEqual(BuildingPopupSide.RightAbove, shortSide);
            Assert.AreEqual(BuildingPopupSide.RightBelow, tallSide,
                "높아진 팝업이 위에 다 들어가지 않으면 아래로 내려가야 한다");
        }

        [Test]
        public void 버튼이_움직이면_자리도_같은_방향으로_움직인다()
        {
            Vector2 before = BuildingPopupPlacement.Solve(
                Rect.MinMaxRect(-10f, -10f, 10f, 10f), Size, Bounds, Margin, out _);
            Vector2 after = BuildingPopupPlacement.Solve(
                Rect.MinMaxRect(20f, 30f, 40f, 50f), Size, Bounds, Margin, out _);

            Assert.Greater(after.x, before.x);
            Assert.Greater(after.y, before.y);
        }

        [Test]
        public void 여백이_음수면_0으로_본다()
        {
            Rect source = Rect.MinMaxRect(-10f, -10f, 10f, 10f);

            Vector2 min = BuildingPopupPlacement.Solve(source, Size, Bounds, -5f, out BuildingPopupSide side);

            Assert.AreEqual(BuildingPopupSide.RightAbove, side);
            Assert.AreEqual(source.xMax, min.x, 0.001f, "음수 여백이 팝업을 버튼 위로 겹쳐 올리면 안 된다");
            Assert.AreEqual(source.yMax, min.y, 0.001f, "여백이 0이면 버튼 변에 딱 붙는다 - 그 안쪽으로는 들어가지 않는다");
        }

        [Test]
        public void 같은_입력이면_언제나_같은_답이_나온다()
        {
            Rect source = Rect.MinMaxRect(150f, 150f, 170f, 170f);

            Vector2 first = BuildingPopupPlacement.Solve(source, Size, Bounds, Margin, out BuildingPopupSide a);
            Vector2 second = BuildingPopupPlacement.Solve(source, Size, Bounds, Margin, out BuildingPopupSide b);

            Assert.AreEqual(a, b);
            Assert.AreEqual(first, second);
        }

        private static void AssertInsideBounds(Vector2 min, Vector2 size)
        {
            AssertInsideBounds(min, size, Bounds);
        }

        private static void AssertInsideBounds(Vector2 min, Vector2 size, Rect bounds)
        {
            Assert.GreaterOrEqual(min.x, bounds.xMin + Margin - 0.001f, "왼쪽이 화면 밖으로 나갔다");
            Assert.LessOrEqual(min.x + size.x, bounds.xMax - Margin + 0.001f, "오른쪽이 화면 밖으로 나갔다");
            Assert.GreaterOrEqual(min.y, bounds.yMin + Margin - 0.001f, "아래가 화면 밖으로 나갔다");
            Assert.LessOrEqual(min.y + size.y, bounds.yMax - Margin + 0.001f, "위가 화면 밖으로 나갔다");
        }
    }
}

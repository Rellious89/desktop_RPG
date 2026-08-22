using UnityEngine;

namespace Building
{
    /// <summary>건물 팝업이 실제로 놓인 자리. 진단과 시험이 "왜 여기에 놓였는가"를 읽을 수 있게
    /// 후보의 이름을 그대로 값으로 둔다 - 좌표만 보고는 어느 규칙이 이겼는지 알 수 없다.</summary>
    public enum BuildingPopupSide
    {
        /// <summary>버튼의 <b>오른쪽 바깥</b>, 그리고 버튼 <b>윗변 위</b>(첫 번째 후보).</summary>
        RightAbove,

        /// <summary>버튼의 <b>왼쪽 바깥</b>, 그리고 버튼 <b>윗변 위</b>.</summary>
        LeftAbove,

        /// <summary>버튼의 <b>오른쪽 바깥</b>, 그리고 버튼 <b>아랫변 아래</b>.</summary>
        RightBelow,

        /// <summary>버튼의 <b>왼쪽 바깥</b>, 그리고 버튼 <b>아랫변 아래</b>.</summary>
        LeftBelow,

        /// <summary>네 후보 중 어느 것도 화면 안에 다 들어가지 않아 <b>화면 안으로 당긴</b> 자리.</summary>
        Clamped
    }

    /// <summary>
    /// 건물 팝업을 <b>누른 버튼 옆에</b> 놓을 자리를 정하는 계산. Unity 오브젝트를 하나도 만지지
    /// 않는 <b>순수 계산</b>이라 EditMode 시험에서 씬 없이 그대로 확인할 수 있다 - 좌표를 정하는
    /// 규칙과 그 좌표를 RectTransform에 적용하는 일을 나눠 둔 이유가 이것이다.
    ///
    /// <b>모든 값은 같은 공간(팝업 부모의 로컬 좌표)에서 온다.</b> 월드 좌표로 더하고 빼면 Canvas
    /// 배율이 바뀔 때 여백만 함께 커지거나 작아진다(아이템 툴팁의 자리 계산과 같은 규칙이다).
    ///
    /// <b>네 후보 모두 버튼 사각형 바깥이다</b> - 가로로 버튼 옆에, 세로로 버튼 위나 아래에 놓아
    /// 팝업이 버튼을 덮지 않는다.
    ///
    /// <b>후보 순서는 고정이다</b> - 오른쪽-위 → 왼쪽-위 → 오른쪽-아래 → 왼쪽-아래. 앞의 후보가
    /// 화면(여백을 뺀 안쪽) 안에 <b>통째로</b> 들어가면 그 자리로 끝이고, 넷 다 실패했을 때만
    /// 첫 후보를 화면 안으로 당긴다. 순서를 고정해 두었으므로 같은 입력이면 언제나 같은 답이 나온다.
    /// </summary>
    public static class BuildingPopupPlacement
    {
        /// <summary>
        /// 팝업 사각형의 <b>왼쪽-아래 모서리</b>를 돌려준다(pivot과 무관한 값이다 - pivot을 되짚는
        /// 일은 적용하는 쪽의 몫이다).
        /// </summary>
        /// <param name="source">누른 버튼이 차지한 사각형.</param>
        /// <param name="size">지금 레이아웃이 끝난 팝업의 크기(가로, 세로).</param>
        /// <param name="bounds">넘어가면 안 되는 범위(Canvas 사각형).</param>
        /// <param name="margin">버튼과의 간격이자 화면 가장자리에서 띄울 여백. 음수는 0으로 본다.</param>
        /// <param name="side">고른 후보. 넷 다 실패하면 <see cref="BuildingPopupSide.Clamped"/>다.</param>
        public static Vector2 Solve(Rect source, Vector2 size, Rect bounds, float margin, out BuildingPopupSide side)
        {
            if (margin < 0f) margin = 0f;

            float insetMinX = bounds.xMin + margin;
            float insetMaxX = bounds.xMax - margin;
            float insetMinY = bounds.yMin + margin;
            float insetMaxY = bounds.yMax - margin;

            // 가로 두 갈래: 버튼 오른쪽 변 바깥으로 margin 띄우거나, 왼쪽 변 바깥으로 margin 띄운다.
            // 두 갈래 모두 버튼 사각형과 <b>겹치지 않는다</b> - 팝업이 자기를 띄운 버튼을 가려 버리면
            // 버튼의 이름도 상태도 보이지 않아 무엇을 눌렀는지 확인할 수 없기 때문이다.
            float rightX = source.xMax + margin;
            float leftX = source.xMin - margin - size.x;

            // 세로 두 갈래: 버튼 윗변 위로 margin 띄우거나, 아랫변 아래로 margin 띄운다.
            float aboveY = source.yMax + margin;
            float belowY = source.yMin - margin - size.y;

            if (Fits(rightX, aboveY, size, insetMinX, insetMaxX, insetMinY, insetMaxY))
            {
                side = BuildingPopupSide.RightAbove;
                return new Vector2(rightX, aboveY);
            }
            if (Fits(leftX, aboveY, size, insetMinX, insetMaxX, insetMinY, insetMaxY))
            {
                side = BuildingPopupSide.LeftAbove;
                return new Vector2(leftX, aboveY);
            }
            if (Fits(rightX, belowY, size, insetMinX, insetMaxX, insetMinY, insetMaxY))
            {
                side = BuildingPopupSide.RightBelow;
                return new Vector2(rightX, belowY);
            }
            if (Fits(leftX, belowY, size, insetMinX, insetMaxX, insetMinY, insetMaxY))
            {
                side = BuildingPopupSide.LeftBelow;
                return new Vector2(leftX, belowY);
            }

            // 어느 후보도 다 들어가지 않는다 - <b>첫 후보(오른쪽-위)를</b> 화면 안으로 당긴다.
            // 순서를 지키는 것이 중요하다: 아래 후보를 당기면 버튼이 화면 위쪽에 있을 때 팝업이
            // 화면 바닥으로 내려가 "누른 버튼 옆"이라는 관계가 끊어진다. 팝업이 화면보다 크면 당길
            // 자리가 없으므로 안쪽 경계에 붙인다(잘리더라도 왼쪽-아래부터 보이게 한다).
            side = BuildingPopupSide.Clamped;
            return new Vector2(
                Clamp(rightX, size.x, insetMinX, insetMaxX),
                Clamp(aboveY, size.y, insetMinY, insetMaxY));
        }

        /// <summary>이 자리에 놓았을 때 팝업이 안쪽 범위에 <b>통째로</b> 들어가는지.</summary>
        private static bool Fits(
            float x, float y, Vector2 size,
            float insetMinX, float insetMaxX, float insetMinY, float insetMaxY)
        {
            if (x < insetMinX || x + size.x > insetMaxX) return false;
            if (y < insetMinY || y + size.y > insetMaxY) return false;
            return true;
        }

        /// <summary>한 축을 안쪽 범위로 당긴다. 범위가 팝업보다 좁으면 최소 경계에 붙인다 -
        /// <see cref="Mathf.Clamp"/>에 뒤집힌 범위를 넘기면 답이 max로 튀어 반대쪽이 잘린다.</summary>
        private static float Clamp(float value, float length, float insetMin, float insetMax)
        {
            if (insetMax - insetMin < length) return insetMin;
            return Mathf.Clamp(value, insetMin, insetMax - length);
        }
    }
}

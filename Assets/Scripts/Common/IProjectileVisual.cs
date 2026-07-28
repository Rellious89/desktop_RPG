namespace Common
{
    /// <summary>
    /// 발사체의 "보이는 부분"이 구현하는 계약. <see cref="ProjectileMover"/>는 이동/회전/수명만 담당하고
    /// 표현은 전부 이 인터페이스를 통해서만 건드린다 - 그래서 이동 시스템이 특정 SpriteRenderer 한 장에
    /// 묶이지 않는다. 구현체는 발사체 루트에 있어도 되고 자식 오브젝트에 있어도 되며, 한 발사체에 여러 개가
    /// 붙어 있어도 된다(Mover가 GetComponentsInChildren로 전부 찾아 같은 진행도를 넘긴다).
    ///
    /// 호출 순서는 항상 BeginFlight -> SetFlightProgress(0~1, 매 프레임) -> ResetVisual(풀 반환 시)이다.
    /// 풀에서 재사용되므로 ResetVisual은 반드시 프레임/알파 등 모든 재생 상태를 초기값으로 되돌려야 한다.
    /// </summary>
    public interface IProjectileVisual
    {
        /// <summary>발사 직전 한 번 호출된다. flightDuration은 이번 비행에 걸릴 시간(초)으로, 재생 프레임
        /// 배분이나 "너무 짧으면 Fade 생략" 같은 판단에 쓴다.</summary>
        void BeginFlight(float flightDuration);

        /// <summary>비행 진행도(0 = 시전 위치, 1 = 도착 위치)를 매 프레임 전달한다. 도착 시점에 스냅
        /// 완료되는 경우에도 마지막으로 1이 한 번 더 들어온다.</summary>
        void SetFlightProgress(float normalizedProgress);

        /// <summary>풀로 반환될 때 호출된다. 다음 재사용이 이전 비행 상태를 물려받지 않도록 프레임/알파를
        /// 프리팹 원본 값으로 되돌린다.</summary>
        void ResetVisual();
    }
}

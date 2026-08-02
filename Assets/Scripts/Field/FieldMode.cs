namespace Field
{
    /// <summary>
    /// 플레이어가 지금 어디에 있는지를 나타내는 <b>필드 모드</b>. 값은 이 둘뿐이며, "이동 중"이나
    /// "전투 중" 같은 과도 상태는 여기에 없다 - 전환이 진행 중인지는
    /// <see cref="FieldModeManager.IsTransitioning"/>이, 전투가 가능한지는
    /// <see cref="FieldModeManager.CanCombat"/>이 따로 답한다.
    ///
    /// <b>이 열거형에 값을 더 늘리지 않는다.</b> 모드가 늘어나는 것처럼 보이는 요구(보스방, 상점 등)는
    /// 대부분 "어느 던전에 있는가"의 문제이므로 <see cref="FieldModeManager.CurrentDungeon"/>이 답해야
    /// 하고, 그렇게 해야 모드로 분기하는 코드가 두 갈래를 넘지 않는다.
    /// </summary>
    public enum FieldMode
    {
        /// <summary>마을. 기본 상태이며 전투가 성립하지 않는다.</summary>
        Town = 0,

        /// <summary>던전. 이 모드일 때만 <see cref="FieldModeManager.CurrentDungeon"/>이 채워진다.</summary>
        Dungeon = 1,
    }
}

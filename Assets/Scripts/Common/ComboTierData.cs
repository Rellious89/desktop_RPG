using System;
using UnityEngine;

namespace Common
{
    /// <summary>
    /// 콤보 티어 하나의 판정 기준과 표현값. ComboTierPresenter의 tiers 목록 원소로 쓰인다.
    /// minCombo 오름차순으로 판정하며(ComboTierPresenter가 정렬/검증한다), 현재 콤보 이하에서
    /// minCombo가 가장 큰 티어가 선택된다.
    /// </summary>
    [Serializable]
    public class ComboTierData
    {
        [Tooltip("이 값 이상일 때 해당 티어로 판정한다.")]
        public int minCombo;
        public string tierName = "Normal";
        [Tooltip("이 티어의 콤보 숫자/티어 텍스트 색상.")]
        public Color textColor = Color.white;
        [Tooltip("이 티어에서의 기준(정지) 스케일 배율 - 초기화 시 캡처한 로컬 스케일에 곱한다.")]
        public float comboScale = 1f;
        [Tooltip("이 티어로 진입하는 순간 튀어 오르는 최고 스케일 배율(기준 스케일 대비).")]
        public float transitionScale = 1.3f;
        [Tooltip("티어 전환 연출의 전체 재생 시간(초).")]
        public float transitionDuration = 0.3f;
    }
}

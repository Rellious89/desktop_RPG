using System;
using DG.Tweening;
using UnityEngine;

namespace Common
{
    /// <summary>UITweenTransition의 이동 방향. Custom은 Phase의 Custom Offset(Vector2)을 그대로 쓴다.</summary>
    public enum UITweenDirection
    {
        None,
        Left,
        Right,
        Up,
        Down,
        Custom
    }

    /// <summary>Ease를 DOTween 프리셋으로 줄지, Inspector에서 직접 편집한 AnimationCurve로 줄지 선택한다.</summary>
    public enum UITweenEaseMode
    {
        Preset,
        AnimationCurve
    }

    /// <summary>
    /// Tween 하나에 적용할 Ease 설정. Preset 모드는 DOTween의 Ease enum을 그대로 노출하고,
    /// AnimationCurve 모드는 사용자가 그린 커브를 사용한다. 커브 모드인데 커브가 비어 있으면
    /// (키 1개 이하) Tween이 굳어버리는 대신 Preset으로 물러난다.
    /// </summary>
    [Serializable]
    public class UITweenEase
    {
        [Tooltip("Preset: DOTween Ease enum 사용 / AnimationCurve: 아래 Curve 사용.")]
        [SerializeField] private UITweenEaseMode mode = UITweenEaseMode.Preset;

        [Tooltip("DOTween Ease 프리셋. Unset을 고르면 DOTween 기본 Ease를 그대로 쓴다.")]
        [SerializeField] private Ease preset = Ease.OutCubic;

        [Tooltip("Mode가 AnimationCurve일 때만 사용한다. 가로축 0~1(진행률), 세로축 0~1(보간값)이 기본이다.")]
        [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public UITweenEase()
        {
        }

        public UITweenEase(Ease preset)
        {
            this.preset = preset;
        }

        /// <summary>이 설정을 Tween에 적용한다.</summary>
        public void Apply(Tween tween)
        {
            if (tween == null) return;

            if (mode == UITweenEaseMode.AnimationCurve && curve != null && curve.length >= 2)
            {
                tween.SetEase(curve);
                return;
            }

            switch (preset)
            {
                case Ease.Unset:
                    // DOTween 기본 Ease를 그대로 둔다.
                    break;
                case Ease.INTERNAL_Zero:
                case Ease.INTERNAL_Custom:
                    // DOTween 내부 전용 값이라 직접 지정하면 보간이 깨진다.
                    tween.SetEase(Ease.Linear);
                    break;
                default:
                    tween.SetEase(preset);
                    break;
            }
        }
    }

    /// <summary>
    /// Enter 또는 Exit 한 구간(Phase)의 설정. Move/Fade/Scale 세 채널을 각각 켜고 끌 수 있고,
    /// 세 채널은 같은 Duration/Delay로 동시에 재생된다.
    /// From 값(From Alpha / From Scale)은 Enter에서만 쓰인다. Exit는 항상 "현재 값"에서 시작하므로
    /// Enter 도중에 Exit가 들어와도 알파나 크기가 튀지 않는다.
    /// </summary>
    [Serializable]
    public class UITweenPhaseSettings
    {
        [Tooltip("끄면 이 구간은 연출 없이 즉시 끝난다(완료 콜백/이벤트는 그대로 호출된다).")]
        [SerializeField] private bool enabled = true;

        [Tooltip("Tween 재생 시간(초). 세 채널이 이 시간을 공유한다.")]
        [Range(0f, 5f)]
        [SerializeField] private float duration = 0.25f;

        [Tooltip("재생 시작까지 기다리는 시간(초). 대기 중에는 시작 상태로 멈춰 있는다.")]
        [Range(0f, 5f)]
        [SerializeField] private float delay;

        [Header("Move")]
        [Tooltip("anchoredPosition 이동을 사용한다.")]
        [SerializeField] private bool move = true;

        [Tooltip("이동 방향. Enter는 이 방향 바깥에서 들어오고, Exit는 이 방향 바깥으로 나간다.")]
        [SerializeField] private UITweenDirection direction = UITweenDirection.Right;

        [Tooltip("기준 위치에서 떨어진 거리(px). Direction이 Custom이면 무시된다.")]
        [SerializeField] private float distance = 400f;

        [Tooltip("Direction이 Custom일 때 사용하는 오프셋(px). Distance를 곱하지 않고 그대로 쓴다.")]
        [SerializeField] private Vector2 customOffset = new Vector2(400f, 0f);

        [Header("Fade")]
        [Tooltip("CanvasGroup alpha를 사용한다. 자기 자신과 모든 자식 UI에 함께 적용된다.")]
        [SerializeField] private bool fade = true;

        [Tooltip("Enter 전용 시작 Alpha. Exit는 이 값을 무시하고 현재 Alpha에서 시작한다.")]
        [Range(0f, 1f)]
        [SerializeField] private float fromAlpha;

        [Tooltip("이 구간이 끝났을 때의 Alpha.")]
        [Range(0f, 1f)]
        [SerializeField] private float toAlpha = 1f;

        [Header("Scale")]
        [Tooltip("localScale 변화를 사용한다. 기본값은 꺼짐이다.")]
        [SerializeField] private bool scale;

        [Tooltip("Enter 전용 시작 배율(기준 Scale에 곱한다). Exit는 현재 Scale에서 시작한다.")]
        [SerializeField] private Vector3 fromScale = new Vector3(0.9f, 0.9f, 0.9f);

        [Tooltip("이 구간이 끝났을 때의 배율(기준 Scale에 곱한다). 1,1,1이면 기준 Scale이다.")]
        [SerializeField] private Vector3 toScale = Vector3.one;

        [Header("Ease")]
        [Tooltip("세 채널이 공통으로 쓰는 Ease.")]
        [SerializeField] private UITweenEase ease = new UITweenEase(Ease.OutCubic);

        [Tooltip("켜면 아래 Move/Fade/Scale Ease를 채널별로 따로 쓴다. 끄면 위의 공통 Ease만 쓴다.")]
        [SerializeField] private bool useChannelEase;

        [SerializeField] private UITweenEase moveEase = new UITweenEase(Ease.OutCubic);
        [SerializeField] private UITweenEase fadeEase = new UITweenEase(Ease.Linear);
        [SerializeField] private UITweenEase scaleEase = new UITweenEase(Ease.OutCubic);

        public UITweenPhaseSettings()
        {
        }

        /// <summary>컴포넌트가 Enter/Exit 기본값을 서로 다르게 넣기 위해 쓰는 생성자.</summary>
        public UITweenPhaseSettings(float duration, float fromAlpha, float toAlpha, Ease easePreset)
        {
            this.duration = duration;
            this.fromAlpha = fromAlpha;
            this.toAlpha = toAlpha;
            ease = new UITweenEase(easePreset);
            moveEase = new UITweenEase(easePreset);
            scaleEase = new UITweenEase(easePreset);
        }

        public bool Enabled => enabled;
        public float Duration => duration;
        public float Delay => delay;

        public bool MoveEnabled => move;
        public bool FadeEnabled => fade;
        public bool ScaleEnabled => scale;

        public float FromAlpha => fromAlpha;
        public float ToAlpha => toAlpha;
        public Vector3 FromScale => fromScale;
        public Vector3 ToScale => toScale;

        public UITweenEase MoveEase => useChannelEase ? moveEase : ease;
        public UITweenEase FadeEase => useChannelEase ? fadeEase : ease;
        public UITweenEase ScaleEase => useChannelEase ? scaleEase : ease;

        /// <summary>Direction/Distance/Custom Offset을 기준 위치로부터의 오프셋(px)으로 환산한다.</summary>
        public Vector2 ResolveOffset()
        {
            switch (direction)
            {
                case UITweenDirection.Left:
                    return new Vector2(-distance, 0f);
                case UITweenDirection.Right:
                    return new Vector2(distance, 0f);
                case UITweenDirection.Up:
                    return new Vector2(0f, distance);
                case UITweenDirection.Down:
                    return new Vector2(0f, -distance);
                case UITweenDirection.Custom:
                    return customOffset;
                default:
                    return Vector2.zero;
            }
        }
    }
}

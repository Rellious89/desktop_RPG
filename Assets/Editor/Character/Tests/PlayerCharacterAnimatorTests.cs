using System;
using System.Reflection;
using Character;
using Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace CharacterEditor.Tests
{
    public sealed class PlayerCharacterAnimatorTests
    {
        private GameObject host;
        private PlayerCharacterAnimator animator;
        private AttackMotionDefinition motion;
        private CharacterMotionProfile profile;
        private Texture2D texture;
        private Sprite sprite;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            host = new GameObject("PlayerCharacterAnimatorTests");
            host.SetActive(false);
            host.AddComponent<SpriteRenderer>();
            FlashOnCue flash = host.AddComponent<FlashOnCue>();
            host.AddComponent<HitEffectSpawner>();
            animator = host.AddComponent<PlayerCharacterAnimator>();

            // EditMode의 비활성 GameObject에서는 MonoBehaviour.Awake가 자동 실행되지 않는다. Strike가
            // 테스트 대상에 도달하기 전에 FlashOnCue 내부 렌더러 null로 실패하지 않도록 수명 주기를
            // 명시적으로 준비한다(제품 코드의 초기화 규칙은 바꾸지 않는다).
            MethodInfo flashAwake = typeof(FlashOnCue).GetMethod(
                "Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(flashAwake);
            flashAwake.Invoke(flash, null);

            texture = new Texture2D(4, 4);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            profile = ScriptableObject.CreateInstance<CharacterMotionProfile>();
            SerializedObject serializedProfile = new SerializedObject(profile);
            SerializedProperty frames = serializedProfile.FindProperty("baseIdle").FindPropertyRelative("frames");
            frames.arraySize = 1;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = sprite;
            serializedProfile.ApplyModifiedPropertiesWithoutUndo();

            motion = ScriptableObject.CreateInstance<AttackMotionDefinition>();
            SetField("motionProfile", profile);
            Invoke("EnsureInitialized");
            SetField("activeMotion", motion);
            SetAttackPhase("Windup");
            host.SetActive(true);
            SetField("activeMotion", motion);
            SetAttackPhase("Windup");
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (motion != null) UnityEngine.Object.DestroyImmediate(motion);
            if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
            if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        }

        [Test]
        public void Strike_StopsWhenHitPointDisablesTheAnimator()
        {
            Action<AttackHitCue> handler = _ => animator.enabled = false;
            PlayerCharacterAnimator.HitPoint += handler;
            try
            {
                Assert.DoesNotThrow(() => Invoke("Strike"));
            }
            finally
            {
                PlayerCharacterAnimator.HitPoint -= handler;
            }

            Assert.IsFalse(animator.isActiveAndEnabled);
            Assert.AreNotEqual("Recovery", AttackPhaseName(), "비활성화 뒤 Recovery를 다시 열면 안 된다.");
        }

        [Test]
        public void Strike_StopsWhenHitPointCancelsTheAttack()
        {
            Action<AttackHitCue> handler = _ => animator.SetCombatEnabled(false);
            PlayerCharacterAnimator.HitPoint += handler;
            try
            {
                Invoke("Strike");
            }
            finally
            {
                PlayerCharacterAnimator.HitPoint -= handler;
            }

            Assert.AreEqual("None", AttackPhaseName(), "취소된 공격에 StartRecovery가 다시 실행되면 안 된다.");
        }

        [Test]
        public void Strike_WithoutReentrantChangeContinuesToRecovery()
        {
            Invoke("Strike");

            Assert.AreEqual("Recovery", AttackPhaseName());
        }

        [Test]
        public void Strike_EmitsHitPointExactlyOnce()
        {
            int count = 0;
            Action<AttackHitCue> handler = _ => count++;
            PlayerCharacterAnimator.HitPoint += handler;
            try
            {
                Invoke("Strike");
            }
            finally
            {
                PlayerCharacterAnimator.HitPoint -= handler;
            }

            Assert.AreEqual(1, count, "스킬도 이 공통 Strike 경로를 쓰므로 HitPoint가 추가로 생기면 안 된다.");
        }

        [Test]
        public void BeginAttackSession_WithoutSkillRuntimeKeepsTheExistingBasicAttackPath()
        {
            MakeMotionPlayable();
            AddToResolvedTier1();
            SetField("activeMotion", null);
            SetAttackPhase("None");

            Invoke("BeginAttackSession");

            Assert.AreSame(motion, GetField("activeMotion"));
            Assert.AreEqual("Windup", AttackPhaseName());
            Assert.AreEqual(1, GetField("pendingAttacks"));
        }

        [Test]
        public void BeginAttackSession_DoesNotRestartAfterAttackStartedCancelsIt()
        {
            MakeMotionPlayable();
            AddToResolvedTier1();
            SetField("activeMotion", null);
            SetAttackPhase("None");

            Action handler = () => animator.SetCombatEnabled(false);
            PlayerCharacterAnimator.AttackStarted += handler;
            try
            {
                Invoke("BeginAttackSession");
            }
            finally
            {
                PlayerCharacterAnimator.AttackStarted -= handler;
                animator.SetCombatEnabled(true);
            }

            Assert.AreEqual("None", AttackPhaseName());
            Assert.IsNull(GetField("activeMotion"), "취소 뒤 선택해 둔 모션을 다시 시작하면 안 된다.");
        }

        private void Invoke(string name)
        {
            MethodInfo method = typeof(PlayerCharacterAnimator).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, name);
            method.Invoke(animator, null);
        }

        private void SetField(string name, object value)
        {
            FieldInfo field = typeof(PlayerCharacterAnimator).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, name);
            field.SetValue(animator, value);
        }

        private object GetField(string name)
        {
            FieldInfo field = typeof(PlayerCharacterAnimator).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, name);
            return field.GetValue(animator);
        }

        private void MakeMotionPlayable()
        {
            var serialized = new SerializedObject(motion);
            SerializedProperty frames = serialized.FindProperty("frames");
            frames.arraySize = 2;
            frames.GetArrayElementAtIndex(0).objectReferenceValue = sprite;
            frames.GetArrayElementAtIndex(1).objectReferenceValue = sprite;
            serialized.FindProperty("hitFrameIndex").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void AddToResolvedTier1()
        {
            var pool = (System.Collections.IList)GetField("resolvedTier1");
            pool.Clear();
            pool.Add(motion);
        }

        private void SetAttackPhase(string name)
        {
            FieldInfo field = typeof(PlayerCharacterAnimator).GetField("attackPhase", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(animator, Enum.Parse(field.FieldType, name));
        }

        private string AttackPhaseName()
        {
            FieldInfo field = typeof(PlayerCharacterAnimator).GetField("attackPhase", BindingFlags.NonPublic | BindingFlags.Instance);
            return field.GetValue(animator).ToString();
        }
    }
}

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
            host.AddComponent<FlashOnCue>();
            host.AddComponent<HitEffectSpawner>();
            animator = host.AddComponent<PlayerCharacterAnimator>();

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

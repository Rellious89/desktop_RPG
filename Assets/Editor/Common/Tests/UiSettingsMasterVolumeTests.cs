using Common;
using NUnit.Framework;
using UnityEngine;

namespace CommonEditor.Tests
{
    public sealed class UiSettingsMasterVolumeTests
    {
        [TestCase(false, 0f)]
        [TestCase(true, 1f)]
        public void LegacySfxEnabled_MigratesOnlyWhenMasterVolumeIsMissing(bool enabled, float expected)
        {
            string json = "{\"sfxEnabled\":" + (enabled ? "true" : "false") +
                          ",\"hudVisible\":false,\"sizeScale\":1.5}";

            UiSettingsData data = UiSettingsSaveSystem.Deserialize(json);

            Assert.That(data.masterVolume, Is.EqualTo(expected));
            Assert.That(data.lastNonZeroMasterVolume, Is.EqualTo(1f));
            Assert.That(data.hudVisible, Is.False);
            Assert.That(data.sizeScale, Is.EqualTo(1.5f));
        }

        [Test]
        public void ExplicitMasterVolume_WinsOverLegacyMuteAndRoundTrips()
        {
            const string json = "{\"sfxEnabled\":false,\"masterVolume\":0.35," +
                                "\"lastNonZeroMasterVolume\":0.35,\"hudVisible\":false,\"sizeScale\":1.5}";

            UiSettingsData data = UiSettingsSaveSystem.Deserialize(json);
            UiSettingsData roundTripped = UiSettingsSaveSystem.Deserialize(JsonUtility.ToJson(data));

            Assert.That(roundTripped.masterVolume, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(roundTripped.lastNonZeroMasterVolume, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(roundTripped.hudVisible, Is.False);
            Assert.That(roundTripped.sizeScale, Is.EqualTo(1.5f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChangingVolumeAndScale_InEitherOrder_PreservesBothOptions(bool scaleFirst)
        {
            UiSettingsData data = new UiSettingsData { hudVisible = false };

            if (scaleFirst)
            {
                data = UiSettingsSaveSystem.UpdateSizeScale(data, 1.5f);
                data = UiSettingsSaveSystem.Deserialize(JsonUtility.ToJson(data));
                data = UiSettingsSaveSystem.UpdateMasterVolume(data, 0.35f, 0.35f);
            }
            else
            {
                data = UiSettingsSaveSystem.UpdateMasterVolume(data, 0.35f, 0.35f);
                data = UiSettingsSaveSystem.Deserialize(JsonUtility.ToJson(data));
                data = UiSettingsSaveSystem.UpdateSizeScale(data, 1.5f);
            }

            UiSettingsData restored = UiSettingsSaveSystem.Deserialize(JsonUtility.ToJson(data));
            Assert.That(restored.masterVolume, Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(restored.sizeScale, Is.EqualTo(1.5f));
            Assert.That(restored.hudVisible, Is.False);
        }

        [Test]
        public void LegacyUiSettings_DefaultToOpaque()
        {
            UiSettingsData restored = UiSettingsSaveSystem.Deserialize(
                "{\"masterVolume\":0.35,\"hudVisible\":false}");

            Assert.That(restored.uiOpacity, Is.EqualTo(1f));
            Assert.That(restored.masterVolume, Is.EqualTo(0.35f));
            Assert.That(restored.hudVisible, Is.False);
        }

        [Test]
        public void UpdatingOpacity_RoundTripsWithoutChangingOtherSettings()
        {
            UiSettingsData data = new UiSettingsData
            {
                masterVolume = 0.4f,
                hudVisible = false,
                sizeScale = 1.5f
            };

            data = UiSettingsSaveSystem.UpdateUiOpacity(data, 0.3f);
            data = UiSettingsSaveSystem.UpdateMasterVolume(data, 0.6f, 0.6f);
            data = UiSettingsSaveSystem.UpdateSizeScale(data, 1.5f);
            UiSettingsData restored = UiSettingsSaveSystem.Deserialize(JsonUtility.ToJson(data));
            Assert.That(restored.uiOpacity, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(restored.masterVolume, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(restored.sizeScale, Is.EqualTo(1.5f));
            Assert.That(restored.hudVisible, Is.False);
        }
    }
}

using System;

namespace Corruption
{
    /// <summary>
    /// 저장된 오염도를 전투 행동력 비용으로 읽는 순수 정책이다. 이 정책은 상태를 만들거나 저장하지
    /// 않으며, 잘못된 설정은 기존 기본 비용(1배)으로 안전하게 되돌린다.
    /// </summary>
    public static class CorruptionStaminaCostPolicy
    {
        public static int Calculate(
            double currentCorruption,
            int baseCorruption,
            CorruptionConfigDefinition config,
            int baseStaminaCost)
        {
            if (baseStaminaCost <= 0) return 0;

            int multiplier = ResolveMultiplier(currentCorruption, baseCorruption, config);
            return SaturatingMultiply(baseStaminaCost, multiplier);
        }

        private static int ResolveMultiplier(
            double currentCorruption,
            int baseCorruption,
            CorruptionConfigDefinition config)
        {
            if (config == null || !config.IsValid) return 1;

            double normalizedCurrent = IsFinite(currentCorruption) && currentCorruption > 0d
                ? currentCorruption
                : 0d;
            double effectiveCorruption = Math.Max(normalizedCurrent, Math.Max(0, baseCorruption));
            effectiveCorruption = Math.Min(effectiveCorruption, config.MaxCorruption);
            double percent = effectiveCorruption / config.MaxCorruption * 100d;

            if (percent >= config.DangerThresholdPercent)
                return config.DangerStaminaCostMultiplier;
            if (percent >= config.WarningThresholdPercent)
                return config.WarningStaminaCostMultiplier;
            return 1;
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static int SaturatingMultiply(int value, int multiplier)
        {
            if (multiplier <= 1) return value;
            return value > int.MaxValue / multiplier ? int.MaxValue : value * multiplier;
        }
    }
}

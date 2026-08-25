using UnityEngine;

/// <summary>
/// 단계별 스탯 성장값을 실제 Entity에 적용할 하나의 보정값으로 변환합니다.
/// 플레이어 레벨과 적 스테이지가 동일한 계산 규칙을 공유하도록 계산 책임을 분리했습니다.
/// </summary>
public static class EntityStatModifierCalculator
{
    /// <summary>
    /// 고정값은 단계 수만큼 선형으로 더하고, 비율은 설정에 따라 선형 또는 복리로 계산합니다.
    /// 복리 계산 시 단계당 10% 성장, 2단계는 총 21% 성장으로 변환됩니다.
    /// </summary>
    public static EntityStatModifier ScalePerStep(
        EntityStatModifier growthPerStep,
        int stepCount,
        bool useCompoundRate
    )
    {
        int validatedStepCount = Mathf.Max(0, stepCount);

        return new EntityStatModifier
        {
            BonusHp = growthPerStep.BonusHp * validatedStepCount,
            BonusHpRecovery = growthPerStep.BonusHpRecovery * validatedStepCount,
            BonusDamageReduction = growthPerStep.BonusDamageReduction * validatedStepCount,
            BonusAttackPower = growthPerStep.BonusAttackPower * validatedStepCount,
            BonusAttackSpeed = growthPerStep.BonusAttackSpeed * validatedStepCount,
            BonusCriticalPercentage = growthPerStep.BonusCriticalPercentage * validatedStepCount,
            BonusHpRate = CalculateRate(
                growthPerStep.BonusHpRate,
                validatedStepCount,
                useCompoundRate
            ),
            BonusHpRecoveryRate = CalculateRate(
                growthPerStep.BonusHpRecoveryRate,
                validatedStepCount,
                useCompoundRate
            ),
            BonusDamageReductionRate = CalculateRate(
                growthPerStep.BonusDamageReductionRate,
                validatedStepCount,
                useCompoundRate
            ),
            BonusAttackPowerRate = CalculateRate(
                growthPerStep.BonusAttackPowerRate,
                validatedStepCount,
                useCompoundRate
            ),
            BonusAttackSpeedRate = CalculateRate(
                growthPerStep.BonusAttackSpeedRate,
                validatedStepCount,
                useCompoundRate
            ),
            BonusCriticalPercentageRate = CalculateRate(
                growthPerStep.BonusCriticalPercentageRate,
                validatedStepCount,
                useCompoundRate
            ),
        };
    }

    private static float CalculateRate(float ratePerStep, int stepCount, bool useCompoundRate)
    {
        if (stepCount <= 0 || Mathf.Approximately(ratePerStep, 0f))
            return 0f;

        if (!useCompoundRate)
            return ratePerStep * stepCount;

        return Mathf.Pow(1f + ratePerStep, stepCount) - 1f;
    }
}

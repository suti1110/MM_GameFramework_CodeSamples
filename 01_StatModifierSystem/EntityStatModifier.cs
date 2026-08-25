[System.Serializable]
public readonly struct EntityStatModifierHandle : System.IEquatable<EntityStatModifierHandle>
{
    internal EntityStatModifierHandle(int id)
    {
        Id = id;
    }

    internal int Id { get; }
    public bool IsValid => Id != 0;

    public bool Equals(EntityStatModifierHandle other) => Id == other.Id;

    public override bool Equals(object obj) =>
        obj is EntityStatModifierHandle other && Equals(other);

    public override int GetHashCode() => Id;

    public static bool operator ==(EntityStatModifierHandle left, EntityStatModifierHandle right) =>
        left.Equals(right);

    public static bool operator !=(EntityStatModifierHandle left, EntityStatModifierHandle right) =>
        !left.Equals(right);
}

[System.Serializable]
public struct EntityStatModifier
{
    [UnityEngine.Header("고정값 보정")]
    public float BonusHp;
    public float BonusHpRecovery;
    public float BonusDamageReduction;
    public float BonusAttackPower;
    public float BonusAttackSpeed;
    public float BonusCriticalPercentage;
    public float BonusCriticalDamageRate;
    public float BonusAttackRangeRate;
    public float BonusMoveSpeedRate;
    public float BonusOutgoingDamageRate;

    [UnityEngine.Header("비율 보정 (0.2 = 20%)")]
    public float BonusHpRate;
    public float BonusHpRecoveryRate;
    public float BonusDamageReductionRate;
    public float BonusAttackPowerRate;
    public float BonusAttackSpeedRate;
    public float BonusCriticalPercentageRate;

    [UnityEngine.Header("기타 보정")]
    public int BonusBasicAttackTargetCount;

    public static EntityStatModifier operator +(EntityStatModifier a, EntityStatModifier b)
    {
        return new EntityStatModifier
        {
            BonusHp = a.BonusHp + b.BonusHp,
            BonusHpRecovery = a.BonusHpRecovery + b.BonusHpRecovery,
            BonusDamageReduction = a.BonusDamageReduction + b.BonusDamageReduction,
            BonusAttackPower = a.BonusAttackPower + b.BonusAttackPower,
            BonusAttackSpeed = a.BonusAttackSpeed + b.BonusAttackSpeed,
            BonusCriticalPercentage = a.BonusCriticalPercentage + b.BonusCriticalPercentage,
            BonusCriticalDamageRate = a.BonusCriticalDamageRate + b.BonusCriticalDamageRate,
            BonusAttackRangeRate = a.BonusAttackRangeRate + b.BonusAttackRangeRate,
            BonusMoveSpeedRate = a.BonusMoveSpeedRate + b.BonusMoveSpeedRate,
            BonusOutgoingDamageRate = a.BonusOutgoingDamageRate + b.BonusOutgoingDamageRate,
            BonusHpRate = a.BonusHpRate + b.BonusHpRate,
            BonusHpRecoveryRate = a.BonusHpRecoveryRate + b.BonusHpRecoveryRate,
            BonusDamageReductionRate = a.BonusDamageReductionRate + b.BonusDamageReductionRate,
            BonusAttackPowerRate = a.BonusAttackPowerRate + b.BonusAttackPowerRate,
            BonusAttackSpeedRate = a.BonusAttackSpeedRate + b.BonusAttackSpeedRate,
            BonusCriticalPercentageRate =
                a.BonusCriticalPercentageRate + b.BonusCriticalPercentageRate,
            BonusBasicAttackTargetCount =
                a.BonusBasicAttackTargetCount + b.BonusBasicAttackTargetCount,
        };
    }

    public static EntityStatModifier Zero => default;
}

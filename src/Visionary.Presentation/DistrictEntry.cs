namespace Visionary.Presentation;

public enum DistrictEntryKind
{
    Household = 1,
    Window = 2,
}

/// <summary>居る区画の入口(GDD05 §2.2)。Window なら <paramref name="HouseholdId"/> = −1。</summary>
public readonly record struct DistrictEntry(DistrictEntryKind Kind, int HouseholdId, string Label);

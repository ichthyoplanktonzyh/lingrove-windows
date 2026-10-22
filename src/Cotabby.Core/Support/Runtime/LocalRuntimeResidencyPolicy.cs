namespace Cotabby.Core.Support.Runtime;

/// <summary>
/// The Mac resource vocabulary separates permission to generate from keeping local memory alive.
/// Endpoint work can remain eligible while its old local allocation is released.
/// </summary>
public enum LocalRuntimeResidency { Released, Warm, OnDemand }

/// <summary>
/// Pure port of LocalRuntimeResidencyPolicy.residency at the pinned Mac revision. Each task supplies
/// its own eligibility; this rule owns neither settings nor native pointers. Warmup and eviction
/// effects remain with that task's runtime/controller.
/// </summary>
public static class LocalRuntimeResidencyPolicy
{
    public static LocalRuntimeResidency Residency(bool usesLocalModel, bool enabled, bool paused, bool keepWarm) =>
        !usesLocalModel || !enabled || paused
            ? LocalRuntimeResidency.Released
            : keepWarm ? LocalRuntimeResidency.Warm : LocalRuntimeResidency.OnDemand;
}

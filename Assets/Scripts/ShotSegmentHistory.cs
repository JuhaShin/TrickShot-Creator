using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct ShotSegment
{
    public string StartToolType;
    public string EndTarget;
    public float DurationSeconds;

    public ShotSegment(string startToolType, string endTarget, float durationSeconds)
    {
        StartToolType = startToolType;
        EndTarget = endTarget;
        DurationSeconds = durationSeconds;
    }
}

public static class ShotSegmentHistory
{
    internal const int Capacity = 4;
    private static readonly Queue<ShotSegment> recent = new(Capacity);

    internal static void Append(Queue<ShotSegment> segments, ShotSegment segment)
    {
        if (segments.Count == Capacity) segments.Dequeue();
        segments.Enqueue(segment);
    }

    internal static void Save(IEnumerable<ShotSegment> segments)
    {
        foreach (ShotSegment segment in segments) Append(recent, segment);
    }

    public static ShotSegment[] GetRecentSegments() => recent.ToArray();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetSession() => recent.Clear();
}

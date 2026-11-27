using System;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
internal struct SavedTool
{
    public bool pan;
    public Pose pose;
}

[Serializable]
internal sealed class StudioField
{
    public string scenePath;
    public string stageId;
    public bool hasSimulationView, centeredGoalPose;
    public Pose simulationView;
    public Vector3 simulationPlayerPosition;
    public Pose ballPose, goalPose;
    public bool hasGoal;
    public SavedTool[] tools;
}

[Serializable]
internal sealed class ProgressSaveData
{
    public int version = 1, day = 1, lastDay;
    public string scene = "OpeningScene", pendingId, settledId;
    public long subscribers = 10, views, newSubscribers, newViews;
    public bool unread, hasScore;
    public long scoreBefore, originalViews;
    public double score, pc, camera, mic, expansion, viewRate, conversionRate;
    public int scoreSimilar;
    public StudioField draft, uploaded;
    public string[] toolOrder;
    public ShotSegment[] segments;
    public int objects, types, bounces, similar;
    public double seconds;
}

internal static class ProgressSave
{
    internal static ProgressSaveData Data { get; private set; }
    private static string PathName => Path.Combine(Application.persistentDataPath, "progress.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Data = null;

    internal static bool Exists => File.Exists(PathName) || File.Exists(PathName + ".bak");

    internal static ProgressSaveData Read()
    {
        foreach (string name in new[] { PathName, PathName + ".bak" })
        {
            if (!File.Exists(name)) continue;
            try
            {
                var value = JsonUtility.FromJson<ProgressSaveData>(File.ReadAllText(name));
                if (value != null && value.version == 1 && value.day >= 1 && value.lastDay == value.day - 1 &&
                    value.subscribers >= 0 && value.views >= 0 &&
                    (value.scene == "OpeningScene" || value.scene == "StudioScene" || value.scene == "MainScene"))
                    return value;
            }
            catch (Exception e) { Debug.LogWarning("Could not read game progress: " + e.Message); }
        }
        return null;
    }

    internal static void Resume(ProgressSaveData data) => Data = data;

    internal static bool Write(ProgressSaveData value, bool fresh = false)
    {
        try
        {
            string path = PathName;
            Directory.CreateDirectory(Application.persistentDataPath);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(value));
            using (var file = new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
            if (fresh && File.Exists(path + ".bak")) File.Delete(path + ".bak");
            if (File.Exists(path)) File.Replace(path + ".tmp", path, fresh ? null : path + ".bak");
            else File.Move(path + ".tmp", path);
            Data = value;
            return true;
        }
        catch (Exception e) { Debug.LogError("Could not save game progress: " + e.Message); return false; }
    }

    internal static bool Enter(string scene)
    {
        if (Data == null) return true;
        Data.scene = scene;
        return Write(Data);
    }
}

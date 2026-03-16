using System.Collections.Generic;
using UnityEngine;

namespace ReplayTimerMod
{
    public struct FrameData
    {
        public float x;
        public float y;
        public bool facingRight;

        // Animation state - populated at record time, empty string when unavailable.
        public string animClip;   // tk2dSpriteAnimationClip.name
        public int animFrame;     // integer frame index within that clip
    }

    // Uniquely identifies a route through a room.
    // EntryFromScene (not gate name) is the entry discriminator - the same gate
    // name can appear on both sides of a boundary, but the source scene is unique.
    public readonly struct RoomKey
    {
        public readonly string SceneName;
        public readonly string EntryFromScene;
        public readonly string ExitToScene;

        public RoomKey(string sceneName, string entryFromScene, string exitToScene)
        {
            SceneName = sceneName;
            EntryFromScene = entryFromScene;
            ExitToScene = exitToScene;
        }

        public override string ToString() =>
            $"{SceneName}[{EntryFromScene}→{ExitToScene}]";

        public override bool Equals(object? obj) =>
            obj is RoomKey other &&
            SceneName == other.SceneName &&
            EntryFromScene == other.EntryFromScene &&
            ExitToScene == other.ExitToScene;

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (SceneName?.GetHashCode() ?? 0);
                hash = hash * 31 + (EntryFromScene?.GetHashCode() ?? 0);
                hash = hash * 31 + (ExitToScene?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }

    public static class TimeUtil
    {
        public static string Format(float t)
        {
            int ms = (int)(t * 100) % 100;
            int s = (int)t % 60;
            int min = (int)t / 60;
            return $"{min}:{s:00}.{ms:00}";
        }
    }

    public class RecordedRoom
    {
        public RoomKey Key { get; }
        public float TotalTime { get; }
        public FrameData[] Frames { get; }
        public int FrameCount => Frames.Length;

        public RecordedRoom(RoomKey key, float totalTime, FrameData[] frames)
        {
            Key = key;
            TotalTime = totalTime;
            Frames = frames;
        }
    }

    public sealed class ReplaySnapshot
    {
        public string SnapshotId { get; }
        public long CapturedAtUtcTicks { get; }
        public RecordedRoom Room { get; }
        public RoomKey Key => Room.Key;
        public float TotalTime => Room.TotalTime;
        public string EncodedData { get; }
        public bool HasCapturedAt => CapturedAtUtcTicks > 0;
        public bool HasVisualOverride { get; }
        public float ColorR { get; }
        public float ColorG { get; }
        public float ColorB { get; }
        public float Alpha { get; }
        public Color OverrideColor => new Color(ColorR, ColorG, ColorB, Alpha);

        public ReplaySnapshot(string snapshotId, long capturedAtUtcTicks,
            RecordedRoom room, string? encodedData = null,
            bool hasVisualOverride = false,
            float colorR = 1f, float colorG = 1f, float colorB = 1f, float alpha = 0.4f)
        {
            SnapshotId = string.IsNullOrWhiteSpace(snapshotId)
                ? System.Guid.NewGuid().ToString("N")
                : snapshotId;
            CapturedAtUtcTicks = capturedAtUtcTicks;
            Room = room;
            EncodedData = encodedData ?? ReplayShareEncoder.Encode(room);
            HasVisualOverride = hasVisualOverride;
            ColorR = Mathf.Clamp01(colorR);
            ColorG = Mathf.Clamp01(colorG);
            ColorB = Mathf.Clamp01(colorB);
            Alpha = Mathf.Clamp01(alpha);
        }

        public Color ResolveGhostColor(Color globalColor) =>
            HasVisualOverride
                ? OverrideColor
                : new Color(globalColor.r, globalColor.g, globalColor.b, globalColor.a);

        public ReplaySnapshot WithVisualOverride(bool hasVisualOverride, Color color) =>
            new ReplaySnapshot(
                SnapshotId,
                CapturedAtUtcTicks,
                Room,
                EncodedData,
                hasVisualOverride,
                color.r,
                color.g,
                color.b,
                color.a);

        public static ReplaySnapshot CreateNew(RecordedRoom room,
            string? encodedData = null, long? capturedAtUtcTicks = null) =>
            new ReplaySnapshot(
                System.Guid.NewGuid().ToString("N"),
                capturedAtUtcTicks ?? System.DateTime.UtcNow.Ticks,
                room,
                encodedData);
    }

    public sealed class RouteReplayHistory
    {
        public RoomKey Key { get; }
        public IReadOnlyList<ReplaySnapshot> Snapshots { get; }
        public ReplaySnapshot Current { get; }
        public int Count => Snapshots.Count;

        public RouteReplayHistory(RoomKey key, IReadOnlyList<ReplaySnapshot> snapshots,
            ReplaySnapshot current)
        {
            Key = key;
            Snapshots = snapshots;
            Current = current;
        }
    }
}
using System.Numerics;

namespace Burger.Core.Figures;

/// <summary>One joint of a <see cref="Rig"/>: its parent (-1 for a root) and where it rests relative to that parent.</summary>
public sealed record Joint(string Name, int Parent, Vector3 Position, Quaternion Rotation);

/// <summary>The position and rotation of every joint relative to its parent. Joints do not scale.</summary>
public sealed class Pose
{
    public readonly Vector3[] Position;
    public readonly Quaternion[] Rotation;

    public Pose(int joints)
    {
        Position = new Vector3[joints];
        Rotation = new Quaternion[joints];
        Array.Fill(Rotation, Quaternion.Identity);
    }

    public void CopyFrom(Pose other)
    {
        other.Position.CopyTo(Position, 0);
        other.Rotation.CopyTo(Rotation, 0);
    }

    /// <summary>Moves this pose towards <paramref name="other"/> by <paramref name="t"/> (0 keeps this pose, 1 gives the other).</summary>
    public void BlendTowards(Pose other, float t)
    {
        for (int i = 0; i < Position.Length; i++)
        {
            Position[i] = Vector3.Lerp(Position[i], other.Position[i], t);
            Rotation[i] = Quaternion.Slerp(Rotation[i], other.Rotation[i], t);
        }
    }
}

/// <summary>
/// A skeleton: joints in parent-before-child order with their rest pose. Space is glTF's: metres, +Y up, the figure faces +Z
/// and its left side is +X. Shared by every figure that uses the same body, so one set of clips animates all of them.
/// </summary>
public sealed class Rig
{
    /// <summary>The most joints a rig may have (the size of the bone palette the renderer uploads).</summary>
    public const int MaxJoints = 64;

    public IReadOnlyList<Joint> Joints { get; }
    /// <summary>Per joint: takes a vertex from model space into the joint's rest space.</summary>
    public Matrix4x4[] InverseBind { get; }
    private readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly Pose _rest;

    public Rig(IReadOnlyList<Joint> joints, Matrix4x4[]? inverseBind = null)
    {
        if (joints.Count == 0 || joints.Count > MaxJoints) throw new ArgumentException($"A rig needs 1 to {MaxJoints} joints, got {joints.Count}.");
        Joints = joints;
        _rest = new Pose(joints.Count);
        for (int i = 0; i < joints.Count; i++)
        {
            if (joints[i].Parent >= i) throw new ArgumentException($"Joint '{joints[i].Name}' is listed before its parent.");
            _index[joints[i].Name] = i;
            _rest.Position[i] = joints[i].Position;
            _rest.Rotation[i] = joints[i].Rotation;
        }
        if (inverseBind != null && inverseBind.Length == joints.Count) InverseBind = inverseBind;
        else
        {
            InverseBind = new Matrix4x4[joints.Count];
            var world = new Matrix4x4[joints.Count];
            WorldTransforms(_rest, world);
            for (int i = 0; i < world.Length; i++) Matrix4x4.Invert(world[i], out InverseBind[i]);
        }
    }

    public int Count => Joints.Count;

    /// <summary>Index of a joint by name, or -1.</summary>
    public int Find(string name) => _index.TryGetValue(name, out int i) ? i : -1;

    /// <summary>A new pose with every joint at rest.</summary>
    public Pose RestPose()
    {
        var p = new Pose(Count);
        p.CopyFrom(_rest);
        return p;
    }

    /// <summary>Resets a pose to rest in place.</summary>
    public void ResetPose(Pose pose) => pose.CopyFrom(_rest);

    /// <summary>Model-space transform of every joint for a pose.</summary>
    public void WorldTransforms(Pose pose, Matrix4x4[] world)
    {
        for (int i = 0; i < Count; i++)
        {
            var local = Matrix4x4.CreateFromQuaternion(pose.Rotation[i]);
            local.Translation = pose.Position[i];
            int parent = Joints[i].Parent;
            world[i] = parent < 0 ? local : local * world[parent];
        }
    }

    /// <summary>The matrices that move a rest-pose vertex to where the pose puts it (one per joint, blended by the skin weights).</summary>
    public void SkinMatrices(Pose pose, Matrix4x4[] skin)
    {
        WorldTransforms(pose, skin);
        for (int i = 0; i < Count; i++) skin[i] = InverseBind[i] * skin[i];
    }
}

/// <summary>Keyframes of one joint. Either channel may be missing; a missing channel leaves the joint as the pose had it.</summary>
public sealed class JointTrack
{
    public float[]? RotationTimes;
    public Quaternion[]? Rotations;
    public float[]? PositionTimes;
    public Vector3[]? Positions;
}

/// <summary>Something a clip announces at a moment of its loop (a shot, a footstep): an effect name and the attachment (a point on the figure) it belongs at.</summary>
public readonly record struct ClipEvent(float Time, string Effect, string Attach);

/// <summary>A named animation of a <see cref="Rig"/>: keyframed joint rotations (and positions) over <see cref="Duration"/> seconds.</summary>
public sealed class Clip
{
    public string Name { get; }
    public float Duration { get; }
    public bool Loop { get; set; } = true;
    /// <summary>
    /// Ground speed in metres per second the cycle is made for (0 for clips that stay in place). Move the figure at this
    /// speed times its scale and the feet do not slide.
    /// </summary>
    public float Speed { get; set; }
    /// <summary>One entry per joint of the rig; null where the clip does not touch the joint.</summary>
    public JointTrack?[] Tracks { get; }
    /// <summary>Events in order of time, raised by <see cref="Animator"/> as playback passes them.</summary>
    public ClipEvent[] Events { get; set; } = Array.Empty<ClipEvent>();

    public Clip(string name, float duration, JointTrack?[] tracks)
    {
        Name = name;
        Duration = Math.Max(duration, 1e-4f);
        Tracks = tracks;
    }

    /// <summary>Writes the pose at <paramref name="time"/> seconds over <paramref name="pose"/> (start from the rest pose).</summary>
    public void Sample(float time, Pose pose)
    {
        time = Loop ? time - MathF.Floor(time / Duration) * Duration : Math.Clamp(time, 0, Duration);
        for (int j = 0; j < Tracks.Length && j < pose.Rotation.Length; j++)
        {
            var t = Tracks[j];
            if (t == null) continue;
            if (t.Rotations is { Length: > 0 } && t.RotationTimes != null)
            {
                var (a, b, f) = Locate(t.RotationTimes, time);
                pose.Rotation[j] = a == b ? t.Rotations[a] : Quaternion.Slerp(t.Rotations[a], t.Rotations[b], f);
            }
            if (t.Positions is { Length: > 0 } && t.PositionTimes != null)
            {
                var (a, b, f) = Locate(t.PositionTimes, time);
                pose.Position[j] = a == b ? t.Positions[a] : Vector3.Lerp(t.Positions[a], t.Positions[b], f);
            }
        }
    }

    private static (int A, int B, float F) Locate(float[] times, float time)
    {
        int n = times.Length;
        if (n == 1 || time <= times[0]) return (0, 0, 0);
        if (time >= times[n - 1]) return (n - 1, n - 1, 0);
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (times[mid] <= time) lo = mid; else hi = mid;
        }
        float span = times[hi] - times[lo];
        return (lo, hi, span > 1e-9f ? (time - times[lo]) / span : 0);
    }
}

/// <summary>
/// Plays the clips of a <see cref="FigureModel"/> for one figure: keeps the time, cross-fades when the clip changes and
/// produces the skin matrices to draw with. Walk and run stay in step with each other while they fade.
/// </summary>
public sealed class Animator
{
    public FigureModel Model { get; }
    /// <summary>Playback speed (1 = as authored).</summary>
    public float Rate = 1f;
    public Clip? Current { get; private set; }
    public float Time { get; set; }
    /// <summary>Called for each <see cref="ClipEvent"/> of the current clip that playback passes in <see cref="Advance"/>.</summary>
    public Action<ClipEvent>? OnEvent;

    private Clip? _from;
    private float _fromTime, _fade, _fadeLength;
    private readonly Pose _pose, _fromPose;
    private readonly Matrix4x4[] _skin;

    public Animator(FigureModel model, string? clip = null)
    {
        Model = model;
        _pose = model.Rig.RestPose();
        _fromPose = model.Rig.RestPose();
        _skin = new Matrix4x4[model.Rig.Count];
        if (clip != null) Play(clip, 0);
    }

    /// <summary>Switches to a clip, blending over <paramref name="fade"/> seconds. Does nothing if it is already playing. False if the model has no such clip.</summary>
    public bool Play(string clip, float fade = 0.25f)
    {
        if (!Model.Clips.TryGetValue(clip, out var next)) return false;
        if (next == Current) return true;
        if (Current != null && fade > 0)
        {
            _from = Current;
            _fromTime = Time;
            _fade = 0;
            _fadeLength = fade;
            // two locomotion cycles keep the same foot forward while they blend
            Time = Current.Speed > 0 && next.Speed > 0 ? Time / Current.Duration * next.Duration : 0;
        }
        else
        {
            _from = null;
            Time = 0;
        }
        Current = next;
        return true;
    }

    public void Advance(float dt)
    {
        if (Current == null) return;
        dt *= Rate;
        float before = Time;
        Time += dt;
        bool wrapped = false;
        if (Current.Loop) { wrapped = Time >= Current.Duration; Time -= MathF.Floor(Time / Current.Duration) * Current.Duration; }
        if (OnEvent != null && Current.Events.Length > 0 && dt > 0)
            foreach (var ev in Current.Events)
                if (wrapped ? ev.Time > before || ev.Time <= Time : ev.Time > before && ev.Time <= Time) OnEvent(ev);
        if (_from == null) return;
        _fromTime += Current.Speed > 0 && _from.Speed > 0 ? dt * _from.Duration / Current.Duration : dt;
        _fade += Math.Abs(dt);
        if (_fade >= _fadeLength) _from = null;
    }

    /// <summary>The pose right now (owned by the animator; valid until the next call).</summary>
    public Pose Evaluate()
    {
        Model.Rig.ResetPose(_pose);
        Current?.Sample(Time, _pose);
        if (_from != null)
        {
            Model.Rig.ResetPose(_fromPose);
            _from.Sample(_fromTime, _fromPose);
            float t = Math.Clamp(_fade / _fadeLength, 0, 1);
            t = t * t * (3 - 2 * t);
            _fromPose.BlendTowards(_pose, t);
            _pose.CopyFrom(_fromPose);
        }
        return _pose;
    }

    /// <summary>The skin matrices for the pose right now (owned by the animator; valid until the next call).</summary>
    public Matrix4x4[] Skin()
    {
        Model.Rig.SkinMatrices(Evaluate(), _skin);
        return _skin;
    }
}

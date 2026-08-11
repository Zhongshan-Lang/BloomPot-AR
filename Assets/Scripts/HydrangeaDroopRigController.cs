using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HydrangeaDroopRigController : MonoBehaviour
{
    private const string LeafBonePrefix = "DroopLeaf_";
    private const string HeadBonePrefix = "DroopHead_";

    [Header("Droop Rotation")]
    [SerializeField] private float _leafMaxDroopDegrees = -7f;
    [SerializeField] private float _headMaxDroopDegrees = -1.25f;
    [SerializeField, Min(0.01f)] private float _transitionDuration = 0.75f;

    private readonly List<BoneBinding> _leafBones = new List<BoneBinding>();
    private readonly List<BoneBinding> _headBones = new List<BoneBinding>();

    private float _currentDroop;
    private float _targetDroop;
    private float _leafStageScale = 1f;
    private float _headStageScale = 1f;
    private bool _initialized;
    private bool _hasLoggedMissingRig;

    public int LeafBoneCount => _leafBones.Count;
    public int HeadBoneCount => _headBones.Count;
    public float CurrentDroop => _currentDroop;
    public float LeafStageScale => _leafStageScale;
    public float HeadStageScale => _headStageScale;

    private void Awake()
    {
        Initialize();
    }

    private void LateUpdate()
    {
        if (!_initialized && !Initialize())
        {
            return;
        }

        if (!Mathf.Approximately(_currentDroop, _targetDroop))
        {
            var maxDelta = Time.deltaTime / Mathf.Max(0.01f, _transitionDuration);
            _currentDroop = Mathf.MoveTowards(_currentDroop, _targetDroop, maxDelta);
        }

        ApplyDroop(_currentDroop);
    }

    public bool Initialize()
    {
        if (_initialized)
        {
            return true;
        }

        _leafBones.Clear();
        _headBones.Clear();

        var transforms = GetComponentsInChildren<Transform>(true);
        Array.Sort(transforms, (left, right) => string.CompareOrdinal(left.name, right.name));
        foreach (var child in transforms)
        {
            if (child.name.StartsWith(LeafBonePrefix, StringComparison.Ordinal))
            {
                _leafBones.Add(new BoneBinding(child));
            }
            else if (child.name.StartsWith(HeadBonePrefix, StringComparison.Ordinal))
            {
                _headBones.Add(new BoneBinding(child));
            }
        }

        _initialized = _leafBones.Count > 0 || _headBones.Count > 0;
        if (!_initialized)
        {
            if (!_hasLoggedMissingRig)
            {
                Debug.LogWarning("HydrangeaDroopRigController: no DroopLeaf_ or DroopHead_ bones were found.", this);
                _hasLoggedMissingRig = true;
            }

            return false;
        }

        _hasLoggedMissingRig = false;
        ApplyDroop(_currentDroop);
        Debug.Log(
            $"HydrangeaDroopRigController: initialized {_leafBones.Count} leaf bones and {_headBones.Count} head bones.",
            this);
        return true;
    }

    public void SetDroop(float value01)
    {
        if (!_initialized && !Initialize())
        {
            return;
        }

        _currentDroop = Mathf.Clamp01(value01);
        _targetDroop = _currentDroop;
        ApplyDroop(_currentDroop);
    }

    public void SetDroopTarget(float value01)
    {
        if (!_initialized && !Initialize())
        {
            return;
        }

        _targetDroop = Mathf.Clamp01(value01);
    }

    public void RestoreFromDroop()
    {
        SetDroopTarget(0f);
    }

    public void SetStageScale(float leafScale, float headScale)
    {
        _leafStageScale = Mathf.Clamp01(leafScale);
        _headStageScale = Mathf.Clamp01(headScale);

        if (_initialized)
        {
            ApplyDroop(_currentDroop);
        }
    }

    public static bool ContainsDroopRig(Transform root)
    {
        if (root == null)
        {
            return false;
        }

        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith(LeafBonePrefix, StringComparison.Ordinal)
                || child.name.StartsWith(HeadBonePrefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyDroop(float droop01)
    {
        ApplyBoneRotations(_leafBones, _leafMaxDroopDegrees * droop01 * _leafStageScale);
        ApplyBoneRotations(_headBones, _headMaxDroopDegrees * droop01 * _headStageScale);
    }

    private static void ApplyBoneRotations(List<BoneBinding> bones, float angle)
    {
        var offset = Quaternion.AngleAxis(angle, Vector3.right);
        foreach (var bone in bones)
        {
            if (bone.Transform != null)
            {
                bone.Transform.localRotation = bone.RestRotation * offset;
            }
        }
    }

    private readonly struct BoneBinding
    {
        public BoneBinding(Transform transform)
        {
            Transform = transform;
            RestRotation = transform.localRotation;
        }

        public Transform Transform { get; }
        public Quaternion RestRotation { get; }
    }
}

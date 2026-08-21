using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 구역별 가로등을 관리합니다. 기본은 소등 상태이며, 브레이커 미션이 완료되면 활성 구역의 가로등만 점등합니다.
// 점등은 서서히 몇 번 깜빡인 뒤 마지막에 완전히 켜지는 연출로 진행됩니다.
public sealed class FieldLampController : MonoBehaviour
{
    [Serializable]
    private struct RegionLampGroup
    {
        public RegionId RegionId;
        public Transform Root;
    }

    // 밝기 기준값은 프리팹에 설정된 값을 그대로 쓴다. 연출은 그 값에 대한 비율로만 조절한다.
    private readonly struct Lamp
    {
        public readonly Light Light;
        public readonly float FullIntensity;

        public Lamp(Light light)
        {
            Light = light;
            FullIntensity = light.intensity;
        }
    }

    [Header("=== 활성 구역을 알려줄 컨트롤러 ===")]
    [SerializeField] private MapRegionController _regionController;

    [Header("=== 구역별 가로등 묶음 ===")]
    [Tooltip("LampCity_A ~ LampCity_E를 각 RegionId에 맞춰 등록합니다.")]
    [SerializeField] private RegionLampGroup[] _groups = Array.Empty<RegionLampGroup>();

    [Header("=== 점등 연출 ===")]
    [Tooltip("완전히 켜지기 전에 깜빡이는 횟수입니다. 0이면 깜빡임 없이 켜집니다.")]
    [SerializeField, Min(0)] private int _flickerCount = 2;

    [Tooltip("깜빡일 때 도달하는 밝기 비율입니다. 프리팹에 설정된 밝기를 100%로 봅니다.")]
    [SerializeField, Range(0f, 1f)] private float _flickerPeakRatio = 0.5f;

    [Tooltip("한 번 깜빡일 때 밝아지는 시간과 어두워지는 시간입니다.")]
    [SerializeField, Min(0f)] private float _flickerFadeSeconds = 0.25f;

    [Tooltip("깜빡임이 끝나고 100%까지 서서히 켜지는 시간입니다.")]
    [SerializeField, Min(0f)] private float _turnOnFadeSeconds = 0.8f;

    private readonly Dictionary<RegionId, Lamp[]> _lampsByRegion = new();
    private Coroutine _fadeRoutine;

    // 이미 켠 구역. 중복 요청과 여러 브레이커가 각각 완료를 알리는 경우를 걸러낸다.
    private RegionId? _litRegion;

    private void Awake()
    {
        foreach (RegionLampGroup group in _groups)
        {
            if (group.Root == null)
            {
                Debug.LogWarning($"[FieldLampController] {group.RegionId} 구역의 가로등 묶음이 비어 있습니다.", this);
                continue;
            }

            _lampsByRegion[group.RegionId] = CollectLamps(group.Root);
        }

        SetLit(false);
    }

    private void OnEnable()
    {
        if (_regionController != null)
        {
            _regionController.ActiveRegionChanged += HandleActiveRegionChanged;
        }
    }

    private void OnDisable()
    {
        if (_regionController != null)
        {
            _regionController.ActiveRegionChanged -= HandleActiveRegionChanged;
        }
    }

    // 구역이 바뀌면 이전 구역의 불이 남지 않도록 소등한다.
    private void HandleActiveRegionChanged(MapRegion region) => SetLit(false);

    // 브레이커 미션의 완료 여부를 그대로 반영합니다.
    // 켤 때는 활성 구역만, 끌 때는 남은 불이 없도록 전 구역을 끕니다.
    // 늦게 접속해 이미 완료된 상태를 따라잡는 경우에는 연출 없이 바로 켭니다.
    public void SetLit(bool lit, bool withFade = true)
    {
        StopFade();

        if (!lit)
        {
            foreach (Lamp[] lamps in _lampsByRegion.Values)
            {
                Enable(lamps, false);
            }

            _litRegion = null;
            return;
        }

        if (!TryGetActiveLamps(out RegionId regionId, out Lamp[] activeLamps) || _litRegion == regionId)
        {
            return;
        }

        _litRegion = regionId;
        Enable(activeLamps, true);

        if (withFade)
        {
            _fadeRoutine = StartCoroutine(FadeOn(activeLamps));
        }
        else
        {
            SetRatio(activeLamps, 1f);
        }
    }

    private bool TryGetActiveLamps(out RegionId regionId, out Lamp[] lamps)
    {
        regionId = default;
        lamps = null;

        MapRegion activeRegion = _regionController != null ? _regionController.ActiveRegion : null;
        if (activeRegion == null)
        {
            Debug.LogWarning("[FieldLampController] 활성 구역이 없어 가로등을 켤 수 없습니다.", this);
            return false;
        }

        regionId = activeRegion.RegionId;
        if (!_lampsByRegion.TryGetValue(regionId, out lamps))
        {
            Debug.LogWarning($"[FieldLampController] {regionId} 구역의 가로등 묶음이 등록되지 않았습니다.", this);
            return false;
        }

        return true;
    }

    // 서서히 깜빡인 뒤 마지막에 100%까지 켠다.
    private IEnumerator FadeOn(Lamp[] lamps)
    {
        SetRatio(lamps, 0f);

        for (int i = 0; i < _flickerCount; i++)
        {
            yield return Fade(lamps, 0f, _flickerPeakRatio, _flickerFadeSeconds);
            yield return Fade(lamps, _flickerPeakRatio, 0f, _flickerFadeSeconds);
        }

        yield return Fade(lamps, 0f, 1f, _turnOnFadeSeconds);
        _fadeRoutine = null;
    }

    private IEnumerator Fade(Lamp[] lamps, float fromRatio, float toRatio, float seconds)
    {
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.deltaTime)
        {
            SetRatio(lamps, Mathf.Lerp(fromRatio, toRatio, elapsed / seconds));
            yield return null;
        }

        SetRatio(lamps, toRatio);
    }

    private void StopFade()
    {
        if (_fadeRoutine == null)
        {
            return;
        }

        StopCoroutine(_fadeRoutine);
        _fadeRoutine = null;
    }

    // 꺼둔 뒤에도 다시 찾을 수 있도록 비활성 오브젝트까지 포함해 모은다.
    private static Lamp[] CollectLamps(Transform root)
    {
        Light[] lights = root.GetComponentsInChildren<Light>(true);
        Lamp[] lamps = new Lamp[lights.Length];
        for (int i = 0; i < lights.Length; i++)
        {
            lamps[i] = new Lamp(lights[i]);
        }

        return lamps;
    }

    // 프리팹에 설정된 밝기로 되돌려, 다음 점등이 항상 같은 값에서 시작하게 한다.
    private static void Enable(Lamp[] lamps, bool enabled)
    {
        SetRatio(lamps, 1f);
        foreach (Lamp lamp in lamps)
        {
            if (lamp.Light != null)
            {
                lamp.Light.gameObject.SetActive(enabled);
            }
        }
    }

    private static void SetRatio(Lamp[] lamps, float ratio)
    {
        foreach (Lamp lamp in lamps)
        {
            if (lamp.Light != null)
            {
                lamp.Light.intensity = lamp.FullIntensity * ratio;
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class MirrorReflectionFeature : ScriptableRendererFeature
{
    // =========================================================
    // 1. Settings
    // =========================================================
    [System.Serializable]
    public class Settings
    {
        [Header("반사 텍스처")]
        [Range(64, 2048), Tooltip("기본 반사 텍스처 해상도")]
        public int textureSize = 512;

        [Header("거울 평면")]
        [Tooltip("Near Clip Plane 오프셋 (Z-fighting 방지)")]
        public float clipPlaneOffset = 0.07f;

        [Header("LOD 거리 기준")]
        [Tooltip("이 거리 이하 : 풀 해상도")]
        public float lodDistanceFull = 5f;

        [Tooltip("이 거리 이하 : 절반 해상도")]
        public float lodDistanceHalf = 15f;
        // 이 거리 초과 : 1/4 해상도
    }

    public Settings settings = new();

    // =========================================================
    // 2. 내부 필드
    // =========================================================
    private static readonly int ReflectionTexID = Shader.PropertyToID("_ReflectionTex");
    private static readonly int BlendID = Shader.PropertyToID("_ReflectionBlend");

    private Dictionary<MirrorPlaneRegistrar, RenderTexture> _reflectionRTs;
    private Dictionary<MirrorPlaneRegistrar, Camera> _reflectionCams;
    private MaterialPropertyBlock _mpb;

    // =========================================================
    // 3. 생성 / 해제
    // =========================================================
    public override void Create()
    {
        _reflectionRTs = new Dictionary<MirrorPlaneRegistrar, RenderTexture>();
        _reflectionCams = new Dictionary<MirrorPlaneRegistrar, Camera>();
        _mpb = new MaterialPropertyBlock();

        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    protected override void Dispose(bool disposing)
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

        foreach (var rt in _reflectionRTs.Values)
            if (rt != null)
                RenderTexture.DestroyImmediate(rt);
        _reflectionRTs.Clear();

        foreach (var cam in _reflectionCams.Values)
            if (cam != null)
                CoreUtils.Destroy(cam.gameObject);
        _reflectionCams.Clear();
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData
    ) { }

    // =========================================================
    // 4. 메인 루프
    // =========================================================
    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera mainCam)
    {
        // ── 반사 카메라 재귀 차단 ────────────────────────────
        if (_reflectionCams.ContainsValue(mainCam))
            return;

        if (mainCam.cameraType != CameraType.Game && mainCam.cameraType != CameraType.SceneView)
            return;

        CleanupStaleResources();

        // ── Frustum Planes : 1회 계산 후 모든 거울에 재사용 ──
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes(mainCam);

        foreach (var mirror in MirrorPlaneRegistrar.AllMirrors)
        {
            if (mirror == null || mirror.MirrorRenderer == null)
                continue;

            // ── ① Frustum Culling ────────────────────────────
            if (!GeometryUtility.TestPlanesAABB(frustumPlanes, mirror.MirrorRenderer.bounds))
                continue;

            float dist = Vector3.Distance(mainCam.transform.position, mirror.transform.position);
            float maxDist = mirror.maxReflectionDistance;

            // ── ② Distance Culling : 렌더 거리 초과 → Fresnel만 ──
            if (maxDist > 0f && dist > maxDist)
            {
                ApplyBlend(mirror, 0f); // blend=0 : 셰이더가 Fresnel만 사용
                continue;
            }

            // ── ③ 페이드 블렌드 계산 ─────────────────────────
            // fadeStart ~ maxDist 구간에서 1→0 으로 부드럽게 감소
            float fadeStart = maxDist * mirror.fadeStartRatio;
            float blend =
                maxDist > 0f ? 1f - Mathf.Clamp01((dist - fadeStart) / (maxDist - fadeStart)) : 1f;
            // blend : 1.0 = 실시간 반사 100%, 0.0 = Fresnel 100%

            // ── ④ N프레임마다 갱신 ──────────────────────────
            mirror.FrameCounter++;
            if (
                mirror.FrameCounter % mirror.renderEveryNFrames != 0
                && _reflectionRTs.TryGetValue(mirror, out var cachedRt)
                && cachedRt != null
            )
            {
                ApplyTexture(mirror, cachedRt, blend); // 기존 RT 유지
                continue;
            }

            // ── ⑤ 해상도 LOD ────────────────────────────────
            int targetSize = GetLODResolution(dist);
            RenderTexture rt = GetOrCreateRT(mirror, targetSize);
            Camera reflCam = GetOrCreateCamera(mirror);

            RenderReflection(mainCam, mirror, reflCam, rt);
            ApplyTexture(mirror, rt, blend);
        }
    }

    // =========================================================
    // 5. MPB 주입 헬퍼
    // =========================================================

    // 실시간 반사 텍스처 + 블렌드값 주입
    private void ApplyTexture(MirrorPlaneRegistrar mirror, RenderTexture rt, float blend = 1f)
    {
        mirror.MirrorRenderer.GetPropertyBlock(_mpb);
        _mpb.SetTexture(ReflectionTexID, rt);
        _mpb.SetFloat(BlendID, blend);
        mirror.MirrorRenderer.SetPropertyBlock(_mpb);
    }

    // 블렌드값만 주입 (렌더 거리 초과 시 Fresnel 전환용)
    private void ApplyBlend(MirrorPlaneRegistrar mirror, float blend)
    {
        mirror.MirrorRenderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(BlendID, blend);
        mirror.MirrorRenderer.SetPropertyBlock(_mpb);
    }

    // =========================================================
    // 6. 해상도 LOD
    // =========================================================
    private int GetLODResolution(float dist)
    {
        if (dist <= settings.lodDistanceFull)
            return settings.textureSize;
        if (dist <= settings.lodDistanceHalf)
            return Mathf.Max(64, settings.textureSize / 2);
        return Mathf.Max(64, settings.textureSize / 4);
    }

    // =========================================================
    // 7. RT 생성 / 재사용
    // =========================================================
    private RenderTexture GetOrCreateRT(MirrorPlaneRegistrar mirror, int size)
    {
        if (_reflectionRTs.TryGetValue(mirror, out var rt))
        {
            if (rt != null && rt.width == size)
                return rt;
            RenderTexture.DestroyImmediate(rt);
        }

        rt = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
        {
            name = $"__MirrorRT_{mirror.name}__",
            hideFlags = HideFlags.DontSave,
            isPowerOfTwo = true,
            antiAliasing = 1,
        };
        rt.Create();
        _reflectionRTs[mirror] = rt;
        return rt;
    }

    // =========================================================
    // 8. 반사 카메라 생성 / 재사용
    // =========================================================
    private Camera GetOrCreateCamera(MirrorPlaneRegistrar mirror)
    {
        if (_reflectionCams.TryGetValue(mirror, out var cam) && cam != null)
            return cam;

        var go = new GameObject($"__MirrorCam_{mirror.name}__", typeof(Camera));
        go.hideFlags = HideFlags.HideAndDontSave;
        cam = go.GetComponent<Camera>();
        cam.enabled = false;
        _reflectionCams[mirror] = cam;
        return cam;
    }

    // =========================================================
    // 9. 반사 렌더
    // =========================================================
    private void RenderReflection(
        Camera mainCam,
        MirrorPlaneRegistrar mirror,
        Camera reflCam,
        RenderTexture rt
    )
    {
        Vector3 planeNormal = mirror.transform.up;
        Vector3 planePos = mirror.transform.position;

        reflCam.CopyFrom(mainCam);
        reflCam.clearFlags = CameraClearFlags.Skybox;
        reflCam.backgroundColor = Color.black;
        reflCam.targetTexture = rt;
        reflCam.enabled = false;

        // 거울별 cullingMask 적용
        reflCam.cullingMask = mirror.reflectionLayers & ~(1 << 4);

        Matrix4x4 reflMat = BuildReflectionMatrix(planePos, planeNormal);
        reflCam.worldToCameraMatrix = mainCam.worldToCameraMatrix * reflMat;

        Vector4 clipPlane = GetCameraSpacePlane(reflCam, planePos, planeNormal, 1f);
        reflCam.projectionMatrix = mainCam.CalculateObliqueMatrix(clipPlane);

        GL.invertCulling = true;
        reflCam.Render();
        GL.invertCulling = false;
    }

    // =========================================================
    // 10. 씬에서 제거된 거울 리소스 정리
    // =========================================================
    private void CleanupStaleResources()
    {
        var activeSet = new HashSet<MirrorPlaneRegistrar>(MirrorPlaneRegistrar.AllMirrors);
        var toRemove = new List<MirrorPlaneRegistrar>();

        foreach (var key in _reflectionRTs.Keys)
            if (!activeSet.Contains(key))
                toRemove.Add(key);

        foreach (var key in toRemove)
        {
            if (_reflectionRTs.TryGetValue(key, out var rt) && rt != null)
                RenderTexture.DestroyImmediate(rt);
            _reflectionRTs.Remove(key);

            if (_reflectionCams.TryGetValue(key, out var cam) && cam != null)
                CoreUtils.Destroy(cam.gameObject);
            _reflectionCams.Remove(key);
        }
    }

    // =========================================================
    // 11. Householder 반사 행렬
    // =========================================================
    private static Matrix4x4 BuildReflectionMatrix(Vector3 pos, Vector3 normal)
    {
        float d = -Vector3.Dot(normal, pos);
        float nx = normal.x,
            ny = normal.y,
            nz = normal.z;
        var m = Matrix4x4.identity;

        m.m00 = 1 - 2 * nx * nx;
        m.m01 = -2 * nx * ny;
        m.m02 = -2 * nx * nz;
        m.m03 = -2 * d * nx;
        m.m10 = -2 * ny * nx;
        m.m11 = 1 - 2 * ny * ny;
        m.m12 = -2 * ny * nz;
        m.m13 = -2 * d * ny;
        m.m20 = -2 * nz * nx;
        m.m21 = -2 * nz * ny;
        m.m22 = 1 - 2 * nz * nz;
        m.m23 = -2 * d * nz;
        m.m30 = 0f;
        m.m31 = 0f;
        m.m32 = 0f;
        m.m33 = 1f;

        return m;
    }

    // =========================================================
    // 12. Oblique Clip Plane
    // =========================================================
    private Vector4 GetCameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * settings.clipPlaneOffset;
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 camPos = m.MultiplyPoint(offsetPos);
        Vector3 camNorm = m.MultiplyVector(normal).normalized * sideSign;

        return new Vector4(camNorm.x, camNorm.y, camNorm.z, -Vector3.Dot(camPos, camNorm));
    }
}

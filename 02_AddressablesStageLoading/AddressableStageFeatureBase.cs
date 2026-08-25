using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.Serialization;

/// <summary>
/// Inspector에서 DungeonData 계열 SO만 할당할 수 있도록 제한한 Addressables 참조입니다.
/// </summary>
[Serializable]
public sealed class DungeonDataAssetReference : AssetReferenceT<DungeonData>
{
    public DungeonDataAssetReference(string guid)
        : base(guid) { }
}

/// <summary>
/// Addressables에 등록된 스테이지 중 현재 스테이지와 다음 스테이지만 메모리에 유지합니다.
/// 배열의 Inspector 순서를 실제 스테이지 순서로 사용하며, 수동 이동 중 발생한 오래된 로드 결과는 적용하지 않습니다.
/// </summary>
public abstract class AddressableStageFeatureBase : MonoBehaviour, IStageProvider
{
    public static event Action<int> OnAnyStageChanged;

    [SerializeField, Tooltip("스테이지 순서대로 등록한 Addressable DungeonData 배열")]
    [FormerlySerializedAs("_stages")]
    private DungeonDataAssetReference[] stageReferences;

    [SerializeField, Tooltip("현재 스테이지 인덱스")]
    [FormerlySerializedAs("_currentStageIndex")]
    private int currentStageIndex;

    private readonly Dictionary<int, AsyncOperationHandle<DungeonData>> loadedStages = new();
    private readonly List<int> releaseTargets = new();

    private IStageDungeon stageDungeon;
    private DungeonManager dungeonManager;
    private int loadRequestVersion;
    private bool isDestroyed;
    private bool isStageFixed;

    public int CurrentStageIndex => currentStageIndex;
    public bool IsStageFixed => isStageFixed;
    public event Action<int> OnStageChanged;
    public event Action<bool> OnStageFixedChanged;

    protected DungeonManager BoundDungeonManager => dungeonManager;
    protected bool IsStageSystemReady => stageDungeon != null;

    protected virtual void Awake()
    {
        // 기존 던전 시작 시점과 동일하게 SceneChanger의 전환 연출이 모두 끝난 뒤 첫 스테이지를 로드합니다.
        SceneChanger.SceneLoaded += HandleSceneLoaded;

        dungeonManager = DungeonManager.Instance;
        if (dungeonManager is IStageDungeon resolvedStageDungeon)
        {
            stageDungeon = resolvedStageDungeon;
            BeginInitialStagePreload();
            return;
        }

        dungeonManager = null;
        Debug.LogError(
            "[AddressableStageFeatureBase] DungeonManager가 IStageDungeon을 구현하지 않았습니다.",
            this
        );
    }

    private void BeginInitialStagePreload()
    {
        if (!CanUseStages())
            return;

        currentStageIndex = Mathf.Clamp(currentStageIndex, 0, stageReferences.Length - 1);

        // 목적지 씬의 Awake부터 로드를 시작하되, 실제 던전 활성화는 SceneChanger.SceneLoaded까지 보류합니다.
        StartCoroutine(PreloadStage(currentStageIndex));
    }

    private void HandleSceneLoaded()
    {
        if (!CanUseStages())
            return;

        currentStageIndex = Mathf.Clamp(currentStageIndex, 0, stageReferences.Length - 1);
        RequestStage(currentStageIndex, false, true);
    }

    /// <summary>
    /// 현재 던전을 완료하고 다음 스테이지로 이동합니다.
    /// 마지막 스테이지에서는 같은 스테이지를 다시 시작해 방치 전투를 계속합니다.
    /// </summary>
    protected void AdvanceToNextStage()
    {
        if (!CanUseStages())
            return;

        int targetIndex = isStageFixed
            ? currentStageIndex
            : Mathf.Min(currentStageIndex + 1, stageReferences.Length - 1);
        RequestStage(targetIndex, true, true);
    }

    /// <summary>
    /// 원하는 스테이지로 이동합니다. 아직 로드되지 않았다면 비동기로 로드한 뒤 던전을 시작합니다.
    /// </summary>
    public void JumpToStage(int targetIndex)
    {
        if (!CanUseStages())
            return;

        int validatedIndex = Mathf.Clamp(targetIndex, 0, stageReferences.Length - 1);
        if (validatedIndex == currentStageIndex)
            return;

        RequestStage(validatedIndex, false, false);
    }

    public void SetStageFixed(bool isFixed)
    {
        if (isStageFixed == isFixed)
            return;

        isStageFixed = isFixed;
        OnStageFixedChanged?.Invoke(isStageFixed);
    }

    private void RequestStage(int targetIndex, bool updateRecord, bool restartSameStage)
    {
        if (!restartSameStage && targetIndex == currentStageIndex)
            return;

        int requestVersion = ++loadRequestVersion;
        StartCoroutine(LoadAndActivateStage(targetIndex, updateRecord, requestVersion));
    }

    private IEnumerator LoadAndActivateStage(int targetIndex, bool updateRecord, int requestVersion)
    {
        yield return EnsureStageLoaded(targetIndex);

        if (isDestroyed || requestVersion != loadRequestVersion)
            yield break;

        if (!TryGetLoadedStage(targetIndex, out DungeonData dungeonData))
            yield break;

        currentStageIndex = targetIndex;
        stageDungeon.SetStage(dungeonData);

        if (updateRecord)
        {
            GameManager.Instance.UpdateDungeonRecord(
                dungeonData.DungeonType,
                currentStageIndex + 1
            );
        }

        OnStageChanged?.Invoke(currentStageIndex);
        OnAnyStageChanged?.Invoke(currentStageIndex);

        int nextStageIndex = currentStageIndex + 1;
        if (nextStageIndex < stageReferences.Length)
            StartCoroutine(PreloadStage(nextStageIndex));

        // SetStage에서 이전 웨이브를 정리한 뒤 Destroy가 반영될 시간을 확보하고 사용하지 않는 핸들을 해제합니다.
        StartCoroutine(ReleaseUnusedStagesAfterFrame(requestVersion));
    }

    private IEnumerator PreloadStage(int stageIndex)
    {
        yield return EnsureStageLoaded(stageIndex);

        if (isDestroyed)
            yield break;

        if (stageIndex != currentStageIndex && stageIndex != currentStageIndex + 1)
            ReleaseStage(stageIndex);
    }

    private IEnumerator EnsureStageLoaded(int stageIndex)
    {
        if (
            loadedStages.TryGetValue(stageIndex, out AsyncOperationHandle<DungeonData> cachedHandle)
        )
        {
            if (!cachedHandle.IsDone)
                yield return cachedHandle;

            yield break;
        }

        DungeonDataAssetReference stageReference = stageReferences[stageIndex];
        if (stageReference == null || !stageReference.RuntimeKeyIsValid())
        {
            Debug.LogError(
                $"[AddressableStageFeatureBase] {stageIndex + 1} 스테이지의 Addressable 참조가 비어 있습니다.",
                this
            );
            yield break;
        }

        AsyncOperationHandle<DungeonData> loadHandle = Addressables.LoadAssetAsync<DungeonData>(
            stageReference
        );
        loadedStages.Add(stageIndex, loadHandle);
        yield return loadHandle;

        if (
            loadHandle.IsValid()
            && loadHandle.Status == AsyncOperationStatus.Succeeded
            && loadHandle.Result != null
        )
            yield break;

        Debug.LogError(
            $"[AddressableStageFeatureBase] {stageIndex + 1} 스테이지 DungeonData 로드에 실패했습니다.",
            this
        );
        ReleaseStage(stageIndex);
    }

    private bool TryGetLoadedStage(int stageIndex, out DungeonData dungeonData)
    {
        dungeonData = null;

        if (!loadedStages.TryGetValue(stageIndex, out AsyncOperationHandle<DungeonData> handle))
            return false;

        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            return false;

        dungeonData = handle.Result;
        return true;
    }

    private IEnumerator ReleaseUnusedStagesAfterFrame(int requestVersion)
    {
        yield return null;

        // 새로운 수동 이동 요청이 들어왔다면 새 목적지 로드 핸들을 이전 요청이 해제하지 않도록 중단합니다.
        if (requestVersion != loadRequestVersion)
            yield break;

        int nextStageIndex = currentStageIndex + 1;
        releaseTargets.Clear();

        foreach (int stageIndex in loadedStages.Keys)
        {
            if (stageIndex != currentStageIndex && stageIndex != nextStageIndex)
                releaseTargets.Add(stageIndex);
        }

        foreach (int stageIndex in releaseTargets)
            ReleaseStage(stageIndex);
    }

    private void ReleaseStage(int stageIndex)
    {
        if (!loadedStages.TryGetValue(stageIndex, out AsyncOperationHandle<DungeonData> handle))
            return;

        loadedStages.Remove(stageIndex);

        if (handle.IsValid())
            Addressables.Release(handle);
    }

    private bool CanUseStages()
    {
        if (!IsStageSystemReady)
            return false;

        if (stageReferences != null && stageReferences.Length > 0)
            return true;

        Debug.LogError(
            "[AddressableStageFeatureBase] Stage References 배열에 DungeonData를 등록해주세요.",
            this
        );
        return false;
    }

    protected virtual void OnDestroy()
    {
        SceneChanger.SceneLoaded -= HandleSceneLoaded;
        isDestroyed = true;
        loadRequestVersion++;
        StopAllCoroutines();

        foreach (AsyncOperationHandle<DungeonData> handle in loadedStages.Values)
        {
            if (handle.IsValid())
                Addressables.Release(handle);
        }

        loadedStages.Clear();
    }

    protected virtual void OnValidate()
    {
        int maximumIndex = stageReferences == null ? 0 : Mathf.Max(0, stageReferences.Length - 1);
        currentStageIndex = Mathf.Clamp(currentStageIndex, 0, maximumIndex);

        if (stageReferences == null)
            return;

        for (int i = 0; i < stageReferences.Length; i++)
        {
            if (stageReferences[i] != null && stageReferences[i].RuntimeKeyIsValid())
                continue;

            Debug.LogError(
                $"[AddressableStageFeatureBase] Stage References의 {i + 1}번째 항목이 비어 있습니다.",
                this
            );
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

public class ObjectPoolManager : MonoBehaviour
{
    public static Dictionary<Kind, ObjectPoolManager> instance = new();
    public int[] defaultCapacity;
    public int[] maxPoolSize;
    public GameObject[] summonPrefab;
    public int weaponIndex = 0;
    public Kind key;
    public Dictionary<int, List<GameObject>> clones = new();

    private readonly Dictionary<int, IObjectPool<GameObject>> pool = new();

    public IObjectPool<GameObject> Pool
    {
        get
        {
            if (!pool.ContainsKey(weaponIndex)) Init();
            return pool[weaponIndex];
        }
        private set
        {
            pool[weaponIndex] = value;
        }
    }

    private void Awake()
    {
        instance[key] = this;

        Init();
    }

    private void Init()
    {
        Pool = new ObjectPool<GameObject>(CreatePooledItem, OnTakeFromPool, OnReturnedToPool,
        OnDestroyPoolObject, true, defaultCapacity[weaponIndex], maxPoolSize[weaponIndex]);

        if (!clones.ContainsKey(weaponIndex)) clones[weaponIndex] = new List<GameObject>();

        if (clones[weaponIndex].Count < defaultCapacity[weaponIndex])
        {
            // 미리 오브젝트 생성 해놓기
            for (int i = 0; i < defaultCapacity[weaponIndex]; i++)
            {
                Pool.Release(CreatePooledItem());
            }
        }
    }

    // 생성
    private GameObject CreatePooledItem()
    {
        int index = clones[weaponIndex].Count;
        clones[weaponIndex].Add(Instantiate(summonPrefab[weaponIndex]));
        return clones[weaponIndex][index];
    }

    // 사용
    private void OnTakeFromPool(GameObject poolGo)
    {
        poolGo.SetActive(true);
    }

    // 반환
    private void OnReturnedToPool(GameObject poolGo)
    {
        poolGo.SetActive(false);
    }

    // 삭제
    private void OnDestroyPoolObject(GameObject poolGo)
    {
        Destroy(poolGo);
    }
}
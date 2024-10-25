using System;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Pool;
using Random = UnityEngine.Random;

public class WaveManager : MonoBehaviour
{
    private class Wave
    {
        private HashSet<Enemy> enemies = new();

        public event Action Cleared;

        public bool Active => enemies.Count > 0;

        public bool Add(Enemy enemy) => enemies.Add(enemy);

        public void Remove(Enemy enemy)
        {
            enemies.Remove(enemy);
            
            if (enemies.Count > 0)
                return;
            
            Cleared?.Invoke();
        }
    }
    
    [Serializable]
    private struct WaveInfo
    {
        public int spawnCount;
        public float spawnPeriodDuration;
    }
    
    [SerializeField] private Enemy enemyPrefab;
    [SerializeField] private NavMeshSurface navMesh;
    [SerializeField] private Transform player;
    [SerializeField] private LayerMask losRayMask;

    [SerializeField] private List<WaveInfo> waves;

    private ObjectPool<Enemy> enemyPool;
    private Wave currentWave = new();

    private void OnEnable()
    {
        enemyPool = new ObjectPool<Enemy>(
            () =>
            {
                Enemy enemy = Instantiate(enemyPrefab);
                enemy.gameObject.SetActive(false);
                return enemy;
            },
            enemy =>
            {
                Vector3 navMeshMin = navMesh.transform.position + navMesh.navMeshData.sourceBounds.min;
                Vector3 navMeshMax = navMesh.transform.position + navMesh.navMeshData.sourceBounds.max;
                Debug.DrawLine(navMeshMin, navMeshMax, Color.blue, 10f);
                float halfHeight = 0.5f * enemy.GetComponent<NavMeshAgent>().height;

                if (((1 << navMesh.gameObject.layer) & losRayMask) <= 0)
                    throw new Exception("NavMesh isn't on the right layer.");
                
                while (true)
                {
                    Vector3 position = new Vector3(
                        Random.Range(navMeshMin.x, navMeshMax.x),
                        Random.Range(navMeshMin.y, navMeshMax.y),
                        Random.Range(navMeshMin.z, navMeshMax.z)
                    );

                    Debug.DrawLine(Vector3.zero, position, Color.magenta, 10f);
                    if (NavMesh.SamplePosition(position, out NavMeshHit hit, 4f * halfHeight, NavMesh.AllAreas))
                    {
                        if (!Physics.Linecast(player.position, hit.position + halfHeight * Vector3.up, losRayMask))
                            continue;

                        enemy.transform.position = hit.position;
                        enemy.gameObject.SetActive(true);
                        enemy.Target = player;
                        return;
                    }
                }
            },
            enemy =>
            {
                enemy.gameObject.SetActive(false);
            },
            Destroy
        );

        currentWave.Cleared += StartNextWave;
        StartNextWave();
    }

    private void StartNextWave()
    {
        if (waves.Count <= 0)
            return;

        WaveInfo wave = waves[0];
        waves.RemoveAt(0);

        StartCoroutine(SpawningRoutine(wave));
    }

    private IEnumerator SpawningRoutine(WaveInfo wave)
    {
        YieldInstruction waitForNextSpawn = new WaitForSeconds(wave.spawnPeriodDuration / wave.spawnCount);
        
        int spawnCount = wave.spawnCount;
        while (spawnCount-- > 0)
        {
            Enemy enemy = enemyPool.Get();
            Damageable damageController = enemy.GetComponent<Damageable>();
            damageController.Died += OnDeath;
            currentWave.Add(enemy);

            void OnDeath()
            {
                enemyPool.Release(enemy);
                currentWave.Remove(enemy);
                damageController.Died -= OnDeath;
            }

            yield return waitForNextSpawn;
        }
    }
}
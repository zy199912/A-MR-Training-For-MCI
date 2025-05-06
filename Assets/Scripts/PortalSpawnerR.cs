using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PortalSpawnerR : MonoBehaviour
{
    public GarbageData[] garbageDatas; // 存放垃圾数据的数组，需在Inspector面板赋值
    public Transform topPoint; // TopPoint的Transform组件，即垃圾生成位置，可在Inspector面板拖拽赋值
    public Transform rightFoot; // LeftFoot的Transform组件，即垃圾移动目标位置，可在Inspector面板拖拽赋值
    public float detectionRadius = 1f; // 检测LeftFoot位置是否有垃圾的半径

    private float timer = 0f;
    public float spawnInterval = 3f; // 生成间隔时间

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= spawnInterval && !IsGarbageAtRightFoot())
        {
            SpawnGarbage();
            timer = 0f;
        }
    }

    bool IsGarbageAtRightFoot()
    {
        Collider[] colliders = Physics.OverlapSphere(rightFoot.position, detectionRadius);
        foreach (Collider collider in colliders)
        {
            if (collider.CompareTag("Garbage")) // 假设垃圾对象的标签为 "Garbage"
            {
                return true;
            }
        }
        return false;
    }

    void SpawnGarbage()
    {
        if (garbageDatas.Length > 0)
        {
            int randomIndex = Random.Range(0, garbageDatas.Length);
            GarbageData currentGarbageData = garbageDatas[randomIndex];

            // 实例化垃圾预制体
            GameObject spawnedGarbage = Instantiate(currentGarbageData.garbagePrefab, topPoint.position, Quaternion.identity);

            // 为垃圾添加移动脚本并设置参数
            GarbageMover garbageMover = spawnedGarbage.AddComponent<GarbageMover>();
            garbageMover.targetPosition = rightFoot;
            garbageMover.moveSpeed = currentGarbageData.moveSpeed;
        }
        else
        {
            Debug.LogError("garbageDatas数组未正确赋值，没有可生成的垃圾数据！");
        }
    }
}
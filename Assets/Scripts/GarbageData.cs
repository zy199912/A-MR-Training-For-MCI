using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using UnityEngine;

// 定义垃圾形状类型的枚举
public enum GarbageShapeType
{
    Cylinder,
    Square
}

// 创建可在Unity菜单中创建的资产
[CreateAssetMenu(fileName = "NewGarbageData", menuName = "Garbage Data", order = 1)]
public class GarbageData : ScriptableObject
{
    // 垃圾预制体
    public GameObject garbagePrefab;
    // 垃圾所属的形状类型
    public GarbageShapeType shapeType;
    // 垃圾名称
    public string garbageName;
    // 垃圾移动速度
    public float moveSpeed;
}
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

public class BluetoothManager : MonoBehaviour
{
    // 更新的设备信息
    public string deviceName = "IM600-V3.11";
    private string deviceAddress = "19:6F:51:5D:D5:D6";
    
    // 服务和特征UUID
    private string serviceUUID = "ae30"; // 使用短UUID格式
    private string writeCharacteristicUUID = "ae01"; // 写入特征
    private string notifyCharacteristicUUID = "ae02"; // 通知特征
    
    // 状态管理
    enum States
    {
        None,
        Scan,
        Connect,
        RequestMTU,
        Subscribe,
        Unsubscribe,
        Disconnect,
        Communication
    }
    
    private States _state = States.None;
    private float _timeout = 0f;
    private bool _connected = false;
    private bool _foundService = false;
    private bool _foundCharacteristic = false;
    
    // 引用GarbageMover
    public GarbageMover gameController;
    
    // 动作检测阈值
    public float stompThreshold = 15.0f;
    public float kickThreshold = 10.0f;
    
    // 传感器数据
    private Vector3 acceleration = Vector3.zero;
    private Vector3 gyro = Vector3.zero;
    private Vector3 baselineAcceleration = Vector3.zero;
    private bool isCalibrated = false;
    
    // 冷却时间防止多次触发
    private float lastStompTime = 0f;
    private float lastKickTime = 0f;
    private float cooldownTime = 0.5f;
    
    // 校准相关
    private float calibrationTimer = 0f;
    private float calibrationTime = 3.0f;
    private List<Vector3> calibrationSamples = new List<Vector3>();
    
    void Start()
    {
        // 如果未指定GarbageMover，自动查找
        if (gameController == null)
            gameController = FindObjectOfType<GarbageMover>();
            
        // 初始化蓝牙
        StartProcess();
    }
    
    void StartProcess()
    {
        Debug.Log("正在初始化蓝牙...");
        
        Reset();
        BluetoothLEHardwareInterface.Initialize(true, false, () => {
            Debug.Log("蓝牙初始化成功");
            
            // 设置蓝牙扫描模式和连接优先级以提高性能
            BluetoothLEHardwareInterface.BluetoothScanMode(BluetoothLEHardwareInterface.ScanMode.LowLatency);
            BluetoothLEHardwareInterface.BluetoothConnectionPriority(BluetoothLEHardwareInterface.ConnectionPriority.High);
            
            SetState(States.Scan, 0.5f);
            
        }, (error) => {
            Debug.LogError("蓝牙初始化失败: " + error);
            
            // 如果蓝牙未启用，尝试启用它
            if (error.Contains("Bluetooth LE Not Enabled"))
                BluetoothLEHardwareInterface.BluetoothEnable(true);
        });
    }
    
    void Reset()
    {
        _connected = false;
        _timeout = 0f;
        _state = States.None;
        _foundService = false;
        _foundCharacteristic = false;
        isCalibrated = false;
    }
    
    void SetState(States newState, float timeout)
    {
        _state = newState;
        _timeout = timeout;
    }
    
    void Update()
    {
        if (_timeout > 0f)
        {
            _timeout -= Time.deltaTime;
            if (_timeout <= 0f)
            {
                _timeout = 0f;
                
                switch (_state)
                {
                    case States.None:
                        break;
                        
                    case States.Scan:
                        Debug.Log("开始扫描设备...");
                        
                        BluetoothLEHardwareInterface.ScanForPeripheralsWithServices(null, (address, name) => {
                            Debug.Log($"发现设备: {name} ({address})");
                            
                            // 也检查地址匹配，因为有时设备名称可能不完全一致
                            if ((name != null && name.Contains(deviceName)) || address == deviceAddress)
                            {
                                Debug.Log($"找到IMU传感器: {name}，正在连接...");
                                BluetoothLEHardwareInterface.StopScan();
                                
                                deviceAddress = address;
                                SetState(States.Connect, 0.5f);
                            }
                        }, null, false);
                        break;
                        
                    case States.Connect:
                        Debug.Log($"正在连接设备: {deviceAddress}");
                        
                        // 重置标志
                        _foundService = false;
                        _foundCharacteristic = false;
                        
                        BluetoothLEHardwareInterface.ConnectToPeripheral(deviceAddress, null, null, (address, serviceUUID, characteristicUUID) => {
                            
                            Debug.Log($"发现特征: {serviceUUID} -> {characteristicUUID}");
                            
                            if (IsEqual(serviceUUID, this.serviceUUID))
                            {
                                Debug.Log($"找到匹配的服务: {serviceUUID}");
                                _foundService = true;
                                
                                if (IsEqual(characteristicUUID, notifyCharacteristicUUID))
                                {
                                    Debug.Log($"找到匹配的特征: {characteristicUUID}");
                                    _foundCharacteristic = true;
                                    
                                    _connected = true;
                                    SetState(States.RequestMTU, 2f);
                                }
                            }
                        }, (disconnectedAddress) => {
                            Debug.Log($"设备断开连接: {disconnectedAddress}");
                            _connected = false;
                            
                            // 尝试重新扫描和连接
                            SetState(States.Scan, 1f);
                        });
                        break;
                        
                    case States.RequestMTU:
                        Debug.Log("正在请求MTU...");
                        
                        BluetoothLEHardwareInterface.RequestMtu(deviceAddress, 185, (address, newMTU) => {
                            Debug.Log($"MTU设置为: {newMTU}");
                            
                            SetState(States.Subscribe, 0.5f);
                        });
                        break;
                        
                    case States.Subscribe:
                        Debug.Log("正在订阅特征...");
                        
                        BluetoothLEHardwareInterface.SubscribeCharacteristicWithDeviceAddress(
                            deviceAddress,
                            FullUUID(serviceUUID),
                            FullUUID(notifyCharacteristicUUID),
                            (address, characteristic) => {
                                Debug.Log("订阅成功");
                                
                                // 重置校准
                                calibrationTimer = 0f;
                                isCalibrated = false;
                                calibrationSamples.Clear();
                                
                                SetState(States.Communication, 0.1f);
                            },
                            (address, characteristic, rawData) => {
                                // 处理接收到的IMU数据
                                ProcessIMUData(rawData);
                            }
                        );
                        break;
                        
                    case States.Communication:
                        // 已连接，可以进行通信
                        _state = States.None;
                        Debug.Log("通信已准备就绪");
                        break;
                        
                    case States.Unsubscribe:
                        BluetoothLEHardwareInterface.UnSubscribeCharacteristic(deviceAddress, FullUUID(serviceUUID), FullUUID(notifyCharacteristicUUID), null);
                        SetState(States.Disconnect, 1f);
                        break;
                        
                    case States.Disconnect:
                        if (_connected)
                        {
                            BluetoothLEHardwareInterface.DisconnectPeripheral(deviceAddress, (address) => {
                                Debug.Log("设备已断开连接");
                                _connected = false;
                                _state = States.None;
                            });
                        }
                        else
                        {
                            _state = States.None;
                        }
                        break;
                }
            }
        }
        
        // 如果已连接但未校准，进行校准
        if (_connected && !isCalibrated && _state == States.None)
        {
            CalibrateIMU();
        }
        
        // 已连接且已校准后，检测动作
        if (_connected && isCalibrated && _state == States.None)
        {
            DetectMotions();
        }
    }
    
    private void ProcessIMUData(byte[] rawData)
    {
        // 根据您提供的模块数据格式进行解析
        if (rawData.Length < 20)
        {
            Debug.LogWarning($"接收到的数据太短: {rawData.Length} 字节");
            return;
        }
        
        try
        {
            // 假设数据格式基于您提供的示例：11-FF-FF-0A-57-09-00-02-00-FE-FF...
            // 这只是一个示例解析，您需要根据传感器的实际数据格式调整
            
            // 打印原始数据帮助调试
            string hexData = BitConverter.ToString(rawData);
            Debug.Log($"原始数据: {hexData}");
            
            // 尝试不同的数据偏移
            // 加速度：尝试不同的字节偏移
            short accelX, accelY, accelZ;
            short gyroX, gyroY, gyroZ;
            
            // 方案1：如之前的偏移
            if (rawData.Length >= 22) {
                accelX = (short)((rawData[10] << 8) | rawData[11]);
                accelY = (short)((rawData[12] << 8) | rawData[13]);
                accelZ = (short)((rawData[14] << 8) | rawData[15]);
                
                gyroX = (short)((rawData[16] << 8) | rawData[17]);
                gyroY = (short)((rawData[18] << 8) | rawData[19]);
                gyroZ = (short)((rawData[20] << 8) | rawData[21]);
            } 
            // 方案2：从开始的偏移
            else if (rawData.Length >= 12) {
                accelX = (short)((rawData[0] << 8) | rawData[1]);
                accelY = (short)((rawData[2] << 8) | rawData[3]);
                accelZ = (short)((rawData[4] << 8) | rawData[5]);
                
                gyroX = (short)((rawData[6] << 8) | rawData[7]);
                gyroY = (short)((rawData[8] << 8) | rawData[9]);
                gyroZ = (short)((rawData[10] << 8) | rawData[11]);
            } else {
                // 如果数据太短，使用默认值
                accelX = accelY = accelZ = 0;
                gyroX = gyroY = gyroZ = 0;
            }
            
            // 将原始数据转换为适当的单位（例如G或度/秒）
            // 缩放系数需要根据传感器的具体规格进行调整
            float accelScale = 1.0f / 32768.0f * 16.0f; // 假设±16G量程
            float gyroScale = 1.0f / 32768.0f * 2000.0f; // 假设±2000°/s量程
            
            acceleration.x = accelX * accelScale;
            acceleration.y = accelY * accelScale;
            acceleration.z = accelZ * accelScale;
            
            gyro.x = gyroX * gyroScale;
            gyro.y = gyroY * gyroScale;
            gyro.z = gyroZ * gyroScale;
            
            // 调试输出
            Debug.Log($"加速度: X={acceleration.x:F2}, Y={acceleration.y:F2}, Z={acceleration.z:F2}");
            Debug.Log($"陀螺仪: X={gyro.x:F2}, Y={gyro.y:F2}, Z={gyro.z:F2}");
        }
        catch (Exception e)
        {
            Debug.LogError($"处理IMU数据时出错: {e.Message}");
        }
    }
    
    private void CalibrateIMU()
    {
        if (isCalibrated)
            return;
            
        if (calibrationTimer == 0f)
        {
            Debug.Log("开始IMU校准。请保持静止3秒...");
            calibrationSamples.Clear();
        }
        
        calibrationTimer += Time.deltaTime;
        calibrationSamples.Add(acceleration);
        
        if (calibrationTimer >= calibrationTime)
        {
            // 计算基准值
            Vector3 sum = Vector3.zero;
            foreach (var sample in calibrationSamples)
            {
                sum += sample;
            }
            baselineAcceleration = sum / calibrationSamples.Count;
            
            Debug.Log($"校准完成。基准加速度: {baselineAcceleration}");
            isCalibrated = true;
        }
    }
    
    private void DetectMotions()
    {
        // 计算相对于基准的加速度
        Vector3 relativeAccel = acceleration - baselineAcceleration;
        
        // 检测跺脚（Z轴强烈加速度变化）
        if (Mathf.Abs(relativeAccel.z) > stompThreshold && Time.time > lastStompTime + cooldownTime)
        {
            Debug.Log($"检测到跺脚！Z轴加速度: {relativeAccel.z:F2}");
            lastStompTime = Time.time;
            
            // 触发跺脚动作（S键）
            if (gameController != null)
                gameController.OnStompDetected();
        }
        
        // 检测踢腿（Y轴加速度变化与一些角速度变化）
        if (relativeAccel.y > kickThreshold && 
            (Mathf.Abs(gyro.x) > kickThreshold/2 || Mathf.Abs(gyro.z) > kickThreshold/2) && 
            Time.time > lastKickTime + cooldownTime)
        {
            Debug.Log($"检测到踢腿！Y轴加速度: {relativeAccel.y:F2}, 陀螺仪X: {gyro.x:F2}, 陀螺仪Z: {gyro.z:F2}");
            lastKickTime = Time.time;
            
            // 触发踢腿动作（W键）
            if (gameController != null)
                gameController.OnKickDetected();
        }
    }
    
    // 向设备发送命令（如果需要）
    private void SendCommand(byte[] command)
    {
        if (!_connected || string.IsNullOrEmpty(deviceAddress))
            return;
            
        BluetoothLEHardwareInterface.WriteCharacteristic(
            deviceAddress, 
            FullUUID(serviceUUID), 
            FullUUID(writeCharacteristicUUID), 
            command, 
            command.Length, 
            false, // 尝试设置为false，如果写入失败可尝试true
            (characteristicUUID) => {
                Debug.Log("命令发送成功");
            }
        );
    }
    
    string FullUUID(string uuid)
    {
        string fullUUID = uuid;
        if (fullUUID.Length == 4)
            fullUUID = "0000" + uuid + "-0000-1000-8000-00805f9b34fb";
            
        return fullUUID;
    }
    
    bool IsEqual(string uuid1, string uuid2)
    {
        if (uuid1.Length == 4)
            uuid1 = FullUUID(uuid1);
        if (uuid2.Length == 4)
            uuid2 = FullUUID(uuid2);
            
        return (uuid1.ToUpper().Equals(uuid2.ToUpper()));
    }
    
    void OnApplicationQuit()
    {
        // 清理蓝牙资源
        if (_connected)
        {
            SetState(States.Unsubscribe, 0.1f);
        }
        
        BluetoothLEHardwareInterface.DeInitialize(() => {
            BluetoothLEHardwareInterface.FinishDeInitialize();
        });
    }
    
    void OnDestroy()
    {
        CancelInvoke();
    }
}
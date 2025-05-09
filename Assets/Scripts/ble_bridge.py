import asyncio
import json
import websockets
import logging
import numpy as np
from bleak import BleakScanner, BleakClient
from collections import deque

# 配置区域 - 根据你的设备修改
DEVICE_NAME = "im600-V3.11"  # 修改为扫描到的名称
SERVICE_UUID = "0000ae30-0000-1000-8000-00805f9b34fb"  # 服务UUID
CHARACTERISTIC_UUID = "0000ae02-0000-1000-8000-00805f9b34fb"  # 通知特征UUID
WEBSOCKET_PORT = 8765  # WebSocket服务器端口
DEVICE_ADDRESS = "19:6F:51:5D:D5:D6"  # 扫描到的设备地址

# 设置日志
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# 保存连接的WebSocket客户端
connected_clients = set()

# 动作检测参数
WINDOW_SIZE = 30  # 传感器数据窗口大小
STOMP_ACCEL_THRESHOLD = 2.0  # 跺脚加速度阈值 (G)
KICK_GYRO_THRESHOLD = 200.0  # 踢腿角速度阈值 (°/s)
COOLDOWN_TIME = 1.0  # 动作冷却时间(秒)，避免重复检测
MOTION_COMPLETION_TIME = 0.8  # 动作完成时间(秒)，确保一次动作只报告一次

# 数据缓冲区
accel_buffer = deque(maxlen=WINDOW_SIZE)
gyro_buffer = deque(maxlen=WINDOW_SIZE)
time_buffer = deque(maxlen=WINDOW_SIZE)  # 存储时间戳

# 上次检测到动作的时间
last_detection_time = 0
current_motion_state = None  # 当前正在进行中的动作
motion_start_time = 0  # 动作开始时间

# 检测动作的函数
def detect_motion(accel_data, gyro_data, timestamp):
    """
    检测跺脚或踢腿动作
    
    参数:
    accel_data - 包含x,y,z加速度的字典
    gyro_data - 包含x,y,z角速度的字典
    timestamp - 当前时间戳
    
    返回:
    None, "stomp", 或 "kick" 字符串
    """
    global last_detection_time, current_motion_state, motion_start_time
    current_time = timestamp
    
    # 添加数据到缓冲区
    accel_buffer.append(accel_data)
    gyro_buffer.append(gyro_data)
    time_buffer.append(current_time)
    
    # 缓冲区未满时不进行检测
    if len(accel_buffer) < WINDOW_SIZE:
        return None
    
    # 冷却时间检查
    if current_time - last_detection_time < COOLDOWN_TIME:
        return None
    
    # 如果当前有动作正在进行，检查是否超时
    if current_motion_state and (current_time - motion_start_time) > MOTION_COMPLETION_TIME:
        # 动作已完成，重置状态
        detected_motion = current_motion_state
        current_motion_state = None
        last_detection_time = current_time
        return detected_motion
    
    # 如果当前已有动作识别中，不再检测新动作
    if current_motion_state:
        return None
    
    # =========== 更精确的动作检测逻辑 ===========
    
    # 根据传感器朝向（X轴垂直向上，Y轴左->右，Z轴后->前）
    
    # 1. 提取最近的传感器数据（减少噪声影响）
    recent_window = 10  # 最近的几个数据点
    recent_accel_x = [data["x"] for data in list(accel_buffer)[-recent_window:]]
    recent_accel_z = [data["z"] for data in list(accel_buffer)[-recent_window:]]
    recent_gyro_y = [data["y"] for data in list(gyro_buffer)[-recent_window:]]
    
    # 2. 计算特征
    # 加速度X轴（垂直方向）的变化 - 对跺脚尤为重要
    accel_x_range = max(recent_accel_x) - min(recent_accel_x)
    accel_x_peak = max(abs(x) for x in recent_accel_x)
    
    # 加速度Z轴（前后方向）的变化 - 对踢腿尤为重要
    accel_z_range = max(recent_accel_z) - min(recent_accel_z)
    accel_z_peak = max(abs(z) for z in recent_accel_z)
    
    # 角速度Y轴（横向旋转）- 踢腿时腿会有一个绕Y轴的旋转
    gyro_y_peak = max(abs(y) for y in recent_gyro_y)
    
    # 3. 动作特征分析
    is_stomp = False
    is_kick = False
    
    # 跺脚特征：X轴加速度变化大（垂直方向）
    # 腿往下踩时会有明显的垂直加速度变化，且相对较少的前后运动
    if accel_x_range > STOMP_ACCEL_THRESHOLD and accel_x_peak > 1.5:
        # 确保Z轴运动相对较小（排除踢腿的可能）
        if accel_z_range < accel_x_range * 0.8:
            is_stomp = True
    
    # 踢腿特征：Z轴加速度变化大（前后方向）且Y轴角速度明显
    # 踢腿时会有明显的前后运动和一定的转动
    if accel_z_range > KICK_GYRO_THRESHOLD/100 and gyro_y_peak > 80:
        is_kick = True
    
    # 如果两个条件都满足，使用更强的特征来区分
    if is_stomp and is_kick:
        if accel_x_range > accel_z_range:
            is_kick = False
        else:
            is_stomp = False
    
    # 设置动作状态
    motion_type = None
    if is_stomp:
        motion_type = "stomp"
        current_motion_state = "stomp"
        motion_start_time = current_time
    elif is_kick:
        motion_type = "kick"
        current_motion_state = "kick"
        motion_start_time = current_time
    
    # 记录检测结果
    if motion_type:
        logger.info(f"检测到动作开始: {motion_type}")
        logger.info(f"  X加速度范围: {accel_x_range:.2f}G, Z加速度范围: {accel_z_range:.2f}G")
        logger.info(f"  X加速度峰值: {accel_x_peak:.2f}G, Y角速度峰值: {gyro_y_peak:.2f}°/s")
    
    # 我们不在这里立即返回动作，而是等到动作完成才报告
    return None

# 处理IMU数据的函数
def process_imu_data(data):
    """处理来自IMU的原始数据并转换为JSON格式"""
    try:
        # 记录原始数据
        hex_data = data.hex('-')
        current_time = asyncio.get_event_loop().time()
        
        # 根据你的IMU数据格式调整这里的解析逻辑
        if len(data) >= 22:
            accel_x = int.from_bytes(data[10:12], byteorder='big', signed=True)
            accel_y = int.from_bytes(data[12:14], byteorder='big', signed=True)
            accel_z = int.from_bytes(data[14:16], byteorder='big', signed=True)
            
            gyro_x = int.from_bytes(data[16:18], byteorder='big', signed=True)
            gyro_y = int.from_bytes(data[18:20], byteorder='big', signed=True)
            gyro_z = int.from_bytes(data[20:22], byteorder='big', signed=True)
            
            # 转换为适当单位
            accel_scale = 1.0 / 32768.0 * 16.0  # 假设±16G量程
            gyro_scale = 1.0 / 32768.0 * 2000.0  # 假设±2000°/s量程
            
            accel_data = {
                "x": accel_x * accel_scale,
                "y": accel_y * accel_scale,
                "z": accel_z * accel_scale
            }
            
            gyro_data = {
                "x": gyro_x * gyro_scale,
                "y": gyro_y * gyro_scale,
                "z": gyro_z * gyro_scale
            }
            
            # 检测动作
            motion_type = detect_motion(accel_data, gyro_data, current_time)
            
            # 如果检测到动作，则发送动作类型
            if motion_type:
                logger.info(f"发送动作事件: {motion_type}")
                return json.dumps({
                    "motion_type": motion_type,
                    "timestamp": current_time
                })
            return None
        else:
            logger.warning(f"数据长度不足: {len(data)} 字节")
            return None
    except Exception as e:
        logger.error(f"处理IMU数据出错: {e}")
        return None

# 校准函数
async def calibrate_imu(websocket):
    """
    校准IMU传感器
    """
    global accel_buffer, gyro_buffer
    
    logger.info("开始校准IMU传感器...")
    await websocket.send(json.dumps({"status": "calibration_started"}))
    
    # 清空缓冲区
    accel_buffer.clear()
    gyro_buffer.clear()
    
    # 等待缓冲区填满静止数据
    while len(accel_buffer) < WINDOW_SIZE:
        await asyncio.sleep(0.1)
    
    # 校准完成
    logger.info("IMU校准完成")
    await websocket.send(json.dumps({"status": "calibration_completed"}))

# BLE通知回调
def notification_handler(sender, data):
    """处理来自BLE设备的通知"""
    processed_data = process_imu_data(data)
    if processed_data:
        # 发送到所有连接的WebSocket客户端
        websocket_send_task = asyncio.create_task(broadcast_message(processed_data))

async def broadcast_message(message):
    """广播消息到所有WebSocket客户端"""
    if connected_clients:
        await asyncio.gather(*[client.send(message) for client in connected_clients])

# WebSocket连接处理函数
async def websocket_handler(websocket):
    """处理WebSocket连接"""
    logger.info(f"WebSocket客户端连接: {websocket.remote_address}")
    connected_clients.add(websocket)
    try:
        async for message in websocket:
            # 处理来自Unity的消息
            try:
                data = json.loads(message)
                if "command" in data:
                    if data["command"] == "ping":
                        await websocket.send(json.dumps({"response": "pong"}))
                    elif data["command"] == "calibrate":
                        await calibrate_imu(websocket)
                    elif data["command"] == "set_thresholds":
                        # 允许Unity调整阈值
                        global STOMP_ACCEL_THRESHOLD, KICK_GYRO_THRESHOLD, COOLDOWN_TIME, MOTION_COMPLETION_TIME
                        if "stomp_threshold" in data:
                            STOMP_ACCEL_THRESHOLD = float(data["stomp_threshold"])
                        if "kick_threshold" in data:
                            KICK_GYRO_THRESHOLD = float(data["kick_threshold"])
                        if "cooldown_time" in data:
                            COOLDOWN_TIME = float(data["cooldown_time"])
                        if "motion_completion_time" in data:
                            MOTION_COMPLETION_TIME = float(data["motion_completion_time"])
                        logger.info(f"更新阈值: 跺脚={STOMP_ACCEL_THRESHOLD}G, 踢腿={KICK_GYRO_THRESHOLD}°/s")
                        logger.info(f"更新时间: 冷却={COOLDOWN_TIME}秒, 完成={MOTION_COMPLETION_TIME}秒")
                        await websocket.send(json.dumps({"status": "thresholds_updated"}))
                    elif data["command"] == "debug_mode":
                        # 添加调试模式命令
                        debug_mode = data.get("enabled", False)
                        if debug_mode:
                            logger.info("调试模式已启用，将发送传感器数据")
                        else:
                            logger.info("调试模式已禁用")
                        # 这里可以添加用于发送原始传感器数据的逻辑
            except json.JSONDecodeError:
                logger.warning(f"收到非JSON消息: {message}")
    except websockets.exceptions.ConnectionClosed:
        logger.info("WebSocket连接已关闭")
    finally:
        connected_clients.remove(websocket)

# 扫描并连接BLE设备
async def scan_and_connect():
    """扫描并连接到BLE设备"""
    logger.info(f"开始扫描BLE设备: {DEVICE_NAME}")
    
    device = None
    
    # 尝试直接使用地址连接
    logger.info(f"尝试直接使用地址连接: {DEVICE_ADDRESS}")
    device = await BleakScanner.find_device_by_address(DEVICE_ADDRESS)
    
    # 如果通过地址没找到，尝试扫描
    if not device:
        logger.info("通过地址未找到设备，开始扫描...")
        # 尝试扫描设备
        for _ in range(3):  # 尝试3次
            devices = await BleakScanner.discover()
            logger.info(f"发现了 {len(devices)} 个蓝牙设备")
            for d in devices:
                logger.info(f"发现设备: {d.name} ({d.address})")
                if d.address == DEVICE_ADDRESS or (d.name and DEVICE_NAME.lower() in d.name.lower()):
                    device = d
                    break
            
            if device:
                break
            
            logger.info("未找到设备，重试中...")
            await asyncio.sleep(2)
    
    if not device:
        logger.error(f"无法找到设备: {DEVICE_NAME} 或地址 {DEVICE_ADDRESS}")
        return
    
    logger.info(f"正在连接到设备: {getattr(device, 'name', 'Unknown')} ({device.address})")
    
    client = BleakClient(device)
    
    try:
        await client.connect()
        logger.info("已连接")
        
        # 获取设备服务和特征并存储特征的handle
        target_char_handle = None
        target_service_uuid = None
        
        for service in client.services:
            logger.info(f"发现服务: {service.uuid}")
            for char in service.characteristics:
                logger.info(f"  特征: {char.uuid}, 属性: {char.properties}, handle: {char.handle}")
                
                # 找到我们想要的特征（在正确的服务下）
                if service.uuid.lower() == SERVICE_UUID.lower() and char.uuid.lower() == CHARACTERISTIC_UUID.lower():
                    target_char_handle = char.handle
                    target_service_uuid = service.uuid
                    logger.info(f"找到目标特征，handle: {target_char_handle}")
        
        if target_char_handle is None:
            logger.error("未找到目标特征")
            return
            
        # 订阅特征
        logger.info(f"正在订阅特征: handle={target_char_handle}")
        await client.start_notify(target_char_handle, notification_handler)
        
        # 保持脚本运行
        while True:
            await asyncio.sleep(1)
            
    except Exception as e:
        logger.error(f"连接或通信错误: {e}")
    finally:
        await client.disconnect()
        logger.info("已断开连接")

# 主函数
async def main():
    """主函数"""
    # 启动WebSocket服务器
    websocket_server = await websockets.serve(websocket_handler, "localhost", WEBSOCKET_PORT)
    logger.info(f"WebSocket服务器已启动: ws://localhost:{WEBSOCKET_PORT}")
    
    # 扫描并连接BLE设备
    ble_task = asyncio.create_task(scan_and_connect())
    
    # 保持服务器运行
    await asyncio.gather(websocket_server.wait_closed(), ble_task)

# 运行主函数
if __name__ == "__main__":
    asyncio.run(main())
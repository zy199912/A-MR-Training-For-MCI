import asyncio
import json
import websockets
import logging
import numpy as np
from bleak import BleakScanner, BleakClient
from collections import deque
import time

# 配置区域 - 根据你的设备修改
DEVICE_NAME = "im600-V3.11"  # 修改为扫描到的名称
SERVICE_UUID = "0000ae30-0000-1000-8000-00805f9b34fb"  # 服务UUID
CHARACTERISTIC_UUID = "0000ae02-0000-1000-8000-00805f9b34fb"  # 通知特征UUID
WEBSOCKET_PORT = 8765  # WebSocket服务器端口
DEVICE_ADDRESS = "19:6F:51:5D:D5:D6"  # 扫描到的设备地址

# 设置日志
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# 保存连接的WebSocket客户端
connected_clients = set()

# 序列检测参数
WINDOW_SIZE = 80  # 增大窗口，捕捉完整动作
DETECTION_WINDOW = 50
COOLDOWN_TIME = 2.5  # 增加冷却时间，避免重复检测

# 数据缓冲区
accel_buffer = deque(maxlen=WINDOW_SIZE)
gyro_buffer = deque(maxlen=WINDOW_SIZE)
time_buffer = deque(maxlen=WINDOW_SIZE)

# 状态管理
last_detection_time = 0
detection_stats = {"stomp": 0, "kick": 0, "total_processed": 0}

class SequentialMotionDetector:
    def __init__(self):
        # 运动状态跟踪
        self.motion_state = "idle"  # idle, building, analyzing
        self.motion_start_time = 0
        self.motion_data_buffer = []
        self.min_motion_duration = 0.5  # 最小运动持续时间
        self.max_motion_duration = 3.0  # 最大运动持续时间
        self.motion_intensity_threshold = 0.12
        
        # 基于真实数据的运动模式特征
        self.motion_patterns = {
            'stomp': {
                # 跺脚完整序列特征
                'total_duration_range': (0.8, 2.5),      # 跺脚持续时间
                'peak_intensity_threshold': 0.5,          # 峰值强度
                'z_range_threshold': 3.0,                 # Z轴范围大
                'sharp_transition_count': 2,              # 尖锐变化次数多
                'final_gyro_range': (15, 120),           # 最终角速度范围
            },
            'kick': {
                # 踢腿完整序列特征  
                'total_duration_range': (1.0, 3.5),      # 踢腿持续时间更长
                'peak_intensity_threshold': 0.4,          # 峰值强度
                'z_range_threshold': 1.5,                 # Z轴范围相对小
                'smooth_transition_score': 0.3,           # 平滑变化更多
                'final_gyro_range': (50, 100),           # 最终角速度范围
            }
        }
        
    def detect_motion_start(self, current_features):
        """检测运动开始"""
        motion_intensity = (current_features['x_std'] + 
                           current_features['z_std'] + 
                           current_features['y_gyro_std']/100)
        
        return motion_intensity > self.motion_intensity_threshold
    
    def detect_motion_end(self, recent_features_list):
        """检测运动结束"""
        if len(recent_features_list) < 3:
            return False
        
        # 检查最近几个窗口的运动强度是否都很低
        recent_intensities = []
        for features in recent_features_list[-3:]:
            intensity = (features['x_std'] + 
                        features['z_std'] + 
                        features['y_gyro_std']/100)
            recent_intensities.append(intensity)
        
        # 如果最近的强度都低于阈值，认为运动结束
        return all(intensity < self.motion_intensity_threshold * 0.5 for intensity in recent_intensities)
        
    def extract_motion_features(self, accel_data, gyro_data, time_data):
        """提取运动特征"""
        features = {
            # 基础统计特征
            'x_std': np.std(accel_data['x']),
            'z_std': np.std(accel_data['z']),
            'y_gyro_std': np.std(gyro_data['y']),
            'x_range': np.max(accel_data['x']) - np.min(accel_data['x']),
            'z_range': np.max(accel_data['z']) - np.min(accel_data['z']),
            'y_gyro_range': np.max(gyro_data['y']) - np.min(gyro_data['y']),
            
            # 时间特征
            'duration': time_data[-1] - time_data[0] if len(time_data) > 1 else 0,
            
            # 峰值特征
            'peak_count_x': len([v for v in accel_data['x'] if abs(v) > np.std(accel_data['x']) * 2]),
            'peak_count_z': len([v for v in accel_data['z'] if abs(v) > np.std(accel_data['z']) * 2]),
            
            # 运动模式特征
            'max_intensity': 0,
            'transition_sharpness': 0,
            'motion_smoothness': 0,
        }
        
        # 计算最大运动强度
        intensities = []
        for i in range(len(accel_data['x'])):
            intensity = abs(accel_data['x'][i]) + abs(accel_data['z'][i]) + abs(gyro_data['y'][i])/100
            intensities.append(intensity)
        
        features['max_intensity'] = max(intensities) if intensities else 0
        
        # 计算变化尖锐度（跺脚应该更尖锐）
        if len(accel_data['x']) > 4:
            x_diff2 = np.diff(np.diff(accel_data['x']))
            z_diff2 = np.diff(np.diff(accel_data['z']))
            features['transition_sharpness'] = np.var(x_diff2) + np.var(z_diff2)
        
        # 计算运动平滑度（踢腿应该更平滑）
        if len(accel_data['x']) > 4:
            x_smooth = 1.0 / (1.0 + np.var(np.diff(accel_data['x'])))
            z_smooth = 1.0 / (1.0 + np.var(np.diff(accel_data['z'])))
            features['motion_smoothness'] = (x_smooth + z_smooth) / 2
        
        return features
    
    def analyze_complete_motion(self, motion_data):
        """分析完整运动序列"""
        if len(motion_data) < 10:
            return None
        
        # 提取所有数据
        accel_x = [d['accel']['x'] for d in motion_data]
        accel_z = [d['accel']['z'] for d in motion_data]
        gyro_y = [d['gyro']['y'] for d in motion_data]
        time_data = [d['timestamp'] for d in motion_data]
        
        accel_data = {'x': accel_x, 'z': accel_z}
        gyro_data = {'y': gyro_y}
        
        # 提取完整运动特征
        features = self.extract_motion_features(accel_data, gyro_data, time_data)
        
        logger.info(f"🔍 分析完整动作序列:")
        logger.info(f"   持续时间: {features['duration']:.2f}秒")
        logger.info(f"   特征值: X_std={features['x_std']:.3f}, Z_std={features['z_std']:.3f}, Y_gyro={features['y_gyro_std']:.1f}")
        logger.info(f"   范围值: X_range={features['x_range']:.2f}, Z_range={features['z_range']:.2f}")
        logger.info(f"   运动特征: 最大强度={features['max_intensity']:.2f}, 尖锐度={features['transition_sharpness']:.3f}, 平滑度={features['motion_smoothness']:.3f}")
        
        # 基于完整序列分类
        return self.classify_complete_motion(features)
    
    def classify_complete_motion(self, features):
        """基于完整运动序列分类"""
        
        stomp_score = 0
        kick_score = 0
        reasons = []
        
        # 特征1: 持续时间
        duration = features['duration']
        stomp_duration_range = self.motion_patterns['stomp']['total_duration_range']
        kick_duration_range = self.motion_patterns['kick']['total_duration_range']
        
        if stomp_duration_range[0] <= duration <= stomp_duration_range[1]:
            stomp_score += 2.0
            reasons.append(f"持续时间{duration:.2f}s符合跺脚范围")
        
        if kick_duration_range[0] <= duration <= kick_duration_range[1]:
            kick_score += 2.0
            reasons.append(f"持续时间{duration:.2f}s符合踢腿范围")
        
        # 特征2: Z轴范围（关键区分特征）
        z_range = features['z_range']
        if z_range > self.motion_patterns['stomp']['z_range_threshold']:
            stomp_score += 3.0
            reasons.append(f"Z轴范围{z_range:.2f}G大，偏向跺脚")
        elif z_range < self.motion_patterns['kick']['z_range_threshold']:
            kick_score += 2.5
            reasons.append(f"Z轴范围{z_range:.2f}G小，偏向踢腿")
        
        # 特征3: 最终角速度
        gyro_std = features['y_gyro_std']
        stomp_gyro_range = self.motion_patterns['stomp']['final_gyro_range']
        kick_gyro_range = self.motion_patterns['kick']['final_gyro_range']
        
        if stomp_gyro_range[0] <= gyro_std <= stomp_gyro_range[1]:
            stomp_score += 2.5
            reasons.append(f"角速度{gyro_std:.1f}°/s符合跺脚范围")
        
        if kick_gyro_range[0] <= gyro_std <= kick_gyro_range[1]:
            kick_score += 2.5
            reasons.append(f"角速度{gyro_std:.1f}°/s符合踢腿范围")
        
        # 特征4: 运动尖锐度 vs 平滑度
        sharpness = features['transition_sharpness']
        smoothness = features['motion_smoothness']
        
        if sharpness > 0.5:  # 跺脚更尖锐
            stomp_score += 2.0
            reasons.append(f"运动尖锐度{sharpness:.3f}高，偏向跺脚")
        
        if smoothness > 0.3:  # 踢腿更平滑
            kick_score += 1.5
            reasons.append(f"运动平滑度{smoothness:.3f}高，偏向踢腿")
        
        # 特征5: 峰值强度
        max_intensity = features['max_intensity']
        if max_intensity > self.motion_patterns['stomp']['peak_intensity_threshold']:
            stomp_score += 1.5
            reasons.append(f"峰值强度{max_intensity:.2f}高，偏向跺脚")
        
        # 决策
        total_score = stomp_score + kick_score
        if total_score < 4.0:  # 总分太低，可能是噪声
            return None
        
        if stomp_score > kick_score and stomp_score > 5.0:
            confidence = min(stomp_score / 10.0, 0.95)
            return {
                "action": "stomp",
                "confidence": confidence,
                "scores": {"stomp": stomp_score, "kick": kick_score},
                "reasons": reasons,
                "features": features
            }
        elif kick_score > stomp_score and kick_score > 5.0:
            confidence = min(kick_score / 10.0, 0.95)
            return {
                "action": "kick", 
                "confidence": confidence,
                "scores": {"stomp": stomp_score, "kick": kick_score},
                "reasons": reasons,
                "features": features
            }
        
        return None
    
    def process_motion_sequence(self, accel_data, gyro_data, timestamp):
        """处理运动序列"""
        global last_detection_time, detection_stats
        
        current_time = timestamp
        detection_stats["total_processed"] += 1
        
        # 添加数据到缓冲区
        accel_buffer.append(accel_data)
        gyro_buffer.append(gyro_data)
        time_buffer.append(current_time)
        
        if len(accel_buffer) < 20:
            return None
        
        # 提取当前窗口特征
        window_size = 15
        recent_accel = {
            'x': [a["x"] for a in list(accel_buffer)[-window_size:]],
            'z': [a["z"] for a in list(accel_buffer)[-window_size:]]
        }
        recent_gyro = {
            'y': [g["y"] for g in list(gyro_buffer)[-window_size:]]
        }
        recent_time = list(time_buffer)[-window_size:]
        
        current_features = self.extract_motion_features(recent_accel, recent_gyro, recent_time)
        
        # 状态机处理
        if self.motion_state == "idle":
            # 检测运动开始
            if self.detect_motion_start(current_features):
                self.motion_state = "building"
                self.motion_start_time = current_time
                self.motion_data_buffer = []
                logger.info("🎬 检测到运动开始")
        
        elif self.motion_state == "building":
            # 收集运动数据
            motion_point = {
                'timestamp': current_time,
                'accel': accel_data.copy(),
                'gyro': gyro_data.copy()
            }
            self.motion_data_buffer.append(motion_point)
            
            # 检查是否运动结束或超时
            motion_duration = current_time - self.motion_start_time
            
            if motion_duration > self.max_motion_duration:
                # 运动超时，强制分析
                logger.info(f"⏰ 运动超时({motion_duration:.1f}s)，开始分析")
                self.motion_state = "analyzing"
            elif (motion_duration > self.min_motion_duration and 
                  self.detect_motion_end([current_features])):
                # 运动自然结束
                logger.info(f"🛑 检测到运动结束({motion_duration:.1f}s)，开始分析")
                self.motion_state = "analyzing"
        
        elif self.motion_state == "analyzing":
            # 分析完整运动
            result = self.analyze_complete_motion(self.motion_data_buffer)
            
            # 重置状态
            self.motion_state = "idle"
            self.motion_data_buffer = []
            
            if result and current_time - last_detection_time > COOLDOWN_TIME:
                last_detection_time = current_time
                detection_stats[result["action"]] = detection_stats.get(result["action"], 0) + 1
                
                logger.info(f"🎯 完整动作识别: {result['action']} (置信度: {result['confidence']:.2f})")
                logger.info(f"   得分: 跺脚={result['scores']['stomp']:.1f}, 踢腿={result['scores']['kick']:.1f}")
                logger.info(f"   主要原因: {'; '.join(result['reasons'][:3])}")
                
                return result
        
        return None

# 创建序列检测器
detector = SequentialMotionDetector()

# 处理IMU数据的函数
def process_imu_data(data):
    """处理来自IMU的原始数据并转换为JSON格式"""
    try:
        # 记录原始数据
        current_time = time.time()
        
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
            
            # 使用新的序列检测算法
            detection_result = detector.process_motion_sequence(accel_data, gyro_data, current_time)
            
            # 如果检测到动作，则发送动作类型
            if detection_result:
                logger.info(f"发送动作事件: {detection_result['action']}")
                return json.dumps({
                    "motion_type": detection_result["action"],
                    "confidence": detection_result["confidence"],
                    "timestamp": current_time,
                    "scores": detection_result["scores"],
                    "reasons": detection_result["reasons"],
                    "stats": detection_stats,
                    "algorithm": "Sequential Motion Detection"
                })
            return None
        else:
            logger.warning(f"数据长度不足: {len(data)} 字节")
            return None
    except Exception as e:
        logger.error(f"处理IMU数据出错: {e}")
        return None

# 校准函数 - 保持原有功能
async def calibrate_imu(websocket):
    """校准IMU传感器"""
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
        disconnected_clients = set()
        for client in connected_clients.copy():
            try:
                await client.send(message)
            except:
                disconnected_clients.add(client)
        
        for client in disconnected_clients:
            connected_clients.discard(client)

# WebSocket连接处理函数
async def websocket_handler(websocket):
    """处理WebSocket连接"""
    logger.info(f"WebSocket客户端连接: {websocket.remote_address}")
    connected_clients.add(websocket)
    
    # 发送连接成功消息
    try:
        await websocket.send(json.dumps({
            "status": "connected",
            "message": "序列动作检测算法已启用",
            "algorithm": "Sequential Motion Detection",
            "features": [
                "完整动作序列分析",
                "避免中间过程误触发",
                "基于运动持续时间判断",
                "多特征综合评分"
            ]
        }))
    except:
        pass
    
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
                    elif data["command"] == "get_stats":
                        await websocket.send(json.dumps({
                            "stats": detection_stats,
                            "buffer_size": len(accel_buffer),
                            "motion_state": detector.motion_state
                        }))
                    elif data["command"] == "set_thresholds":
                        # 允许Unity调整序列检测器的阈值
                        if "motion_intensity_threshold" in data:
                            detector.motion_intensity_threshold = float(data["motion_intensity_threshold"])
                        if "min_motion_duration" in data:
                            detector.min_motion_duration = float(data["min_motion_duration"])
                        if "max_motion_duration" in data:
                            detector.max_motion_duration = float(data["max_motion_duration"])
                        if "cooldown_time" in data:
                            global COOLDOWN_TIME
                            COOLDOWN_TIME = float(data["cooldown_time"])
                        
                        logger.info(f"更新序列检测参数")
                        await websocket.send(json.dumps({"status": "thresholds_updated"}))
                    elif data["command"] == "debug_mode":
                        # 添加调试模式命令
                        debug_mode = data.get("enabled", False)
                        if debug_mode:
                            logger.info("调试模式已启用，将发送传感器数据")
                        else:
                            logger.info("调试模式已禁用")
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
        logger.info("✅ 设备连接成功")
        
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
        logger.info("🎬 序列动作检测算法已启动")
        
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
    try:
        # 启动WebSocket服务器
        websocket_server = await websockets.serve(websocket_handler, "localhost", WEBSOCKET_PORT)
        logger.info(f"WebSocket服务器已启动: ws://localhost:{WEBSOCKET_PORT}")
        
        # 扫描并连接BLE设备
        ble_task = asyncio.create_task(scan_and_connect())
        
        # 保持服务器运行
        await asyncio.gather(websocket_server.wait_closed(), ble_task)
    except KeyboardInterrupt:
        logger.info("程序被中断")
    except Exception as e:
        logger.error(f"主函数错误: {e}")

# 运行主函数
if __name__ == "__main__":
    try:
        print("🎬 序列动作检测系统")
        print("="*60)
        print("🎯 核心改进:")
        print("   ✅ 等待完整动作序列完成后再分析")
        print("   ✅ 避免中间过程误触发")
        print("   ✅ 状态机管理: idle → building → analyzing")
        print("   ✅ 基于完整序列的多特征分析")
        print("   ✅ 运动持续时间: 跺脚0.8-2.5s, 踢腿1.0-3.5s")
        print("   ✅ 增加冷却时间，避免重复检测")
        print("="*60)
        
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n程序被用户中断")
    except Exception as e:
        print(f"程序异常退出: {e}")
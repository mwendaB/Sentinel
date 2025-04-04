
import asyncio
import time
import threading
import logging
from typing import List, Dict, Any, Callable, Optional, Set
from concurrent.futures import ThreadPoolExecutor
from collections import deque
from datetime import datetime, timedelta
import ipaddress
import socket

from sqlalchemy.orm import Session
from scapy.all import sniff, IP, TCP, UDP, ICMP, Raw, Ether, rdpcap, wrpcap
import dpkt

from app.core.config import settings
from app.models.packet import Packet
from app.models.capture import Capture
from app.models.flow import Flow
from app.models.session import Session as DbSession
from app.models.host import Host
from app.services.flow_tracker import FlowTracker
from app.services.protocol_analyzer import ProtocolAnalyzer
from app.utils.filtering import parse_bpf_filter
from app.utils.logging import setup_logger

logger = setup_logger(__name__)

class CircularBufferPacketStore:
    """Circular buffer for storing packets before database commit"""
    
    def __init__(self, max_size: int = 10000):
        self.buffer = deque(maxlen=max_size)
        self.lock = threading.Lock()
    
    def add_packet(self, packet_data: Dict[str, Any]) -> None:
        with self.lock:
            self.buffer.append(packet_data)
    
    def get_packets(self, count: Optional[int] = None) -> List[Dict[str, Any]]:
        with self.lock:
            if count is None:
                return list(self.buffer)
            return list(self.buffer)[-count:]
    
    def clear(self) -> None:
        with self.lock:
            self.buffer.clear()
    
    def is_empty(self) -> bool:
        with self.lock:
            return len(self.buffer) == 0
    
    def size(self) -> int:
        with self.lock:
            return len(self.buffer)

class PacketCaptureService:
    def __init__(self, db: Session):
        self.db = db
        self.active_captures: Dict[int, Dict[str, Any]] = {}
        self.packet_buffer = CircularBufferPacketStore(max_size=settings.MAX_STORED_PACKETS)
        self.flow_tracker = FlowTracker(db)
        self.protocol_analyzer = ProtocolAnalyzer()
        self.packet_callbacks: Dict[int, List[Callable[[Dict[str, Any]], None]]] = {}
        self.executor = ThreadPoolExecutor(max_workers=4)
        self.stats: Dict[int, Dict[str, Any]] = {}
        self.db_commit_interval = 2.0  # seconds
        
        # Start background task for periodic database commits
        self.running = True
        self.commit_thread = threading.Thread(target=self._db_commit_loop)
        self.commit_thread.daemon = True
        self.commit_thread.start()
    
    def start_capture(self, 
                     interface: str, 
                     filter_str: Optional[str] = None, 
                     name: Optional[str] = None,
                     description: Optional[str] = None,
                     user_id: Optional[int] = None) -> Dict[str, Any]:
        """Start packet capture on the specified interface."""
        try:
            # Create capture record in database
            capture = Capture(
                name=name or f"Capture on {interface}",
                description=description,
                interface=interface,
                filter=filter_str,
                start_time=time.time(),
                status="active",
                user_id=user_id,
                settings={
                    "max_packets": settings.MAX_STORED_PACKETS,
                    "retention_days": settings.PACKET_RETENTION_DAYS,
                    "geolocation_enabled": settings.ENABLE_GEOLOCATION,
                    "anomaly_detection": settings.ANOMALY_DETECTION
                }
            )
            
            self.db.add(capture)
            self.db.commit()
            self.db.refresh(capture)
            
            # Initialize statistics for this capture
            self.stats[capture.id] = {
                "packet_count": 0,
                "bytes_captured": 0,
                "start_time": time.time(),
                "protocols": {}
            }
            
            # Initialize callbacks list for this capture
            self.packet_callbacks[capture.id] = []
            
            # Parse BPF filter if provided
            bpf_filter = None
            if filter_str:
                bpf_filter = parse_bpf_filter(filter_str)
            
            # Start capture thread
            capture_thread = threading.Thread(
                target=self._capture_packets,
                args=(capture.id, interface, bpf_filter)
            )
            capture_thread.daemon = True
            capture_thread.start()
            
            # Store active capture info
            self.active_captures[capture.id] = {
                "thread": capture_thread,
                "interface": interface,
                "filter": filter_str,
                "start_time": time.time(),
                "status": "active"
            }
            
            logger.info(f"Started capture {capture.id} on interface {interface}")
            
            return {
                "capture_id": capture.id,
                "status": "started",
                "interface": interface,
                "filter": filter_str
            }
            
        except Exception as e:
            logger.error(f"Error starting capture: {str(e)}")
            raise
    
    def stop_capture(self, capture_id: int) -> Dict[str, Any]:
        """Stop an active packet capture."""
        if capture_id not in self.active_captures:
            return {"status": "error", "message": f"Capture {capture_id} not found or not active"}
        
        # Update capture status
        self.active_captures[capture_id]["status"] = "stopping"
        
        # Wait for capture thread to finish gracefully (max 5 seconds)
        wait_start = time.time()
        while (self.active_captures[capture_id]["thread"].is_alive() and 
              time.time() - wait_start < 5.0):
            time.sleep(0.1)
        
        # Update database record
        try:
            capture = self.db.query(Capture).filter(Capture.id == capture_id).first()
            if capture:
                capture.status = "completed"
                capture.end_time = time.time()
                capture.packet_count = self.stats[capture_id]["packet_count"]
                capture.bytes_captured = self.stats[capture_id]["bytes_captured"]
                
                self.db.commit()
                logger.info(f"Stopped capture {capture_id}")
        except Exception as e:
            logger.error(f"Error updating capture status: {str(e)}")
        
        # Clean up
        if capture_id in self.active_captures:
            del self.active_captures[capture_id]
        
        # Return stats
        return {
            "status": "stopped",
            "capture_id": capture_id,
            "statistics": self.stats.get(capture_id, {})
        }
    
    def get_active_captures(self) -> List[Dict[str, Any]]:
        """Get list of active captures."""
        result = []
        for capture_id, info in self.active_captures.items():
            capture = self.db.query(Capture).filter(Capture.id == capture_id).first()
            if capture:
                result.append({
                    "id": capture.id,
                    "name": capture.name,
                    "interface": capture.interface,
                    "filter": capture.filter,
                    "start_time": capture.start_time,
                    "packet_count": self.stats[capture_id]["packet_count"],
                    "bytes_captured": self.stats[capture_id]["bytes_captured"],
                    "duration": time.time() - info["start_time"]
                })
        return result
    
    def get_capture_details(self, capture_id: int) -> Dict[str, Any]:
        """Get detailed information about a capture."""
        capture = self.db.query(Capture).filter(Capture.id == capture_id).first()
        if not capture:
            return {"status": "error", "message": f"Capture {capture_id} not found"}
        
        # Get statistics
        stats = self.stats.get(capture_id, {})
        if not stats and capture.status != "active":
            # For completed captures, compute stats from database
            packet_count = self.db.query(Packet).filter(Packet.capture_id == capture_id).count()
            bytes_captured = self.db.query(Packet.size).filter(Packet.capture_id == capture_id).all()
            bytes_total = sum(size[0] for size in bytes_captured) if bytes_captured else 0
            
            stats = {
                "packet_count": packet_count,
                "bytes_captured": bytes_total,
                "start_time": capture.start_time,
                "duration": (capture.end_time or time.time()) - capture.start_time
            }
        
        # Get flow statistics
        flow_count = self.db.query(Flow).filter(Flow.capture_id == capture_id).count()
        
        # Get protocol distribution
        protocol_stats = {}
        protocol_query = self.db.query(
            Packet.protocol, 
            func.count(Packet.id).label("count")
        ).filter(
            Packet.capture_id == capture_id
        ).group_by(
            Packet.protocol
        ).all()
        
        for protocol, count in protocol_stats:
            protocol_stats[protocol] = count
        
        # Construct result
        return {
            "id": capture.id,
            "name": capture.name,
            "description": capture.description,
            "interface": capture.interface,
            "filter": capture.filter,
            "status": capture.status,
            "start_time": capture.start_time,
            "end_time": capture.end_time,
            "duration": (capture.end_time or time.time()) - capture.start_time,
            "user_id": capture.user_id,
            "statistics": {
                "packet_count": stats.get("packet_count", 0),
                session = self.active_sessions[session_key]
            
            # Update session
            session["last_seen"] = now
            session["packet_count"] += 1
            
            # Check for application layer data
            if "payload_data" in packet_data and packet_data["payload_data"]:
                # Append to session data for protocols that support reconstruction
                if session["app_protocol"] in ("HTTP", "SMTP", "DNS"):
                    session["metadata"].update(packet_data["payload_data"])
            
            # Update session status based on protocol
            if packet_data["protocol"] == "TCP" and "flags" in packet_data:
                if "F" in packet_data["flags"]:  # FIN flag
                    session["status"] = "closed"
                    session["record"].status = "closed"
                    session["record"].end_time = now
            
            return session["id"]
        
        # Create new session
        capture_id = session_key[0]
        flow_key = session_key[1]
        app_protocol = packet_data.get("protocol")
        
        # Determine application protocol
        if app_protocol in ("TCP", "UDP") and "payload_data" in packet_data:
            # Try to determine app protocol from payload analysis
            if isinstance(packet_data["payload_data"], dict) and "protocol" in packet_data["payload_data"]:
                app_protocol = packet_data["payload_data"]["protocol"]
        
        # Create session record
        session_record = DbSession(
            capture_id=capture_id,
            flow_id=flow_id,
            protocol=packet_data["protocol"],
            app_protocol=app_protocol,
            start_time=now,
            end_time=None,
            source_ip=flow_key.src_ip,
            destination_ip=flow_key.dst_ip,
            source_port=flow_key.src_port,
            destination_port=flow_key.dst_port,
            status="active",
            metadata={} if "payload_data" not in packet_data else packet_data["payload_data"]
        )
        
        # Add to database
        self.db.add(session_record)
        self.db.commit()
        self.db.refresh(session_record)
        
        # Store in memory
        self.active_sessions[session_key] = {
            "id": session_record.id,
            "start_time": now,
            "last_seen": now,
            "packet_count": 1,
            "app_protocol": app_protocol,
            "status": "active",
            "metadata": {} if "payload_data" not in packet_data else packet_data["payload_data"].copy(),
            "record": session_record
        }
        
        return session_record.id
    
    def _update_flow_record(self, flow: Dict[str, Any]) -> None:
        """Update flow record in database with latest statistics."""
        try:
            record = flow["record"]
            record.packet_count = flow["packet_count"]
            record.bytes_sent = flow["bytes"]  # Simplified, should track direction
            record.end_time = flow["last_seen"]
            record.status = flow["status"]
            
            self.db.commit()
        except Exception as e:
            logger.error(f"Error updating flow record: {str(e)}")
            self.db.rollback()
    
    def _cleanup_stale_flows(self) -> None:
        """Clean up stale flows and sessions that have timed out."""
        now = time.time()
        
        # Clean up stale flows
        for key, flow in list(self.active_flows.items()):
            if now - flow["last_seen"] > self.flow_timeout:
                # Mark as timeout in database
                try:
                    record = flow["record"]
                    record.status = "timeout"
                    record.end_time = flow["last_seen"]
                    self.db.commit()
                except:
                    self.db.rollback()
                
                # Remove from memory
                del self.active_flows[key]
        
        # Clean up stale sessions
for key, session in list(self.active_sessions.items()):
    if now - session["last_seen"] > self.session_timeout:
        # Mark as timeout in database
        try:
            record = session["record"]
            record.status = "timeout"
            record.end_time = session["last_seen"]
            self.db.commit()
        except sqlalchemy.exc.SQLAlchemyError as e:
            logger.error(f"Database error cleaning up session {key}: {str(e)}")
            self.db.rollback()
        except Exception as e:
            logger.error(f"Unexpected error cleaning up session {key}: {str(e)}")
            self.db.rollback()
        finally:
            # Remove from memory regardless of database operation result
            del self.active_sessions[key]
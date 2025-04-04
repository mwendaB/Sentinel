# app/services/anomaly_detector.py
import time
import statistics
from typing import Dict, Any, List, Tuple, Set, Optional
from collections import defaultdict, deque
from datetime import datetime, timedelta
import logging
from sqlalchemy.orm import Session

from app.models.alert import Alert
from app.core.config import settings
from app.utils.logging import setup_logger

logger = setup_logger(__name__)

class AnomalyDetector:
    """Service for detecting anomalies in network traffic patterns."""
    
    def __init__(self, db: Session):
        self.db = db
        self.enabled = settings.ANOMALY_DETECTION
        
        # Traffic baselines by source IP
        self.ip_traffic_history: Dict[str, deque] = defaultdict(lambda: deque(maxlen=100))
        
        # Port scan detection
        self.scan_thresholds = {
            "tcp_ports_per_minute": 20,  # Threshold for TCP port scan
            "udp_ports_per_minute": 15,  # Threshold for UDP port scan
            "hosts_per_minute": 10       # Threshold for horizontal scan
        }
        
        # Track connection attempts for port scan detection
        self.connection_attempts: Dict[str, Dict[str, Set[int]]] = defaultdict(
            lambda: {
                "tcp": set(),
                "udp": set(),
                "targets": set(),
                "last_reset": time.time()
            }
        )
        
        # Traffic volume baselines
        self.baseline_window = 300  # 5 minutes
        self.traffic_baselines: Dict[str, Dict[str, Any]] = {}
        
        # Known protocols and ports for unexpected service detection
        self.known_port_protocols = {
            80: "HTTP", 443: "HTTPS", 22: "SSH", 21: "FTP",
            23: "TELNET", 25: "SMTP", 53: "DNS", 67: "DHCP",
            68: "DHCP", 110: "POP3", 143: "IMAP", 389: "LDAP",
            636: "LDAPS", 993: "IMAPS", 995: "POP3S"
        }
    
    def process_packet(self, packet_data: Dict[str, Any], capture_id: int) -> List[Dict[str, Any]]:
        """Process a packet and detect anomalies."""
        if not self.enabled:
            return []
        
        alerts = []
        
        # Extract basic packet info
        src_ip = packet_data.get("source_ip")
        dst_ip = packet_data.get("destination_ip")
        protocol = packet_data.get("protocol")
        src_port = packet_data.get("source_port")
        dst_port = packet_data.get("destination_port")
        
        # Skip if missing essential data
        if not all([src_ip, dst_ip, protocol]):
            return []
        
        # Update traffic history
        self._update_traffic_history(src_ip, packet_data)
        
        # Check for port scans
        port_scan_alert = self._detect_port_scan(src_ip, dst_ip, protocol, dst_port)
        if port_scan_alert:
            alerts.append(self._create_alert(
                capture_id, 
                "Port Scan Detected", 
                port_scan_alert, 
                "medium", 
                "intrusion",
                src_ip, 
                dst_ip, 
                protocol
            ))
        
        # Check for unusual traffic patterns
        if self._is_traffic_anomaly(src_ip, packet_data):
            alerts.append(self._create_alert(
                capture_id,
                "Unusual Traffic Volume",
                f"Unusual traffic pattern detected from {src_ip}",
                "low",
                "anomaly",
                src_ip,
                dst_ip,
                protocol
            ))
        
        # Check for unexpected services on non-standard ports
        service_alert = self._detect_unexpected_service(protocol, src_port, dst_port)
        if service_alert:
            alerts.append(self._create_alert(
                capture_id,
                "Unexpected Service",
                service_alert,
                "low",
                "anomaly",
                src_ip,
                dst_ip,
                protocol
            ))
        
        # Save alerts to database if any were generated
        self._save_alerts(alerts)
        
        return alerts
    
    def _update_traffic_history(self, ip: str, packet_data: Dict[str, Any]) -> None:
        """Update traffic history for a specific IP."""
        now = time.time()
        self.ip_traffic_history[ip].append({
            "timestamp": now,
            "size": packet_data.get("size", 0),
            "protocol": packet_data.get("protocol", ""),
        })
        
        # Update baseline if needed
        if ip not in self.traffic_baselines or now - self.traffic_baselines[ip]["last_update"] > 60:
            # Calculate baseline if we have enough history
            if len(self.ip_traffic_history[ip]) >= 10:
                recent_packets = list(self.ip_traffic_history[ip])
                sizes = [p["size"] for p in recent_packets]
                
                self.traffic_baselines[ip] = {
                    "last_update": now,
                    "avg_packet_size": statistics.mean(sizes) if sizes else 0,
                    "std_packet_size": statistics.stdev(sizes) if len(sizes) > 1 else 0,
                    "packets_per_minute": len(
                        [p for p in recent_packets if now - p["timestamp"] <= 60]
                    ),
                    "protocols": {
                        proto: sum(1 for p in recent_packets if p["protocol"] == proto)
                        for proto in set(p["protocol"] for p in recent_packets)
                    }
                }
    
    def _detect_port_scan(self, src_ip: str, dst_ip: str, protocol: str, dst_port: Optional[int]) -> Optional[str]:
        """Detect potential port scanning behavior."""
        now = time.time()
        
        # Reset counters if needed (every minute)
        if now - self.connection_attempts[src_ip]["last_reset"] > 60:
            self.connection_attempts[src_ip] = {
                "tcp": set(),
                "udp": set(),
                "targets": set(),
                "last_reset": now
            }
        
        # Skip if no destination port (not a connection attempt)
        if dst_port is None:
            return None
        
        # Add to connection tracking
        self.connection_attempts[src_ip]["targets"].add(dst_ip)
        
        if protocol.lower() == "tcp":
            self.connection_attempts[src_ip]["tcp"].add((dst_ip, dst_port))
        elif protocol.lower() == "udp":
            self.connection_attempts[src_ip]["udp"].add((dst_ip, dst_port))
        
        # Check for TCP port scan
        tcp_ports = len(self.connection_attempts[src_ip]["tcp"])
        if tcp_ports > self.scan_thresholds["tcp_ports_per_minute"]:
            # Check if multiple ports on same host (vertical scan)
            tcp_targets = {target for target, _ in self.connection_attempts[src_ip]["tcp"]}
            if len(tcp_targets) == 1:
                return f"Potential TCP port scan: {src_ip} probed {tcp_ports} ports on {next(iter(tcp_targets))}"
        
        # Check for UDP port scan
        udp_ports = len(self.connection_attempts[src_ip]["udp"])
        if udp_ports > self.scan_thresholds["udp_ports_per_minute"]:
            # Check if multiple ports on same host (vertical scan)
            udp_targets = {target for target, _ in self.connection_attempts[src_ip]["udp"]}
            if len(udp_targets) == 1:
                return f"Potential UDP port scan: {src_ip} probed {udp_ports} ports on {next(iter(udp_targets))}"
        
        # Check for horizontal scan (multiple hosts)
        hosts_count = len(self.connection_attempts[src_ip]["targets"])
        if hosts_count > self.scan_thresholds["hosts_per_minute"]:
            # Check if targeting same port (horizontal scan)
            tcp_ports = {port for _, port in self.connection_attempts[src_ip]["tcp"]}
            udp_ports = {port for _, port in self.connection_attempts[src_ip]["udp"]}
            
            if len(tcp_ports) == 1 or len(udp_ports) == 1:
                target_port = next(iter(tcp_ports)) if len(tcp_ports) == 1 else next(iter(udp_ports))
                return f"Potential horizontal scan: {src_ip} probed {hosts_count} hosts on port {target_port}"
        
        return None
    
    def _is_traffic_anomaly(self, ip: str, packet_data: Dict[str, Any]) -> bool:
        """Detect if current traffic deviates significantly from baseline."""
        if ip not in self.traffic_baselines:
            return False
        
        baseline = self.traffic_baselines[ip]
        
        # Check packet size anomaly (if deviates more than 3 standard deviations)
        size = packet_data.get("size", 0)
        if baseline["std_packet_size"] > 0:
            z_score = abs(size - baseline["avg_packet_size"]) / baseline["std_packet_size"]
            if z_score > 3.0:
                return True
        
        # Check for sudden traffic increase
        recent_packets = sum(
            1 for p in self.ip_traffic_history[ip] 
            if time.time() - p["timestamp"] <= 10  # Last 10 seconds
        )
        
        packets_per_10s = baseline["packets_per_minute"] / 6.0
        if packets_per_10s > 0 and recent_packets > packets_per_10s * 3:
            return True
        
        return False
    
    def _detect_unexpected_service(self, protocol: str, src_port: Optional[int], dst_port: Optional[int]) -> Optional[str]:
        """Detect services running on non-standard ports."""
        if not src_port or not dst_port:
            return None
        
        # Check if protocol matches expected port
        expected_protocol = self.known_port_protocols.get(dst_port)
        
        if expected_protocol and protocol != "TCP" and protocol != "UDP" and protocol != expected_protocol:
            return f"Unexpected protocol {protocol} on port {dst_port} (expected {expected_protocol})"
        
        return None
    
    def _create_alert(self, capture_id: int, title: str, description: str, 
                     severity: str, category: str, source_ip: str, 
                     destination_ip: str, protocol: str) -> Dict[str, Any]:
        """Create an alert object."""
        return {
            "capture_id": capture_id,
            "timestamp": time.time(),
            "severity": severity,
            "category": category,
            "title": title,
            "description": description,
            "source_ip": source_ip,
            "destination_ip": destination_ip,
            "protocol": protocol
        }
    
    def _save_alerts(self, alerts: List[Dict[str, Any]]) -> None:
        """Save alerts to the database."""
        if not alerts:
            return
            
        try:
            # Convert to Alert model objects
            alert_objects = [Alert(**alert) for alert in alerts]
            
            # Add all to database
            self.db.add_all(alert_objects)
            self.db.commit()
        except Exception as e:
            logger.error(f"Error saving alerts: {str(e)}")
            self.db.rollback()
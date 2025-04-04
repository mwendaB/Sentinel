from sqlalchemy import Column, Integer, String, Float, DateTime, Boolean, ForeignKey, Text, Index
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
from app.db.session import Base

class Alert(Base):
    __tablename__ = "alerts"

    id = Column(Integer, primary_key=True, index=True)
    capture_id = Column(Integer, ForeignKey("captures.id"), index=True)
    timestamp = Column(Float, index=True)
    severity = Column(String, index=True)  # critical, high, medium, low, info
    category = Column(String, index=True)  # intrusion, anomaly, policy, etc.
    title = Column(String, nullable=False)
    description = Column(Text)
    source_ip = Column(String, index=True, nullable=True)
    destination_ip = Column(String, index=True, nullable=True)
    protocol = Column(String, nullable=True)
    
    # Related objects
    packet_id = Column(BigInteger, ForeignKey("packets.id"), nullable=True)
    flow_id = Column(Integer, ForeignKey("flows.id"), nullable=True)
    session_id = Column(Integer, ForeignKey("sessions.id"), nullable=True)
    
    # Additional data
    metadata = Column(JSONB, default={})
    signature_id = Column(String, nullable=True)
    acknowledged = Column(Boolean, default=False)
    acknowledged_by = Column(Integer, ForeignKey("users.id"), nullable=True)
    acknowledged_at = Column(DateTime(timezone=True), nullable=True)
    false_positive = Column(Boolean, default=False)
    
    # Relationships
    capture = relationship("Capture", back_populates="alerts")
    user = relationship("User")
    
    # Timestamps
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    
    # Indexes
    __table_args__ = (
        Index('ix_alerts_capture_severity', 'capture_id', 'severity'),
        Index('ix_alerts_time_category', 'timestamp', 'category'),
    )
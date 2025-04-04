from sqlalchemy import Column, Integer, String, Float, DateTime, Boolean, ForeignKey, Index, Text
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
from app.db.session import Base

class Session(Base):
    __tablename__ = "sessions"

    id = Column(Integer, primary_key=True, index=True)
    capture_id = Column(Integer, ForeignKey("captures.id"), index=True)
    flow_id = Column(Integer, ForeignKey("flows.id"), index=True)
    protocol = Column(String, index=True)
    start_time = Column(Float, index=True)
    end_time = Column(Float, index=True, nullable=True)
    source_ip = Column(String, index=True)
    destination_ip = Column(String, index=True)
    source_port = Column(Integer)
    destination_port = Column(Integer)
    status = Column(String, default="active")  # active, closed, timeout
    
    # For protocol-specific sessions
    app_protocol = Column(String, index=True, nullable=True)  # HTTP, DNS, SMTP, etc.
    metadata = Column(JSONB, default={})
    reconstructed_data = Column(Text, nullable=True)  # For protocols like HTTP
    
    # Relationships
    capture = relationship("Capture", back_populates="sessions")
    flow = relationship("Flow")
    packets = relationship("Packet", back_populates="session")
    
    # Timestamps
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    updated_at = Column(DateTime(timezone=True), onupdate=func.now())
    
    # Indexes
    __table_args__ = (
        Index('ix_sessions_app_proto', 'app_protocol'),
        Index('ix_sessions_flow_time', 'flow_id', 'start_time'),
        Index('ix_sessions_src_dst_ports', 'source_ip', 'destination_ip', 'source_port', 'destination_port'),
    )
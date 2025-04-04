from sqlalchemy import Column, Integer, String, Float, DateTime, Boolean, ForeignKey, Text
from sqlalchemy.dialects.postgresql import JSONB
from sqlalchemy.orm import relationship
from sqlalchemy.sql import func
from app.db.session import Base

class Capture(Base):
    __tablename__ = "captures"

    id = Column(Integer, primary_key=True, index=True)
    name = Column(String, nullable=True)
    description = Column(Text, nullable=True)
    interface = Column(String)
    filter = Column(String, nullable=True)
    start_time = Column(Float, index=True)
    end_time = Column(Float, index=True, nullable=True)
    status = Column(String, default="active")  # active, completed, stopped, error
    user_id = Column(Integer, ForeignKey("users.id"), nullable=True)
    
    # Statistics
    packet_count = Column(Integer, default=0)
    bytes_captured = Column(BigInteger, default=0)
    
    # Settings used for this capture
    settings = Column(JSONB, default={})
    error_message = Column(Text, nullable=True)
    
    # Relationships
    user = relationship("User")
    packets = relationship("Packet", back_populates="capture")
    flows = relationship("Flow", back_populates="capture")
    sessions = relationship("Session", back_populates="capture")
    alerts = relationship("Alert", back_populates="capture")
    
    # Timestamps
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    updated_at = Column(DateTime(timezone=True), onupdate=func.now())
# app/core/config.py
from pydantic_settings import BaseSettings
from typing import List, Optional
import secrets
from pathlib import Path

class Settings(BaseSettings):
    API_V1_STR: str = "/api/v1"
    SECRET_KEY: str = secrets.token_urlsafe(32)
    ACCESS_TOKEN_EXPIRE_MINUTES: int = 60 * 24 * 8  # 8 days
    
    # CORS
    BACKEND_CORS_ORIGINS: List[str] = ["http://localhost:3000", "http://localhost:8000"]
    
    # Database
    POSTGRES_SERVER: str = "localhost"
    POSTGRES_USER: str = "postgres"
    POSTGRES_PASSWORD: str = "postgres"
    POSTGRES_DB: str = "networkanalyzer"
    POSTGRES_PORT: str = "5432"
    DATABASE_URI: Optional[str] = None
    
    # Network analysis settings
    MAX_STORED_PACKETS: int = 1000000  # Maximum packets to store in the database
    PACKET_RETENTION_DAYS: int = 30    # Number of days to keep packet data
    MAX_CAPTURE_SIZE_MB: int = 500     # Maximum capture size in MB
    ENABLE_GEOLOCATION: bool = True    # Whether to enable IP geolocation
    ANOMALY_DETECTION: bool = True     # Whether to enable anomaly detection
    
    # Path to GeoIP database
    GEOIP_DB_PATH: Optional[Path] = None
    
    # Security settings
    ALGORITHM: str = "HS256"
    
    # Logging
    LOG_LEVEL: str = "INFO"
    
    class Config:
        case_sensitive = True
        env_file = ".env"

    def get_database_url(self) -> str:
        if self.DATABASE_URI:
            return self.DATABASE_URI
        return f"postgresql://{self.POSTGRES_USER}:{self.POSTGRES_PASSWORD}@{self.POSTGRES_SERVER}:{self.POSTGRES_PORT}/{self.POSTGRES_DB}"

settings = Settings()

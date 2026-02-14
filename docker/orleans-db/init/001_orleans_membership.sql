CREATE TABLE IF NOT EXISTS OrleansMembershipTable
(
    DeploymentId VARCHAR(150) NOT NULL,
    Address VARCHAR(45) NOT NULL,
    Port INT NOT NULL,
    Generation INT NOT NULL,
    SiloName VARCHAR(150) NOT NULL,
    HostName VARCHAR(150) NOT NULL,
    Status INT NOT NULL,
    ProxyPort INT NULL,
    SuspectTimes VARCHAR(8000) NULL,
    StartTime TIMESTAMP(3) NOT NULL,
    IAmAliveTime TIMESTAMP(3) NOT NULL,
    PRIMARY KEY (DeploymentId, Address, Port, Generation)
);

CREATE TABLE IF NOT EXISTS OrleansMembershipVersionTable
(
    DeploymentId VARCHAR(150) NOT NULL,
    Timestamp BIGINT NOT NULL,
    Version INT NOT NULL,
    PRIMARY KEY (DeploymentId)
);

CREATE INDEX IF NOT EXISTS IX_OrleansMembershipTable_IAmAliveTime
ON OrleansMembershipTable (DeploymentId, IAmAliveTime);

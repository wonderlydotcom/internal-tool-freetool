-- Migration: Remove WorkspaceId columns and legacy tables after Space refactor
-- This migration completes the Group/Workspace -> Space unification by:
-- 1. Removing WorkspaceId from Folders (was NOT NULL, causing insert failures)
-- 2. Removing WorkspaceId from Resources (was nullable, cleanup)
-- 3. Making SpaceId NOT NULL on both tables
-- 4. Dropping legacy Workspaces, UserGroups, Groups tables

-- ============================================================================
-- Step 1: Recreate Folders table without WorkspaceId, with SpaceId NOT NULL
-- ============================================================================

CREATE TABLE Folders_new (
    Id TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL,
    ParentId TEXT,
    SpaceId TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (SpaceId) REFERENCES Spaces(Id),
    FOREIGN KEY (ParentId) REFERENCES Folders(Id)
);

INSERT INTO Folders_new (Id, Name, ParentId, SpaceId, CreatedAt, UpdatedAt, IsDeleted)
SELECT Id, Name, ParentId, SpaceId, CreatedAt, UpdatedAt, IsDeleted FROM Folders;

DROP TABLE Folders;
ALTER TABLE Folders_new RENAME TO Folders;

-- Recreate Folders indexes
CREATE UNIQUE INDEX IX_Folders_Name_ParentId ON Folders(Name, ParentId);
CREATE INDEX IX_Folders_IsDeleted ON Folders(IsDeleted);
CREATE INDEX IX_Folders_SpaceId ON Folders(SpaceId);

-- ============================================================================
-- Step 2: Recreate Resources table without WorkspaceId, with SpaceId NOT NULL
-- ============================================================================

CREATE TABLE Resources_new (
    Id TEXT NOT NULL PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT NOT NULL,
    BaseUrl TEXT NOT NULL,
    UrlParameters TEXT NOT NULL DEFAULT '[]',
    Headers TEXT NOT NULL DEFAULT '[]',
    Body TEXT NOT NULL DEFAULT '[]',
    HttpMethod TEXT NOT NULL DEFAULT 'GET',
    SpaceId TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    IsDeleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (SpaceId) REFERENCES Spaces(Id)
);

INSERT INTO Resources_new (Id, Name, Description, BaseUrl, UrlParameters, Headers, Body, HttpMethod, SpaceId, CreatedAt, UpdatedAt, IsDeleted)
SELECT Id, Name, Description, BaseUrl, UrlParameters, Headers, Body, HttpMethod, SpaceId, CreatedAt, UpdatedAt, IsDeleted FROM Resources;

DROP TABLE Resources;
ALTER TABLE Resources_new RENAME TO Resources;

-- Recreate Resources indexes
CREATE UNIQUE INDEX IX_Resources_Name ON Resources(Name);
CREATE INDEX IX_Resources_CreatedAt ON Resources(CreatedAt);
CREATE INDEX IX_Resources_UpdatedAt ON Resources(UpdatedAt);
CREATE INDEX IX_Resources_SpaceId ON Resources(SpaceId);

-- ============================================================================
-- Step 3: Drop legacy tables (no longer needed after Space refactor)
-- ============================================================================

DROP TABLE IF EXISTS Workspaces;
DROP TABLE IF EXISTS UserGroups;
DROP TABLE IF EXISTS Groups;

-- Drop backup table from migration 012 if it still exists
DROP TABLE IF EXISTS Folders_backup;

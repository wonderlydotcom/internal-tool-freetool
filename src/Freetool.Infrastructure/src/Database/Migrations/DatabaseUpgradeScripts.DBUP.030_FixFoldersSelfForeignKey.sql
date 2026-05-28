-- Rebuild Folders so the self-referencing ParentId foreign key points at the final table name.
-- DBUP.017 recreated the table through Folders_new and left the FK pointing at that
-- transient table name in fresh SQLite schemas.

PRAGMA foreign_keys=OFF;

DROP TABLE IF EXISTS Folders_fk_rebuild;

CREATE TABLE Folders_fk_rebuild (
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

INSERT INTO Folders_fk_rebuild (Id, Name, ParentId, SpaceId, CreatedAt, UpdatedAt, IsDeleted)
SELECT Id, Name, ParentId, SpaceId, CreatedAt, UpdatedAt, IsDeleted FROM Folders;

DROP TABLE Folders;
ALTER TABLE Folders_fk_rebuild RENAME TO Folders;

CREATE UNIQUE INDEX IX_Folders_Name_ParentId ON Folders(Name, ParentId);
CREATE INDEX IX_Folders_IsDeleted ON Folders(IsDeleted);
CREATE INDEX IX_Folders_SpaceId ON Folders(SpaceId);

PRAGMA foreign_keys=ON;

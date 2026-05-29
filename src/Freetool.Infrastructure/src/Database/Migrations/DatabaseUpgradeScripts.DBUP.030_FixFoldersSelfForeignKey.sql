-- DBUP.017 now builds fresh schemas with Folders.ParentId referencing Folders.
-- Existing production databases already have the correct self-reference after SQLite
-- rewrote the transient table reference during the original migration. Keep this
-- migration as a no-op so environments that pulled the previous repair attempt can
-- record DBUP.030 without rebuilding Folders under DbUp's transaction.

SELECT 1;

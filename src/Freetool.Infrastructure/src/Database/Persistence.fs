namespace Freetool.Infrastructure.Database

open System
open System.IO
open System.Data.Common
open System.Reflection
open DbUp
open Microsoft.Data.Sqlite

module Persistence =

    type IDBIOAsync<'a> = (DbConnection -> Async<'a>) -> Async<'a>

    let getDatabaseConnectionString (dataSource: string) =
        let builder = SqliteConnectionStringBuilder()
        builder.DataSource <- dataSource
        builder.ToString()

    let private ensureParentDirectoryExists (connectionString: string) =
        let builder = SqliteConnectionStringBuilder(connectionString)
        let dataSource = builder.DataSource |> Option.ofObj |> Option.defaultValue ""

        let shouldEnsureDirectory =
            not (String.IsNullOrWhiteSpace dataSource)
            && not (String.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
            && builder.Mode <> SqliteOpenMode.Memory
            && not (dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))

        if shouldEnsureDirectory then
            let dbPath =
                if Path.IsPathRooted dataSource then
                    dataSource
                else
                    Path.GetFullPath dataSource

            let directory = Path.GetDirectoryName dbPath

            if not (String.IsNullOrWhiteSpace directory) then
                Directory.CreateDirectory(directory) |> ignore

    let prepareSqliteConnectionString (connectionString: string) =
        ensureParentDirectoryExists connectionString
        connectionString

    let getDbConnectionAsync connectionString : Async<DbConnection> = async {
        let dbConnection = new SqliteConnection(connectionString)
        return dbConnection
    }

    let getDatabaseConnection connectionString : DbConnection = new SqliteConnection(connectionString)

    let withDbIoAsync connectionString (callbackAsync: DbConnection -> Async<'a>) = async {
        use dbConnection = new SqliteConnection(connectionString)
        do! dbConnection.OpenAsync() |> Async.AwaitTask
        return! callbackAsync dbConnection
    }

    let configureSqliteWal (connectionString: string) =
        use connection = new SqliteConnection(connectionString)
        connection.Open()
        use command = connection.CreateCommand()
        command.CommandText <- "PRAGMA journal_mode=WAL;"
        command.ExecuteNonQuery() |> ignore
        command.CommandText <- "PRAGMA busy_timeout=5000;"
        command.ExecuteNonQuery() |> ignore

    let upgradeDatabase (connectionString: string) =
        let connectionString = prepareSqliteConnectionString connectionString

        let upgrader =
            DeployChanges.To
                .SQLiteDatabase(connectionString: string)
                .WithScriptsEmbeddedInAssembly(
                    Assembly.GetExecutingAssembly(),
                    (fun (scriptName: string) ->
                        printfn "DBUp found script: %s" scriptName
                        scriptName.Contains("DatabaseUpgradeScripts.DBUP"))
                )
                .WithTransactionPerScript()
                .LogToConsole()
                .Build()

        let result = upgrader.PerformUpgrade()

        match result.Successful with
        | false ->
            printfn "Database upgrade failed!"

            match result.Error with
            | null -> ()
            | ex -> printfn "Error: %s" ex.Message

            failwith "Failed to upgrade database!"
        | true ->
            printfn "Database upgrade successful!"
            configureSqliteWal connectionString

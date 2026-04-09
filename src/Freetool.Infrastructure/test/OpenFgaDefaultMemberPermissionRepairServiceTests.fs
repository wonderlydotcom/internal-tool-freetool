module Freetool.Infrastructure.Tests.OpenFgaDefaultMemberPermissionRepairServiceTests

open System
open System.Text.Json
open System.Text.Json.Serialization
open System.Threading.Tasks
open Xunit
open Microsoft.Extensions.Logging.Abstractions
open Freetool.Api.Services
open Freetool.Application.DTOs
open Freetool.Application.Interfaces
open Freetool.Domain
open Freetool.Domain.Entities
open Freetool.Domain.Events
open Freetool.Domain.ValueObjects

let private serializerOptions =
    let options = JsonSerializerOptions()
    options.Converters.Add(JsonFSharpConverter())
    options

type MockEventRepository(events: EventData list) =
    interface IEventRepository with
        member _.SaveEventAsync(_event: IDomainEvent) = Task.FromResult(())
        member _.CommitAsync() = Task.FromResult(())

        member _.GetEventsAsync(filter: EventFilter) : Task<PagedResult<EventData>> = task {
            let filtered =
                events
                |> List.filter (fun event ->
                    filter.EventType |> Option.forall (fun eventType -> event.EventType = eventType))
                |> List.filter (fun event ->
                    filter.EntityType
                    |> Option.forall (fun entityType -> event.EntityType = entityType))
                |> List.sortBy (fun event -> event.OccurredAt)

            let items = filtered |> List.skip filter.Skip |> List.truncate filter.Take

            return {
                Items = items
                TotalCount = filtered.Length
                Skip = filter.Skip
                Take = filter.Take
            }
        }

        member _.GetEventsByAppIdAsync(_filter: AppEventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 10
                }
            )

        member _.GetEventsByDashboardIdAsync(_filter: DashboardEventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 10
                }
            )

        member _.GetEventsByUserIdAsync(_filter: UserEventFilter) =
            Task.FromResult(
                {
                    Items = []
                    TotalCount = 0
                    Skip = 0
                    Take = 10
                }
            )

type MockSpaceRepository(spaces: ValidatedSpace list) =
    interface ISpaceRepository with
        member _.GetByIdAsync(spaceId: SpaceId) : Task<ValidatedSpace option> = task {
            return spaces |> List.tryFind (fun space -> space.State.Id = spaceId)
        }

        member _.GetByNameAsync(name: string) : Task<ValidatedSpace option> = task {
            return spaces |> List.tryFind (fun space -> space.State.Name = name)
        }

        member _.GetAllAsync (skip: int) (take: int) : Task<ValidatedSpace list> = task {
            return spaces |> List.skip skip |> List.truncate take
        }

        member _.GetByUserIdAsync(userId: UserId) : Task<ValidatedSpace list> = task {
            return spaces |> List.filter (fun space -> List.contains userId space.State.MemberIds)
        }

        member _.GetByModeratorUserIdAsync(userId: UserId) : Task<ValidatedSpace list> = task {
            return spaces |> List.filter (fun space -> space.State.ModeratorUserId = userId)
        }

        member _.AddAsync(_space: ValidatedSpace) = Task.FromResult(Ok())
        member _.UpdateAsync(_space: ValidatedSpace) = Task.FromResult(Ok())
        member _.DeleteAsync(_space: ValidatedSpace) = Task.FromResult(Ok())

        member _.ExistsAsync(spaceId: SpaceId) =
            Task.FromResult(spaces |> List.exists (fun space -> space.State.Id = spaceId))

        member _.ExistsByNameAsync(name: string) =
            Task.FromResult(spaces |> List.exists (fun space -> space.State.Name = name))

        member _.GetCountAsync() = Task.FromResult(spaces.Length)

type TrackingAuthorizationService() =
    let mutable tuples: Set<string> = Set.empty
    let mutable updateRequests: UpdateRelationshipsRequest list = []

    let key subject relation obj =
        sprintf
            "%s|%s|%s"
            (AuthTypes.subjectToString subject)
            (AuthTypes.relationToString relation)
            (AuthTypes.objectToString obj)

    member _.SeedTuple(subject: AuthSubject, relation: AuthRelation, obj: AuthObject) =
        tuples <- tuples |> Set.add (key subject relation obj)

    member _.UpdateRequests = updateRequests

    interface IAuthorizationService with
        member _.CreateStoreAsync req =
            Task.FromResult({ Id = "store-1"; Name = req.Name })

        member _.WriteAuthorizationModelAsync() =
            Task.FromResult({ AuthorizationModelId = "model-1" })

        member _.InitializeOrganizationAsync _ _ = Task.FromResult(())
        member _.CreateRelationshipsAsync _ = Task.FromResult(())
        member _.DeleteRelationshipsAsync _ = Task.FromResult(())
        member _.StoreExistsAsync _ = Task.FromResult(true)

        member _.UpdateRelationshipsAsync(request: UpdateRelationshipsRequest) = task {
            updateRequests <- request :: updateRequests

            for tuple in request.TuplesToAdd do
                tuples <- tuples |> Set.add (key tuple.Subject tuple.Relation tuple.Object)

            for tuple in request.TuplesToRemove do
                tuples <- tuples |> Set.remove (key tuple.Subject tuple.Relation tuple.Object)
        }

        member _.CheckPermissionAsync (subject: AuthSubject) (relation: AuthRelation) (obj: AuthObject) =
            Task.FromResult(tuples |> Set.contains (key subject relation obj))

        member this.BatchCheckPermissionsAsync
            (subject: AuthSubject)
            (relations: AuthRelation list)
            (obj: AuthObject)
            : Task<Map<AuthRelation, bool>> =
            task {
                let! results =
                    relations
                    |> List.map (fun relation -> task {
                        let! allowed = (this :> IAuthorizationService).CheckPermissionAsync subject relation obj
                        return (relation, allowed)
                    })
                    |> Task.WhenAll

                return results |> Array.toList |> Map.ofList
            }

let private createSpace (name: string) =
    let actorUserId = UserId.NewId()
    let moderatorUserId = UserId.NewId()

    match Space.create actorUserId name moderatorUserId None with
    | Ok space -> space
    | Error error -> failwithf "Failed to create test space: %A" error

let private createPermissionsChangedEvent
    (actorUserId: UserId)
    (space: ValidatedSpace)
    (granted: string list)
    (revoked: string list)
    (occurredAt: DateTime)
    =
    let defaultPermissionsChangedEventType: Freetool.Domain.Entities.SpaceEvents =
        Freetool.Domain.Entities.SpaceDefaultMemberPermissionsChangedEvent

    let event =
        SpaceEvents.spaceDefaultMemberPermissionsChanged actorUserId space.State.Id space.State.Name granted revoked

    {
        Id = Guid.NewGuid()
        EventId = event.EventId.ToString()
        EventType = EventType.SpaceEvents defaultPermissionsChangedEventType
        EntityType = EntityType.Space
        EntityId = space.State.Id.Value.ToString()
        EventData = JsonSerializer.Serialize(event, serializerOptions)
        OccurredAt = occurredAt
        CreatedAt = occurredAt
        UserId = actorUserId
    }

[<Fact>]
let ``RepairAsync adds missing default member tuples from audit history`` () : Task = task {
    let actorUserId = UserId.NewId()
    let space = createSpace "Engineering"
    let spaceId = space.State.Id.Value.ToString()

    let event =
        createPermissionsChangedEvent actorUserId space [ "CreateApp"; "RunApp" ] [] (DateTime.UtcNow.AddMinutes(-5.0))

    let eventRepository = MockEventRepository([ event ]) :> IEventRepository
    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = TrackingAuthorizationService()

    let service =
        OpenFgaDefaultMemberPermissionRepairService(
            eventRepository,
            spaceRepository,
            authService :> IAuthorizationService,
            NullLogger<OpenFgaDefaultMemberPermissionRepairService>.Instance
        )

    let! summary = service.RepairAsync(true, None)

    Assert.Equal(1, summary.SpacesExamined)
    Assert.Equal(1, summary.SpacesWithDrift)

    let result = Assert.Single(summary.Results)
    Assert.True(result.Applied)
    let request = Assert.Single(authService.UpdateRequests)

    Assert.True(
        request.TuplesToAdd
        |> List.exists (fun tuple -> tuple.Relation = AppCreate && tuple.Object = SpaceObject spaceId),
        sprintf "Summary: %A\nRequest: %A" summary request
    )

    Assert.True(
        request.TuplesToAdd
        |> List.exists (fun tuple -> tuple.Relation = AppRun && tuple.Object = SpaceObject spaceId),
        sprintf "TuplesToAdd: %A" request.TuplesToAdd
    )

    let! canCreateApp =
        (authService :> IAuthorizationService).CheckPermissionAsync
            (UserSetFromRelation("space", spaceId, "member"))
            AppCreate
            (SpaceObject spaceId)

    let! canRunApp =
        (authService :> IAuthorizationService).CheckPermissionAsync
            (UserSetFromRelation("space", spaceId, "member"))
            AppRun
            (SpaceObject spaceId)

    Assert.True(canCreateApp)
    Assert.True(canRunApp)
}

[<Fact>]
let ``RepairAsync removes stale default member tuples when later audit events revoke them`` () : Task = task {
    let actorUserId = UserId.NewId()
    let space = createSpace "Support"
    let spaceId = space.State.Id.Value.ToString()
    let defaultMemberSubject = UserSetFromRelation("space", spaceId, "member")

    let grantedEvent =
        createPermissionsChangedEvent actorUserId space [ "CreateApp"; "RunApp" ] [] (DateTime.UtcNow.AddMinutes(-10.0))

    let revokedEvent =
        createPermissionsChangedEvent actorUserId space [] [ "RunApp" ] (DateTime.UtcNow.AddMinutes(-1.0))

    let eventRepository =
        MockEventRepository([ grantedEvent; revokedEvent ]) :> IEventRepository

    let spaceRepository = MockSpaceRepository([ space ]) :> ISpaceRepository
    let authService = TrackingAuthorizationService()
    authService.SeedTuple(defaultMemberSubject, AppCreate, SpaceObject spaceId)
    authService.SeedTuple(defaultMemberSubject, AppRun, SpaceObject spaceId)

    let service =
        OpenFgaDefaultMemberPermissionRepairService(
            eventRepository,
            spaceRepository,
            authService :> IAuthorizationService,
            NullLogger<OpenFgaDefaultMemberPermissionRepairService>.Instance
        )

    let! summary = service.RepairAsync(true, None)

    Assert.Equal(1, summary.SpacesExamined)
    Assert.Equal(1, summary.SpacesWithDrift)

    let result = Assert.Single(summary.Results)
    Assert.True(result.Applied)
    let request = Assert.Single(authService.UpdateRequests)

    Assert.True(
        request.TuplesToRemove
        |> List.exists (fun tuple -> tuple.Relation = AppRun && tuple.Object = SpaceObject spaceId),
        sprintf "Summary: %A\nRequest: %A" summary request
    )

    let! canCreateApp =
        (authService :> IAuthorizationService).CheckPermissionAsync defaultMemberSubject AppCreate (SpaceObject spaceId)

    let! canRunApp =
        (authService :> IAuthorizationService).CheckPermissionAsync defaultMemberSubject AppRun (SpaceObject spaceId)

    Assert.True(canCreateApp)
    Assert.False(canRunApp)
}
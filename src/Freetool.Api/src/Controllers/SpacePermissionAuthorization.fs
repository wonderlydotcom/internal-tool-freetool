namespace Freetool.Api.Controllers

open System.Threading.Tasks
open Freetool.Application.Interfaces
open Freetool.Domain.ValueObjects

module SpacePermissionAuthorization =
    let private isModeratorInheritedPermission (permission: AuthRelation) =
        match permission with
        | ResourceCreate
        | ResourceEdit
        | ResourceDelete
        | AppCreate
        | AppEdit
        | AppDelete
        | AppRun
        | DashboardCreate
        | DashboardEdit
        | DashboardDelete
        | DashboardRun
        | FolderCreate
        | FolderEdit
        | FolderDelete
        | SpaceRename
        | SpaceAddMember
        | SpaceRemoveMember -> true
        | OrganizationAdmin
        | SpaceMember
        | SpaceModerator
        | SpaceOrganization
        | SpaceCreate
        | SpaceDelete -> false

    let hasSpacePermissionWithModeratorFallback
        (authorizationService: IAuthorizationService)
        (spaceRepository: ISpaceRepository)
        (userId: UserId)
        (spaceId: SpaceId)
        (permission: AuthRelation)
        : Task<bool> =
        task {
            let userIdStr = userId.Value.ToString()
            let spaceIdStr = spaceId.Value.ToString()

            let! hasPermission =
                authorizationService.CheckPermissionAsync (User userIdStr) permission (SpaceObject spaceIdStr)

            if hasPermission then
                return true
            elif not (isModeratorInheritedPermission permission) then
                return false
            else
                let! spaceOption = spaceRepository.GetByIdAsync spaceId

                return
                    match spaceOption with
                    | Some space when space.State.ModeratorUserId = userId -> true
                    | _ -> false
        }
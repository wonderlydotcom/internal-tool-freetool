namespace Freetool.Api.Ui

open Oxpecker

module Routes =
    let endpoints = [
        GET [
            route "/" Handlers.index
            route "/spaces" Handlers.spacesPage
            route "/spaces-list" Handlers.spacesListPage
            route "/users" Handlers.usersPage
            route "/audit" Handlers.auditPage
            route "/dev/select-user" Handlers.devSelectUserPage
            routef "/spaces/{%s}/resources" Handlers.resourcesPage
            routef "/spaces/{%s}/settings" Handlers.settingsPage
            routef "/spaces/{%s}/permissions" Handlers.permissionsAlias
            routef "/spaces/{%s}/trash" Handlers.trashPage
            routef "/spaces/{%s}/{%s}/run" Handlers.runAppPage
            routef "/spaces/{%s}/{%s}/dashboard-run" Handlers.dashboardRunPage
            routef "/spaces/{%s}/{%s}" Handlers.nodePage
            routef "/spaces/{%s}" Handlers.spaceRootPage
        ]
        POST [
            route "/dev/select-user" Handlers.devSelectUserPost
            route "/_ui/spaces/create" Handlers.createSpace
            routef "/_ui/spaces/{%s}/name" Handlers.updateSpaceName
            routef "/_ui/spaces/{%s}/moderator" Handlers.changeModerator
            routef "/_ui/spaces/{%s}/members/add" Handlers.addMember
            routef "/_ui/spaces/{%s}/members/remove" Handlers.removeMember
            routef "/_ui/spaces/{%s}/members/permissions" Handlers.updateMemberPermissions
            routef "/_ui/spaces/{%s}/default-member-permissions" Handlers.updateDefaultMemberPermissions
            routef "/_ui/spaces/{%s}/delete" Handlers.deleteSpace
            routef "/_ui/spaces/{%s}/folders/create" Handlers.createFolder
            routef "/_ui/spaces/{%s}/folders/{%s}/rename" Handlers.renameFolder
            routef "/_ui/spaces/{%s}/folders/{%s}/delete" Handlers.deleteFolder
            routef "/_ui/spaces/{%s}/resources/create" Handlers.createResource
            routef "/_ui/spaces/{%s}/resources/{%s}/update" Handlers.updateResource
            routef "/_ui/spaces/{%s}/resources/{%s}/delete" Handlers.deleteResource
            routef "/_ui/spaces/{%s}/apps/create" Handlers.createApp
            routef "/_ui/spaces/{%s}/apps/{%s}/delete" Handlers.deleteApp
            routef "/_ui/spaces/{%s}/apps/{%s}/name" Handlers.updateAppName
            routef "/_ui/spaces/{%s}/apps/{%s}/description" Handlers.updateAppDescription
            routef "/_ui/spaces/{%s}/apps/{%s}/config" Handlers.updateAppConfig
            routef "/_ui/spaces/{%s}/apps/{%s}/run" Handlers.runApp
            routef "/spaces/{%s}/{%s}/run" Handlers.runApp
            routef "/_ui/spaces/{%s}/dashboards/create" Handlers.createDashboard
            routef "/_ui/spaces/{%s}/dashboards/{%s}/delete" Handlers.deleteDashboard
            routef "/_ui/spaces/{%s}/dashboards/{%s}/name" Handlers.updateDashboardName
            routef "/_ui/spaces/{%s}/dashboards/{%s}/config" Handlers.updateDashboardConfig
            routef "/_ui/spaces/{%s}/dashboards/{%s}/prepare" Handlers.prepareDashboard
            routef "/spaces/{%s}/{%s}/dashboard-run" Handlers.prepareDashboard
            routef "/_ui/spaces/{%s}/trash/apps/{%s}/restore" Handlers.restoreApp
            routef "/_ui/spaces/{%s}/trash/folders/{%s}/restore" Handlers.restoreFolder
            routef "/_ui/spaces/{%s}/trash/resources/{%s}/restore" Handlers.restoreResource
        ]
    ]
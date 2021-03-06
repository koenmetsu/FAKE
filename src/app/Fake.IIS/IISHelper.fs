﻿[<AutoOpen>]
module Fake.IISHelper

open Microsoft.Web.Administration
open Fake.PermissionsHelper
open Fake.ProcessHelper

let private bindApplicationPool (appPool : ApplicationPool) (app : Application) =
    app.ApplicationPoolName <- appPool.Name

let private doWithManager (f : ServerManager->unit) (mgr : ServerManager option) =
    match mgr with
    | Some m -> f m
    | None ->
        let m = new ServerManager()
        f m
        m.CommitChanges()

let SetPhysicalPath (virtualPath : string) physicalPath (siteName : string) (manager : ServerManager option) =
    doWithManager (fun m ->
        let site = m.Sites.[siteName]
        let app = site.Applications.[virtualPath]
        let virtDir = app.VirtualDirectories.[virtualPath]
        virtDir.PhysicalPath <- physicalPath
    ) manager

let RemoveBindingFromSite bindingInformation bindingProtocol (siteName : string) (manager : ServerManager option) =
    doWithManager (fun m ->
        let site = m.Sites.[siteName]
        match site.Bindings |> Seq.tryFind( fun b -> b.Protocol = bindingProtocol && b.BindingInformation = bindingInformation) with    
        | Some b -> site.Bindings.Remove b
        | None -> ()
    ) manager

let  AddBindingToSite (bindingInformation : string) (bindingProtocol : string) (siteName : string) (manager : ServerManager option) =
    doWithManager (fun m ->
        let site = m.Sites.[siteName]
        match site.Bindings |> Seq.exists( fun b -> b.Protocol = bindingProtocol && b.BindingInformation = bindingInformation) with
        | false -> site.Bindings.Add(bindingInformation, bindingProtocol) |> ignore
        | true -> ()
    ) manager

let Site (name : string) protocol binding (physicalPath : string) appPool (mgr : ServerManager) =
    let mutable site = mgr.Sites.[name] 
    match (site) with
    | null -> site <- mgr.Sites.Add(name, protocol, binding, physicalPath)
    | _ -> 
        SetPhysicalPath "/" physicalPath name (Some mgr)
        AddBindingToSite binding protocol name (Some mgr)

    site.ApplicationDefaults.ApplicationPoolName <- appPool
    site

let Application (virtualPath : string) (physicalPath : string) (site : Site) (mgr : ServerManager) =
    let app = site.Applications.[virtualPath]
    match (app) with
    | null -> site.Applications.Add(virtualPath, physicalPath)
    | _ -> app.VirtualDirectories.[0].PhysicalPath <- physicalPath; app

let commit (mgr : ServerManager) = mgr.CommitChanges()

type ApplicationPoolConfig(name : string, ?runtime:string, ?allow32on64:bool, ?identity : ProcessModelIdentityType) = class
    member this.name = name
    member this.runtime = defaultArg runtime "v4.0"
    member this.allow32on64 = defaultArg allow32on64 false
    member this.identity = defaultArg identity ProcessModelIdentityType.ApplicationPoolIdentity        
end

let private MergeAppPoolProperties (appPool:ApplicationPool)(config:ApplicationPoolConfig) = 
        appPool.Enable32BitAppOnWin64 <- config.allow32on64
        appPool.ManagedRuntimeVersion <- config.runtime
        appPool.ProcessModel.IdentityType <- config.identity
        appPool

let ApplicationPool (config: ApplicationPoolConfig) (mgr : ServerManager) = 
    let appPool = mgr.ApplicationPools.[config.name]
    match (appPool) with
    | null -> 
        let pool = mgr.ApplicationPools.Add(config.name)
        MergeAppPoolProperties pool config
    | _ ->
        MergeAppPoolProperties appPool config

let IIS (site : ServerManager -> Site) 
        (appPool : ServerManager -> ApplicationPool) 
        (app : (Site -> ServerManager -> Application) option) =
    use mgr = new ServerManager()
    requiresAdmin (fun _ -> 
        match app with
        | Some(app) -> bindApplicationPool (appPool mgr) (app (site mgr) mgr)
        | None -> bindApplicationPool (appPool mgr) (site mgr).Applications.[0]
        commit mgr
    )

let AppCmd (command : string) = 
    System.Console.WriteLine("Applying {0} via appcmd.exe", command)
    if 0 <> ExecProcess (fun info ->  
        info.FileName <- @"c:\windows\system32\inetsrv\appcmd.exe"
        info.Arguments <- command) (System.TimeSpan.FromSeconds(30.))
    then failwithf "AppCmd.exe %s failed." command
    ()

let UnlockSection (configPath : string) =
    requiresAdmin (fun _ -> AppCmd (sprintf "unlock config -section:%s" configPath))

let deleteSite (name : string) = 
    use mgr = new ServerManager()
    let site = mgr.Sites.[name]
    if site <> null then
        site.Delete()
        commit mgr 

let deleteApp (name : string) (site : Site) = 
    use mgr = new ServerManager()
    let app = site.Applications.[name]
    if app <> null then
        app.Delete()
        commit mgr

let deleteApplicationPool (name : string) = 
    use mgr = new ServerManager()
    let appPool = mgr.ApplicationPools.[name]
    if appPool <> null then
        appPool.Delete()
        commit mgr


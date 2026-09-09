module App.Common.Handler

open App.Common.View
open App.Infrastructure
open App.ServiceRegistry
open FSharp.ViewEngine
open Giraffe
open Microsoft.AspNetCore.Http
open StarFederation.Datastar.DependencyInjection
open System
open System.Collections.Generic
open System.Text.Json

let patchElement (ds:IDatastarService) (element:HtmlElement) = task {
    do! ds.PatchElementsAsync(Render.toString element)
}

let inline patchSignals (ds:IDatastarService) (signals:'T) = task {
    do! ds.PatchSignalsAsync(signals)
}

let private navigationUrl (ctx:HttpContext) =
    let query =
        seq {
            for parameter in ctx.Request.Query do
                if not (String.Equals(parameter.Key, "datastar", StringComparison.OrdinalIgnoreCase)) then
                    for value in parameter.Value do
                        yield KeyValuePair(parameter.Key, value)
        }

    ctx.Request.Path.ToString() + QueryString.Create(query).ToString()

let private afterNavigationScript updateHistoryAndScroll =
    $"""(function(){{{updateHistoryAndScroll}requestAnimationFrame(function(){{document.getElementById('page-content')?.focus({{preventScroll:true}});window.meiermadeTelemetry?.trackPage();}});}})();"""

let historyScript (url:string) =
    let serializedUrl = JsonSerializer.Serialize url
    afterNavigationScript $"if(window.location.pathname+window.location.search!=={serializedUrl}){{window.history.pushState({{meierMadeScrollX:0,meierMadeScrollY:0}},'',{serializedUrl});}}window.scrollTo(0,0);"

let private restoreHistoryScript =
    afterNavigationScript "var navigationState=window.history.state||{};window.scrollTo(navigationState.meierMadeScrollX||0,navigationState.meierMadeScrollY||0);"

let private updateHistory (ctx:HttpContext) (ds:IDatastarService) (url:string) = task {
    let script = if ctx.IsHistoryRestore then restoreHistoryScript else historyScript url
    do! ds.ExecuteScriptAsync(script)
}

let patchPage (ctx:HttpContext) (ds:IDatastarService) (metadata:PageMetadata) (page:HtmlElement) (selectedNav:string) = task {
    do! patchSignals ds {| navigationOpen = false; selectedNav = selectedNav |}
    for element in PageHead.patchableElements metadata do
        do! patchElement ds element
    do! patchElement ds page
    do! updateHistory ctx ds (navigationUrl ctx)
}

let renderPage (services:Services) (metadata:PageMetadata) (page:HtmlElement) (selectedNav:string) : HttpHandler =
    fun next ctx -> task {
        let privacyPolicy = App.Privacy.fromRequest ctx
        let doc = Document.primary(metadata, page, services.config.analytics.otelEndpoint, privacyPolicy, selectedNav)
        let html = Render.toHtmlDocString doc
        return! htmlString html next ctx
    }

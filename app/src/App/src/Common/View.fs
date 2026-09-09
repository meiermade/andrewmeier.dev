module App.Common.View

open FSharp.ViewEngine
open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open type Html
open type Datastar

type PageMetadata =
    { canonicalPath:string
      description:string
      title:string }

module Asset =
    let resolveWithManifest (manifest:IReadOnlyDictionary<string, string>) (path:string) =
        match manifest.TryGetValue path with
        | true, resolvedPath -> resolvedPath
        | false, _ -> path

    let private manifest =
        lazy
            let manifestPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "asset-manifest.json")

            if File.Exists(manifestPath) then
                try
                    JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText manifestPath)
                    :> IReadOnlyDictionary<string, string>
                with _ ->
                    Dictionary<string, string>() :> IReadOnlyDictionary<string, string>
            else
                Dictionary<string, string>() :> IReadOnlyDictionary<string, string>

    let fingerprinted (path:string) =
        resolveWithManifest manifest.Value path

module Navigation =
    let initialize = "window.history.scrollRestoration = 'manual'"

    let saveScroll =
        "window.history.replaceState(Object.assign({}, window.history.state || {}, {meierMadeScrollX: window.scrollX, meierMadeScrollY: window.scrollY}), '', window.location.href)"

    let action (href:string) =
        let url = JsonSerializer.Serialize href
        let request = $"@get({url}, {{filterSignals: {{include: /^$/}}, headers: {{'X-MeierMade-Navigation': 'push'}}}})"
        $"evt.button === 0 && !evt.ctrlKey && !evt.metaKey && !evt.shiftKey && !evt.altKey && !evt.currentTarget.hasAttribute('download') && (!evt.currentTarget.target || evt.currentTarget.target === '_self') && evt.currentTarget.origin === window.location.origin && (evt.preventDefault(), {url} === window.location.pathname + window.location.search || ({saveScroll}, {request}))"

    let restoreAction =
        "@get(window.location.pathname + window.location.search, {filterSignals: {include: /^$/}, headers: {'X-MeierMade-Navigation': 'restore'}})"

module PageHead =
    let canonicalUrl (metadata:PageMetadata) =
        $"https://andymeier.dev{metadata.canonicalPath}"

    let titleElement (metadata:PageMetadata) =
        titleBuilder { _id "document-title"; metadata.title }

    let descriptionElement (metadata:PageMetadata) =
        meta { _id "meta-description"; _name "description"; _content metadata.description }

    let openGraphTitleElement (metadata:PageMetadata) =
        meta { _id "open-graph-title"; _property "og:title"; _content metadata.title }

    let openGraphDescriptionElement (metadata:PageMetadata) =
        meta { _id "open-graph-description"; _property "og:description"; _content metadata.description }

    let openGraphUrlElement (metadata:PageMetadata) =
        meta { _id "open-graph-url"; _property "og:url"; _content (canonicalUrl metadata) }

    let canonicalElement (metadata:PageMetadata) =
        link { _id "canonical-url"; _rel "canonical"; _href (canonicalUrl metadata) }

    let patchableElements metadata =
        [ titleElement metadata
          descriptionElement metadata
          openGraphTitleElement metadata
          openGraphDescriptionElement metadata
          openGraphUrlElement metadata
          canonicalElement metadata ]

module SafeOutput =
    let private imageSchemes = Set.ofList [ Uri.UriSchemeHttp; Uri.UriSchemeHttps ]

    let private tryImageUrl (value:string) =
        match Uri.TryCreate(value, UriKind.Absolute) with
        | true, uri when imageSchemes.Contains(uri.Scheme.ToLowerInvariant()) ->
            uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped) |> Some
        | _ -> None

    let tryBackgroundImageStyle (value:string) =
        value
        |> tryImageUrl
        |> Option.map (fun url ->
            let cssUrl =
                url
                    .Replace("\\", "%5C", StringComparison.Ordinal)
                    .Replace("'", "%27", StringComparison.Ordinal)
                    .Replace("\"", "%22", StringComparison.Ordinal)
                    .Replace("(", "%28", StringComparison.Ordinal)
                    .Replace(")", "%29", StringComparison.Ordinal)

            $"background-image: url('{cssUrl}')")

module SiteUrl =
    let article (permalink:string) =
        $"/articles/{Uri.EscapeDataString permalink}"

module MiniIcon =
    let github =
        raw """
        <svg viewBox="0 0 24 24" aria-hidden="true" class="h-6 w-6" fill="currentColor">
            <path fill-rule="evenodd" clip-rule="evenodd" d="M12 2C6.477 2 2 6.463 2 11.97c0 4.404 2.865 8.14 6.839 9.458.5.092.682-.216.682-.48 0-.236-.008-.864-.013-1.695-2.782.602-3.369-1.337-3.369-1.337-.454-1.151-1.11-1.458-1.11-1.458-.908-.618.069-.606.069-.606 1.003.07 1.531 1.027 1.531 1.027.892 1.524 2.341 1.084 2.91.828.092-.643.35-1.083.636-1.332-2.22-.251-4.555-1.107-4.555-4.927 0-1.088.39-1.979 1.029-2.675-.103-.252-.446-1.266.098-2.638 0 0 .84-.268 2.75 1.022A9.607 9.607 0 0 1 12 6.82c.85.004 1.705.114 2.504.336 1.909-1.29 2.747-1.022 2.747-1.022.546 1.372.202 2.386.1 2.638.64.696 1.028 1.587 1.028 2.675 0 3.83-2.339 4.673-4.566 4.92.359.307.678.915.678 1.846 0 1.332-.012 2.407-.012 2.734 0 .267.18.577.688.48 3.97-1.32 6.833-5.054 6.833-9.458C22 6.463 17.522 2 12 2Z"></path>
        </svg>
        """

    let xdotcom =
        raw """
        <svg viewBox="0 0 1200 1227" aria-hidden="true" class="h-5 w-5" fill="currentColor">
            <path d="M714.163 519.284L1160.89 0H1055.03L667.137 450.887L357.328 0H0L468.492 681.821L0 1226.37H105.866L515.491 750.218L842.672 1226.37H1200L714.137 519.284H714.163ZM569.165 687.828L521.697 619.934L144.011 79.6944H306.615L611.412 515.685L658.88 583.579L1055.08 1150.3H892.476L569.165 687.854V687.828Z"></path>
        </svg>
        """

    let linkedIn =
        raw """
        <svg viewBox="0 0 24 24" aria-hidden="true" class="h-6 w-6" fill="currentColor">
            <path d="M18.335 18.339H15.67v-4.177c0-.996-.02-2.278-1.39-2.278-1.389 0-1.601 1.084-1.601 2.205v4.25h-2.666V9.75h2.56v1.17h.035c.358-.674 1.228-1.387 2.528-1.387 2.7 0 3.2 1.778 3.2 4.091v4.715zM7.003 8.575a1.546 1.546 0 01-1.548-1.549 1.548 1.548 0 111.547 1.549zm1.336 9.764H5.666V9.75H8.34v8.589zM19.67 3H4.329C3.593 3 3 3.58 3 4.297v15.406C3 20.42 3.594 21 4.328 21h15.338C20.4 21 21 20.42 21 19.703V4.297C21 3.58 20.4 3 19.666 3h.003z"></path>
        </svg>
        """

    let calendar =
        raw """
        <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" width="16" height="16" class="shrink-0" aria-hidden="true">
          <path stroke-linecap="round" stroke-linejoin="round" d="M6.75 3v2.25M17.25 3v2.25M3 18.75V7.5a2.25 2.25 0 0 1 2.25-2.25h13.5A2.25 2.25 0 0 1 21 7.5v11.25M3 18.75A2.25 2.25 0 0 0 5.25 21h13.5A2.25 2.25 0 0 0 21 18.75M3 18.75v-7.5A2.25 2.25 0 0 1 5.25 9h13.5A2.25 2.25 0 0 1 21 11.25v7.5" />
        </svg>
        """

    let clock =
        raw """
        <svg xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor" width="16" height="16" class="shrink-0" aria-hidden="true">
          <path stroke-linecap="round" stroke-linejoin="round" d="M12 6v6l4 2m5-2a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
        </svg>
        """

    let logo =
        raw """
        <svg version="1.1" viewBox="0 0 650 650" class="w-full h-full" fill="currentColor" aria-hidden="true"><path d="M321.765 141.729c15.302-2.413 33.5 7.639 41.272 25.18 4.622 10.433 8.885 21.227 13.165 31.908l24.773 62.302 42.843 106.053c6.032 15 14.892 35.276 19.322 51.273.567 2.05-.486 10.69-1.396 13.36-5.92 17.37-14.839 39.343-22.538 55.23-5.926 12.225-18.045 19.073-28.732 21.225-11.206 1.994-28.433-4.294-35.669-15.395-7.077-10.856-14.596-32.455-19.72-45.472l-24.578-62.426-36.478-91.774c-7.363-18.472-14.766-36.918-21.958-55.497-2.558-6.607-4.738-14.643-1.706-21.864 13.688-32.607 18.644-68.773 51.4-74.103m139.643.039c10.591-1.07 28.41 6.264 34.927 16.594 8.982 14.235 18.584 41.208 25.314 57.95l36.856 91.66 34.336 85.245c6.838 16.894 16.943 40.613 22.766 57.852 1.662 4.855 2.568 10.084 2.667 15.39.068 4.278-.37 7.754-1.336 11.768-7.717 32.113-31.838 28.16-52.186 28.128-13.24.097-31.09 2.626-42.209-8.166-9.939-9.647-15.55-28.516-21.255-42.848l-21.718-54.146a15601 15601 0 0 1-50.16-124.246c-6.435-16.112-13.052-32.11-19.344-48.306-2.29-5.897-4.1-13.57-1.444-19.838 14.022-33.084 19.4-63.584 52.786-67.037m-278.687-.016c10.607-.558 18.9 2.317 27.98 9.433 7.124 5.58 10.37 11.225 14.23 20.641 8.102 19.764 15.828 39.756 23.564 59.755l34.291 87.692 23.365 58.809c3.597 9.064 11.492 27.413 13.618 36.933 2.995 13.41-12.94 42.493-17.012 55.087-1.763 5.453-6.203 14.923-8.823 19.104-3.186 5.059-7.119 9.247-11.57 12.32-10.46 7.379-22.782 8.999-34.179 4.495-14.467-5.692-19.556-15.45-26.25-32.046-13.388-33.18-25.976-67.331-39.694-100.243l-4.077 9.484-27.858 69.868c-9.942 24.92-17.987 55.505-43.946 53.41-17.945-1.446-49.04 5.575-63.434-8.616-6.218-6.245-10.202-15.49-11.034-25.609-1.462-17.079 6.65-32.608 12.572-47.276l15.345-38.07 48.355-119.624 25.6-63.251c12.357-30.878 18.545-58.49 48.957-62.296" style="stroke-width:1.13671"/></svg>
        """

    let sun =
        raw """<svg class="h-5 w-5 dark:hidden" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" d="M12 3v2.25m6.364.386-1.591 1.591M21 12h-2.25m-.386 6.364-1.591-1.591M12 18.75V21m-4.773-4.227-1.591 1.591M5.25 12H3m4.227-4.773L5.636 5.636M15.75 12a3.75 3.75 0 1 1-7.5 0 3.75 3.75 0 0 1 7.5 0Z"/></svg>"""

    let moon =
        raw """<svg class="hidden h-5 w-5 dark:block" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" d="M21.752 15.002A9.72 9.72 0 0 1 18 15.75c-5.385 0-9.75-4.365-9.75-9.75 0-1.33.266-2.597.748-3.752A9.753 9.753 0 0 0 3 11.25C3 16.635 7.365 21 12.75 21a9.753 9.753 0 0 0 9.002-5.998Z"/></svg>"""

    let sunSmall =
        raw """<svg class="h-5 w-5" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" d="M12 3v2.25m6.364.386-1.591 1.591M21 12h-2.25m-.386 6.364-1.591-1.591M12 18.75V21m-4.773-4.227-1.591 1.591M5.25 12H3m4.227-4.773L5.636 5.636M15.75 12a3.75 3.75 0 1 1-7.5 0 3.75 3.75 0 0 1 7.5 0Z"/></svg>"""

    let moonSmall =
        raw """<svg class="h-5 w-5" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" d="M21.752 15.002A9.72 9.72 0 0 1 18 15.75c-5.385 0-9.75-4.365-9.75-9.75 0-1.33.266-2.597.748-3.752A9.753 9.753 0 0 0 3 11.25C3 16.635 7.365 21 12.75 21a9.753 9.753 0 0 0 9.002-5.998Z"/></svg>"""

    let monitor =
        raw """<svg class="h-5 w-5" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" d="M9 17.25v1.007a3 3 0 0 1-.879 2.122L7.5 21h9l-.621-.621A3 3 0 0 1 15 18.257V17.25m6-12V15a2.25 2.25 0 0 1-2.25 2.25H5.25A2.25 2.25 0 0 1 3 15V5.25m18 0A2.25 2.25 0 0 0 18.75 3H5.25A2.25 2.25 0 0 0 3 5.25m18 0V12a2.25 2.25 0 0 1-2.25 2.25H5.25A2.25 2.25 0 0 1 3 12V5.25"/></svg>"""

    let hamburger =
        raw """<svg class="h-6 w-6" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" d="M3.75 6.75h16.5M3.75 12h16.5m-16.5 5.25h16.5"/></svg>"""

    let close =
        raw """<svg class="h-6 w-6" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor"><path stroke-linecap="round" stroke-linejoin="round" d="M6 18 18 6M6 6l12 12"/></svg>"""

type DisclosureConfig =
    { id: string
      openSignal: string
      triggerLabel: string
      rootClass: string
      triggerClass: string
      triggerContent: HtmlElement
      panelLabel: string
      panelClass: string
      panelContent: HtmlElement list }

module Disclosure =
    let panel (config:DisclosureConfig) =
        let buttonId = config.id + "-button"
        let panelId = config.id + "-panel"
        let escapeExpression =
            $"evt.key == 'Escape' && ${config.openSignal} && ((${config.openSignal} = false), document.getElementById('{buttonId}').focus(), true)"

        div {
            _class config.rootClass
            _data ("disclosure-root", "")
            _data ("signals", $"{{ {config.openSignal}: false }}")
            _data ("on:keydown__window", escapeExpression)
            button {
                _id buttonId
                _type "button"
                _ariaLabel config.triggerLabel
                _attr ("aria-controls", panelId)
                _data ("disclosure-button", "")
                _data ("attr:aria-expanded", $"${config.openSignal} ? 'true' : 'false'")
                _data ("on:click__stop", $"${config.openSignal} = !${config.openSignal}")
                _class config.triggerClass
                config.triggerContent
            }
            div {
                _id panelId
                _role "group"
                _ariaLabel config.panelLabel
                _data ("disclosure-panel", "")
                _data ("show", $"${config.openSignal}")
                _data ("on:click__outside", $"${config.openSignal} = false")
                _style "display:none"
                _class config.panelClass
                for item in config.panelContent do item
            }
        }

module Footer =
    let primary =
        div {
            _class "flex p-10 bg-gray-100 border-t border-gray-300 dark:bg-gray-900 dark:border-gray-700 dark:text-gray-300"
            div {
                _class "text-sm space-y-1"
                div { _class "w-12 h-12 text-emerald-600 dark:text-emerald-400"; MiniIcon.logo }
                p { "Andy Meier" }
                p { $"Copyright © {System.DateTime.Now.Year} - All right reserved" }
            }
            div { _class "grow" }
            div {
                _class "text-sm flex flex-col space-y-1"
                a { _href "/articles"; _class "underline cursor-pointer hover:text-emerald-600 dark:hover:text-emerald-400"; _dataOn ("click", Navigation.action "/articles"); "Articles" }
                a { _href "/privacy"; _class "underline cursor-pointer hover:text-emerald-600 dark:hover:text-emerald-400"; _dataOn ("click", Navigation.action "/privacy"); "Privacy" }
                a { _href "https://meiermade.com"; _class "whitespace-nowrap underline hover:text-emerald-600 dark:hover:text-emerald-400"; "Meier Made" }
                button {
                    _id "analytics-settings"
                    _type "button"
                    _ariaControls "cookie-consent-banner"
                    _ariaExpanded "false"
                    _class "text-left underline hover:text-emerald-600 dark:hover:text-emerald-400"
                    "Analytics settings"
                }
            }
        }

module TopNav =
    let private item (id:string, el:HtmlElement, href:string) =
        let baseClass =
            if id = "nav-home" then
                "p-2 text-base font-bold tracking-tight cursor-pointer hover:text-emerald-600 dark:hover:text-emerald-400"
            else
                "p-2 text-sm font-semibold cursor-pointer hover:text-emerald-600 dark:hover:text-emerald-400"

        a {
            _id id
            _href href
            _class baseClass
            _dataClass ("text-emerald-600", $"$selectedNav == '{id}'")
            _dataClass ("dark:text-emerald-400", $"$selectedNav == '{id}'")
            _dataClass ("text-gray-800", $"$selectedNav != '{id}'")
            _dataClass ("dark:text-gray-200", $"$selectedNav != '{id}'")
            _dataOn ("click", Navigation.action href)
            el
        }

    let private mobileItem (id:string, label:string, href:string) =
        a {
            _id $"{id}-mobile"
            _href href
            _class "block w-full cursor-pointer px-4 py-2 text-left text-sm transition hover:bg-gray-100 hover:text-emerald-600 focus:bg-gray-100 focus:text-emerald-600 focus-visible:outline-2 focus-visible:outline-inset focus-visible:outline-emerald-600 dark:hover:bg-gray-700/50 dark:hover:text-emerald-400 dark:focus:bg-gray-700/50 dark:focus:text-emerald-400 dark:focus-visible:outline-emerald-400"
            _dataClass ("text-emerald-600", $"$selectedNav == '{id}'")
            _dataClass ("dark:text-emerald-400", $"$selectedNav == '{id}'")
            _dataClass ("font-semibold", $"$selectedNav == '{id}'")
            _dataClass ("text-gray-700", $"$selectedNav != '{id}'")
            _dataClass ("dark:text-gray-300", $"$selectedNav != '{id}'")
            _dataOn ("click", Navigation.action href)
            text label
        }

    let private companyLink (className:string) =
        a {
            _href "https://meiermade.com"
            _class className
            text "Meier Made"
        }

    let private themeItem (value:string) (label:string) (icon:HtmlElement) =
        button {
            _type "button"
            _class "flex w-full items-center gap-2 px-4 py-2 text-sm transition hover:bg-gray-100 focus:bg-gray-100 focus-visible:outline-2 focus-visible:outline-inset focus-visible:outline-emerald-600 dark:hover:bg-gray-700/50 dark:focus:bg-gray-700/50 dark:focus-visible:outline-emerald-400"
            _dataClass ("text-emerald-600", $"$theme == '{value}'")
            _dataClass ("dark:text-emerald-400", $"$theme == '{value}'")
            _dataClass ("font-semibold", $"$theme == '{value}'")
            _dataClass ("text-gray-700", $"$theme != '{value}'")
            _dataClass ("dark:text-gray-300", $"$theme != '{value}'")
            _dataOn ("click", $"$theme = '{value}'; $themeOpen = false; setTheme('{value}'); document.getElementById('theme-button').focus()")
            icon
            text label
        }

    let private themeToggle =
        Disclosure.panel {
            id = "theme"
            openSignal = "themeOpen"
            triggerLabel = "Choose theme"
            rootClass = "relative inline-block text-left"
            triggerClass = "inline-flex w-full items-center justify-center rounded-md p-2 text-gray-600 hover:cursor-pointer hover:bg-gray-100 hover:text-emerald-600 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-600 dark:text-gray-400 dark:hover:bg-gray-800 dark:hover:text-emerald-400 dark:focus-visible:outline-emerald-400"
            triggerContent = span { _class "contents"; MiniIcon.sun; MiniIcon.moon }
            panelLabel = "Theme"
            panelClass = "absolute right-0 top-full z-40 mt-2 w-36 origin-top-right rounded-md bg-white py-1 shadow-lg ring-1 ring-black/5 dark:bg-gray-800 dark:ring-white/10"
            panelContent =
                [ themeItem "light" "Light" MiniIcon.sunSmall
                  themeItem "dark" "Dark" MiniIcon.moonSmall
                  themeItem "system" "System" MiniIcon.monitor ]
        }

    let private mobileDropdown =
        Disclosure.panel {
            id = "navigation"
            openSignal = "navigationOpen"
            triggerLabel = "Open navigation"
            rootClass = "relative inline-block text-left md:hidden"
            triggerClass = "inline-flex w-full items-center justify-center rounded-md p-2 text-gray-600 hover:cursor-pointer hover:bg-gray-100 hover:text-emerald-600 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-600 dark:text-gray-400 dark:hover:bg-gray-800 dark:hover:text-emerald-400 dark:focus-visible:outline-emerald-400"
            triggerContent = span { _class "contents"; MiniIcon.hamburger }
            panelLabel = "Navigation"
            panelClass = "absolute right-0 top-full z-40 mt-2 w-56 origin-top-right rounded-md bg-white py-1 shadow-lg ring-1 ring-black/5 dark:bg-gray-800 dark:ring-white/10"
            panelContent =
                [ mobileItem("nav-articles", "Articles", "/articles")
                  a {
                      _href "https://meiermade.com"
                      _class "block w-full px-4 py-2 text-left text-sm text-gray-700 transition hover:bg-gray-100 hover:text-emerald-600 focus:bg-gray-100 focus:text-emerald-600 focus-visible:outline-2 focus-visible:outline-inset focus-visible:outline-emerald-600 dark:text-gray-300 dark:hover:bg-gray-700/50 dark:hover:text-emerald-400 dark:focus:bg-gray-700/50 dark:focus:text-emerald-400 dark:focus-visible:outline-emerald-400"
                      "Meier Made"
                  } ]
        }

    let primary =
        nav {
            _id "top-nav"
            _class
                "relative h-14 bg-gray-100/80 py-1.5 px-4 border-b border-gray-300/80 backdrop-blur-md dark:bg-gray-900/80 dark:border-gray-700/80"
            _dataSignals "{theme: 'system'}"
            _dataInit "$theme = getInitialTheme(); applyTheme($theme)"
            div {
                _class "flex items-center gap-4"
                item("nav-home", div { _class "w-8 h-8 text-emerald-600 dark:text-emerald-400"; MiniIcon.logo }, "/")
                div { _class "grow" }
                div {
                    _class "hidden md:flex items-center gap-4"
                    item("nav-articles", text "Articles", "/articles")
                    companyLink "p-2 text-sm font-semibold text-gray-800 hover:text-emerald-600 dark:text-gray-200 dark:hover:text-emerald-400"
                }
                themeToggle
                mobileDropdown
            }
            div {
                _id "article-scroll-progress"
                _role "progressbar"
                _ariaLabel "Article reading progress"
                _attr ("aria-valuemin", "0")
                _attr ("aria-valuemax", "100")
                _attr ("aria-valuenow", "0")
                _class
                    "article-scroll-progress pointer-events-none absolute inset-x-0 -bottom-px h-0.5 origin-left bg-emerald-600 dark:bg-emerald-400"
                _style "transform:scaleX(0)"
            }
        }

module Page =
    let primary (page:HtmlElement) =
        div { _id "page-content"; _tabindex -1; _class "min-h-screen bg-gray-100 dark:bg-gray-900"; page }

module Analytics =
    let banner (policy:App.Privacy.BrowserPolicy) =
        let title, description =
            match policy.analytics with
            | App.Privacy.AnalyticsMode.OptIn ->
                "Optional analytics",
                "Optional browser analytics starts only if you accept."
            | App.Privacy.AnalyticsMode.DefaultOn ->
                "Analytics settings",
                "Limited first-party analytics may run by default in your region."

        div {
            _id "cookie-consent-banner"
            _role "dialog"
            _ariaLabelledby "analytics-consent-title"
            _ariaDescribedby "analytics-consent-description"
            _class "pointer-events-none fixed inset-x-0 bottom-0 z-50 hidden px-4 pb-4 sm:px-6 sm:pb-6"
            div {
                _class "pointer-events-auto ml-auto max-w-xl rounded-2xl border border-gray-200 bg-white p-5 shadow-2xl shadow-gray-950/15 sm:p-6 dark:border-gray-700 dark:bg-gray-900 dark:shadow-black/30"
                h2 {
                    _id "analytics-consent-title"
                    _tabindex -1
                    _class "text-base font-semibold text-gray-950 outline-none dark:text-gray-50"
                    title
                }
                p {
                    _id "analytics-consent-description"
                    _class "mt-2 text-sm/6 text-gray-600 dark:text-gray-300"
                    description
                    " It helps me understand traffic sources, site usage, performance, and errors; declining does not affect the site. A first-party cookie remembers an explicit choice for six months. Read the "
                    a {
                        _href "/privacy#analytics"
                        _class "font-semibold text-emerald-700 underline decoration-emerald-700/30 underline-offset-2 hover:decoration-emerald-700 dark:text-emerald-400 dark:decoration-emerald-400/30 dark:hover:decoration-emerald-400"
                        "privacy page"
                    }
                    "."
                }
                p {
                    _id "analytics-consent-error"
                    _ariaLive "polite"
                    _class "mt-3 hidden text-sm/6 font-medium text-red-700 dark:text-red-300"
                }
                div {
                    _class "mt-5 flex flex-col gap-3 sm:flex-row sm:justify-end"
                    button {
                        _id "analytics-reject"
                        _type "button"
                        _class "rounded-full border border-gray-300 bg-white px-4 py-2.5 text-sm font-semibold text-gray-800 transition hover:border-gray-400 hover:bg-gray-50 disabled:cursor-wait disabled:opacity-60 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-600 dark:border-gray-600 dark:bg-gray-900 dark:text-gray-100 dark:hover:border-gray-500 dark:hover:bg-gray-800 dark:focus-visible:outline-emerald-400"
                        "Decline analytics"
                    }
                    button {
                        _id "analytics-accept"
                        _type "button"
                        _class "rounded-full border border-gray-300 bg-white px-4 py-2.5 text-sm font-semibold text-gray-800 transition hover:border-gray-400 hover:bg-gray-50 disabled:cursor-wait disabled:opacity-60 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-emerald-600 dark:border-gray-600 dark:bg-gray-900 dark:text-gray-100 dark:hover:border-gray-500 dark:hover:bg-gray-800 dark:focus-visible:outline-emerald-400"
                        "Accept analytics"
                    }
                }
            }
        }

type Document =
    static member primary (metadata:PageMetadata, page:HtmlElement, otelEndpoint:string option, privacyPolicy:App.Privacy.BrowserPolicy, ?selectedNav:string) =
        let selectedNav = defaultArg selectedNav ""

        html {
            _lang "en"
            head {
                PageHead.titleElement metadata
                meta { _charset "UTF-8" }
                meta { _name "viewport"; _content "width=device-width, initial-scale=1.0" }
                PageHead.descriptionElement metadata
                PageHead.openGraphTitleElement metadata
                PageHead.openGraphDescriptionElement metadata
                meta { _property "og:type"; _content "website" }
                PageHead.openGraphUrlElement metadata
                PageHead.canonicalElement metadata
                script { js "let t=localStorage.getItem('theme');if(t==='dark'||(!t||t==='system')&&window.matchMedia('(prefers-color-scheme: dark)').matches){document.documentElement.classList.add('dark')}" }
                script {
                    _id "privacy-controls"
                    _type "module"
                    _src (Asset.fingerprinted "/scripts/privacy.js")
                    _data ("analytics-mode", App.Privacy.analyticsModeValue privacyPolicy)
                    match otelEndpoint with
                    | Some endpoint ->
                        _data ("otel-endpoint", endpoint)
                        _data ("telemetry-src", Asset.fingerprinted "/scripts/telemetry.js")
                    | None -> ()
                }
                link { _href (Asset.fingerprinted "/css/compiled.css"); _rel "stylesheet" }
                link { _href (Asset.fingerprinted "/css/prism.css"); _rel "stylesheet" }
                script { _type "module"; _src (Asset.fingerprinted "/scripts/datastar.1.0.2.js") }
            }
            body {
                _dataInit Navigation.initialize
                _dataOn ("scroll__window__throttle.100ms.trailing", Navigation.saveScroll)
                _dataOn ("popstate__window", Navigation.restoreAction)
                _dataSignals $"{{selectedNav: '{selectedNav}'}}"
                _class "bg-gray-200 dark:bg-gray-950"
                div {
                    _class "mx-auto max-w-7xl"
                    TopNav.primary
                    page
                    Footer.primary
                }
                Analytics.banner privacyPolicy
                script { js "function getInitialTheme(){return localStorage.getItem('theme')||'system'};function applyTheme(t){var d=document.documentElement,isDark=t==='dark'||(t==='system'&&window.matchMedia('(prefers-color-scheme: dark)').matches);d.classList.toggle('dark',isDark)};function setTheme(t){localStorage.setItem('theme',t);applyTheme(t);void window.renderMermaid?.(document)}" }
            }
        }

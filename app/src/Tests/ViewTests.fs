module ViewTests

open App.Articles
open App.Articles.Shared
open App.Common.View
open Expecto
open FSharp.ViewEngine
open System.Collections.Generic
open type Html

[<Tests>]
let tests =
    testList "View" [
        testList "Asset" [
            test "uses fingerprinted path from manifest when present" {
                let manifest = Dictionary<string, string>()
                manifest.Add("/css/compiled.css", "/css/compiled.abc123.css")

                let path = Asset.resolveWithManifest manifest "/css/compiled.css"

                Expect.equal path "/css/compiled.abc123.css" "Expected manifest fingerprinted path"
            }

            test "falls back to original path when manifest entry is missing" {
                let manifest = Dictionary<string, string>()
                manifest.Add("/css/other.css", "/css/other.abc123.css")

                let path = Asset.resolveWithManifest manifest "/css/compiled.css"

                Expect.equal path "/css/compiled.css" "Expected original path when manifest entry is missing"
            }
        ]

        testList "Personal site separation" [
            test "links to Meier Made without retaining company services or projects" {
                let navigation = Render.toHtmlDocString (TopNav.primary)
                let profile = Render.toHtmlDocString App.Index.View.aboutMe

                Expect.stringContains navigation "https://meiermade.com" "Expected a Meier Made company link"
                Expect.isFalse (navigation.Contains "Services") "Expected company services to leave personal navigation"
                Expect.isFalse (navigation.Contains "Projects") "Expected company projects to leave personal navigation"
                Expect.isFalse (profile.Contains "Currently working at") "Expected current-employer claim to be removed"
                Expect.stringContains profile "engineer and the owner" "Expected current role"
                Expect.stringContains profile "Meier Made, LLC" "Expected company ownership"
                Expect.stringContains profile "https://meiermade.com" "Expected company profile link"
                Expect.stringContains profile "St. Louis, Missouri" "Expected hometown"
                Expect.stringContains profile "New York City" "Expected current city"
                Expect.stringContains profile "The opinions shared here are my own." "Expected personal-opinions disclaimer"
            }

            test "uses Datastar navigation disclosures without emulated menus" {
                let navigation = Render.toHtmlDocString TopNav.primary

                Expect.stringContains navigation "href=\"/articles\"" "Expected progressive navigation links"
                Expect.stringContains navigation "!evt.ctrlKey" "Expected modified clicks to retain native anchor behavior"
                Expect.isFalse (navigation.Contains "click__prevent") "Expected default navigation to be prevented only for eligible clicks"
                Expect.stringContains navigation "data-disclosure-root" "Expected hand-rolled Datastar disclosures"
                Expect.stringContains navigation "aria-expanded" "Expected accessible disclosure triggers"
                Expect.stringContains navigation "aria-controls" "Expected triggers to identify disclosure panels"
                Expect.isFalse (navigation.Contains "role=\"menu\"") "Expected native links and buttons instead of emulated menus"
                Expect.isFalse (navigation.Contains "role=\"menuitem") "Expected native links and buttons instead of emulated menu items"
                Expect.isFalse (navigation.Contains "<el-") "Expected no Tailwind Elements navigation"
            }
        ]

        testList "Articles" [
            test "uses accessible Datastar disclosures with canonical filter links" {
                let page =
                    App.Articles.View.articlesPage {
                        articles = []
                        filters = { search = None; tag = None; publishedYear = None }
                        tags = [ "Finance" ]
                        years = [ 2026 ]
                    }

                let html = Render.toHtmlDocString page

                Expect.stringContains html "data-filter-control=\"tag\"" "Expected a hand-rolled Datastar tag disclosure"
                Expect.stringContains html "data-filter-control=\"year\"" "Expected a hand-rolled Datastar year disclosure"
                Expect.stringContains html "href=\"/articles?tag=Finance\"" "Expected canonical tag filter link"
                Expect.stringContains html "href=\"/articles?year=2026\"" "Expected canonical year filter link"
                Expect.isFalse (html.Contains "role=\"combobox\"") "Expected no emulated combobox"
                Expect.isFalse (html.Contains "role=\"listbox\"") "Expected no emulated listbox"
                Expect.isFalse (html.Contains "data-select-root") "Expected no custom select state machine"
                Expect.isFalse (html.Contains "<el-") "Expected no Tailwind Elements controls"
                Expect.isFalse (html.Contains "<select") "Expected article filters not to render native selects"
            }

            test "encodes filter values as HTML attributes instead of JavaScript literals" {
                let unsafeValue = "Andy's \"Notes\""
                let page =
                    App.Articles.View.articlesPage {
                        articles = []
                        filters = { search = None; tag = None; publishedYear = None }
                        tags = [ unsafeValue ]
                        years = [ 2026 ]
                    }

                let html = Render.toHtmlDocString page
                let expressions =
                    System.Text.RegularExpressions.Regex.Matches(html, "data-on:[^=]+=\"([^\"]*)\"")
                    |> Seq.cast<System.Text.RegularExpressions.Match>
                    |> Seq.map _.Groups.[1].Value
                    |> String.concat "\n"

                Expect.stringContains html "Andy&#39;s &quot;Notes&quot;" "Expected safely encoded option text"
                Expect.isFalse (expressions.Contains unsafeValue) "Expected user-derived values to stay out of Datastar expressions"
                Expect.isFalse (expressions.Contains "Andy&#39;s") "Expected encoded user-derived values to stay out of Datastar expressions"
                Expect.isFalse (html.Contains "data-select-root") "Expected no custom select state machine"
            }

            test "does not let article permalinks escape attributes or Datastar expressions" {
                let unsafePermalink = "\" onmouseover=\"alert(1)"
                let article : Article =
                    { permalink = unsafePermalink
                      title = "Example article"
                      summary = "Summary"
                      cover = "https://assets.meiermade.com/andymeier/articles/shared/cover.webp"
                      tags = [||]
                      createdAt = System.DateTimeOffset(2026, 2, 1, 0, 0, 0, System.TimeSpan.Zero)
                      page = empty }

                let html = ArticleCard.summary article |> Render.toHtmlDocString

                Expect.isFalse (html.Contains "onmouseover=") "Expected permalink not to create an HTML event attribute"
                Expect.stringContains html "/articles/%22%20onmouseover%3D%22alert%281%29" "Expected permalink to be encoded as one URL segment"
            }

            test "restricts background images to escaped HTTP resources" {
                let background = SafeOutput.tryBackgroundImageStyle "https://example.com/image.png?value=')"
                let unsafeBackground = SafeOutput.tryBackgroundImageStyle "javascript:alert(1)"

                Expect.isNone unsafeBackground "Expected an unsafe image URL scheme to be rejected"
                Expect.isSome background "Expected an HTTPS background image"
                Expect.isFalse (background.Value.Contains "value=')") "Expected untrusted CSS delimiters to be encoded"
                Expect.stringContains background.Value "%27%29" "Expected quote and parenthesis CSS characters to be encoded"
            }

            test "identifies articles as personal writing" {
                let page =
                    App.Articles.View.articlesPage {
                        articles = []
                        filters = { search = None; tag = None; publishedYear = None }
                        tags = []
                        years = []
                    }
                    |> Render.toHtmlDocString

                Expect.stringContains page "Personal notes by Andy Meier. Unless otherwise noted, they reflect my own views—not those of clients, collaborators, or organizations I work with." "Expected personal-writing subtitle"
                Expect.isFalse (page.Contains "My thoughts on finance and technology") "Expected superseded subtitle to be removed"
            }

            test "shows article tags on the detail page" {
                let metadata : ArticleMetadata =
                    { permalink = "article"
                      title = "Example article"
                      summary = "Summary"
                      cover = "https://assets.meiermade.com/andymeier/articles/shared/cover.webp"
                      tags = [| "Engineering"; "Finance" |]
                      createdAt = System.DateTimeOffset(2026, 2, 1, 0, 0, 0, System.TimeSpan.Zero) }

                let html = ArticlePage.primary metadata [] |> Render.toHtmlDocString

                Expect.stringContains html "Engineering" "Expected article tag on detail page"
                Expect.stringContains html "Finance" "Expected article tag on detail page"
            }

            test "scopes clear actions to search or added filters" {
                let searchOnly =
                    App.Articles.View.articlesPage {
                        articles = []
                        filters = { search = Some "engine"; tag = None; publishedYear = None }
                        tags = [ "Engineering" ]
                        years = [ 2026 ]
                    }
                    |> Render.toHtmlDocString

                let searchAndFilter =
                    App.Articles.View.articlesPage {
                        articles = []
                        filters = { search = Some "engine"; tag = Some "Engineering"; publishedYear = None }
                        tags = [ "Engineering" ]
                        years = [ 2026 ]
                    }
                    |> Render.toHtmlDocString

                Expect.stringContains searchOnly "Clear search" "Expected a dedicated search clear action"
                Expect.isFalse (searchOnly.Contains "Clear filters") "Expected filter clear action only after adding a filter"
                Expect.isFalse (searchOnly.Contains "type=\"search\"") "Expected no browser-native search clear control"
                Expect.stringContains searchAndFilter "Clear filters" "Expected filters-only clear action"
                Expect.stringContains searchAndFilter "href=\"/articles?search=engine\"" "Expected filter clearing to preserve search"
            }
        ]

        testList "Navigation" [
            test "serializes pushed URLs as JavaScript data" {
                let script = App.Common.Handler.historyScript "');alert(1)//"

                Expect.isFalse (script.Contains "pushState(null, '', '');alert(1)//')") "Expected URL not to break out of the JavaScript string"
                Expect.stringContains script "\\u0027);alert(1)//" "Expected the quote to be JavaScript encoded"
                Expect.stringContains script "window.history.pushState" "Expected history update script"
            }
        ]

        testList "Document" [
            test "disabled analytics omits telemetry attributes but preserves consent in every region" {
                let metadata : PageMetadata =
                    { canonicalPath = "/"
                      description = "Personal notes by Andy Meier."
                      title = "Andy Meier" }
                for country in [ Some "DE"; Some "US"; None ] do
                    let policy = App.Privacy.resolve country
                    let html =
                        Document.primary(metadata, Page.primary (div { "Hello" }), None, policy)
                        |> Render.toHtmlDocString
                    Expect.isFalse (html.Contains "data-otel-endpoint") "no public endpoint"
                    Expect.isFalse (html.Contains "data-telemetry-src") "no telemetry module attribute"
                    Expect.isFalse (html.Contains "/scripts/telemetry") "no eager or deferred telemetry source"
                    Expect.stringContains html "src=\"/scripts/privacy.js\"" "consent controller remains"
                    Expect.stringContains html $"data-analytics-mode=\"{App.Privacy.analyticsModeValue policy}\"" "regional policy remains"
                    for control in [ "Analytics settings"; "Accept analytics"; "Decline analytics" ] do
                        Expect.stringContains html control "consent controls remain usable"
            }

            test "includes patchable metadata, navigation, and delayed analytics loading" {
                let metadata : PageMetadata =
                    { canonicalPath = "/"
                      description = "Personal notes by Andy Meier."
                      title = "Andy Meier" }
                let privacyPolicy = App.Privacy.resolve (Some "DE")
                let doc = Document.primary(metadata, Page.primary (div { "Hello" }), Some "https://otel.meiermade.com", privacyPolicy, "nav-home")

                let html = Render.toHtmlDocString doc

                Expect.stringContains html "<title id=\"document-title\">Andy Meier</title>" "Expected patchable page title"
                Expect.stringContains html "id=\"canonical-url\" rel=\"canonical\" href=\"https://andymeier.dev/\"" "Expected patchable canonical URL"
                Expect.stringContains html "id=\"page-content\" tabindex=\"-1\"" "Expected a stable, focusable patch root"
                Expect.stringContains html "data-on:popstate__window=" "Expected browser history restoration"
                Expect.stringContains html "window.history.scrollRestoration = &#39;manual&#39;" "Expected application-controlled history scroll restoration"
                Expect.stringContains html "data-on:scroll__window__throttle.100ms.trailing=" "Expected throttled per-entry scroll persistence"
                Expect.stringContains html "selectedNav: &#39;nav-home&#39;" "Expected encoded nav signal to render"
                Expect.stringContains html "cookie-consent-banner" "Expected consent banner"
                Expect.stringContains html "analytics-consent-title" "Expected consent dialog title"
                Expect.stringContains html "Optional analytics" "Expected strict-region consent heading"
                Expect.stringContains html "rounded-2xl" "Expected compact consent card"
                Expect.stringContains html "Decline analytics" "Expected symmetrical decline action"
                Expect.stringContains html "Accept analytics" "Expected symmetrical accept action"
                Expect.stringContains html "Optional browser analytics starts only if you accept." "Expected accurate first-party analytics disclosure"
                Expect.stringContains html "href=\"/privacy#analytics\"" "Expected detailed privacy disclosure"
                Expect.stringContains html "Analytics settings" "Expected a persistent withdrawal control"
                Expect.stringContains html "aria-controls=\"cookie-consent-banner\"" "Expected settings relationship"
                Expect.isFalse (html.Contains "fetch('/privacy/consent'") "Expected no embedded consent implementation"
                Expect.isFalse (html.Contains "window.applyAnalyticsConsent") "Expected consent behavior in the typed client bundle"
                Expect.isFalse (html.Contains "googletagmanager.com") "Expected no Google tag loader"
                Expect.isFalse (html.Contains "gtag(") "Expected no Google Analytics calls"
                Expect.stringContains html "id=\"privacy-controls\"" "Expected the lightweight privacy module"
                Expect.stringContains html "src=\"/scripts/privacy.js\"" "Expected the privacy controller asset"
                Expect.stringContains html "data-analytics-mode=\"opt-in\"" "Expected the server-owned regional policy"
                Expect.stringContains html "data-telemetry-src=\"/scripts/telemetry.js\"" "Expected deferred telemetry asset metadata"
                Expect.stringContains html "data-otel-endpoint=\"https://otel.meiermade.com\"" "Expected the public OTLP endpoint"
                Expect.isFalse (html.Contains "window.loadOpenTelemetry") "Expected no global telemetry lifecycle API"
                Expect.isFalse (html.Contains "snowplow") "Expected Snowplow to be removed"
                Expect.isFalse (html.Contains "tailwindplus-elements") "Expected no Tailwind Elements runtime"
                Expect.isFalse (html.Contains "const itemsFor") "Expected no global interaction state machine"
                Expect.isFalse (html.Contains "data-select-root") "Expected no custom select state machine"
                Expect.isFalse (html.Contains "<el-") "Expected no Tailwind Elements markup"
            }
        ]
    ]

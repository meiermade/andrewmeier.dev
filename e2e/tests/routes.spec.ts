import { expect, test } from '@playwright/test'

const isRemote = process.env.E2E_SCOPE === 'deployed'
const siteBaseUrl = process.env.SITE_E2E_BASE_URL ?? (isRemote ? 'https://andymeier.dev' : 'http://127.0.0.1:5051')
const otelEndpoint = isRemote ? 'https://otel.meiermade.com' : 'https://otel.test'
const expectedAnalyticsMode = process.env.E2E_EXPECTED_ANALYTICS_MODE
const isTelemetryScriptRequest = (url: string) => /^\/scripts\/telemetry(?:\.[a-f0-9]+)?\.js$/.test(new URL(url).pathname)
const testImage = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=', 'base64')

test.beforeEach(async ({ page }) => {
  if (!isRemote) {
    await page.route('https://assets.meiermade.com/andymeier/articles/**', route =>
      route.fulfill({ contentType: 'image/png', body: testImage }),
    )
  }

  await page.context().addCookies([{
    name: 'analytics-consent',
    value: 'v1.declined.2026-08-16.0',
    url: siteBaseUrl,
    sameSite: 'Lax',
  }])
})

test('recognizes local and fingerprinted telemetry script requests', () => {
  expect(isTelemetryScriptRequest('https://andymeier.dev/scripts/telemetry.js')).toBe(true)
  expect(isTelemetryScriptRequest('https://andymeier.dev/scripts/telemetry.033ef8688173.js')).toBe(true)
  expect(isTelemetryScriptRequest('https://andymeier.dev/scripts/privacy.8683adb54986.js')).toBe(false)
})

test('homepage renders recent articles', async ({ page }) => {
  const response = await page.goto('/', { waitUntil: 'domcontentloaded' })

  expect(response?.status()).toBe(200)
  expect(response?.headers()['strict-transport-security']).toContain('max-age=')
  expect(response?.headers()['x-content-type-options']).toBe('nosniff')
  expect(response?.headers()['referrer-policy']).toBe('strict-origin-when-cross-origin')
  expect(response?.headers()['content-security-policy']).toContain("frame-ancestors 'none'")
  await expect(page.getByRole('heading', { name: 'Andy Meier', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Meier Made, LLC', exact: true })).toHaveAttribute('href', 'https://meiermade.com')
  await expect(page.getByText('Originally from St. Louis, Missouri', { exact: false })).toBeVisible()
  await expect(page.getByText('The opinions shared here are my own.', { exact: false })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Recent articles', exact: true })).toBeVisible()
  await expect(page.locator('#top-nav')).toHaveCSS('position', 'sticky')
  await expect(page.locator('#top-nav')).toHaveCSS('top', '0px')
  await expect(page.locator('#top-nav')).toHaveCSS('backdrop-filter', 'blur(12px)')
  await expect.poll(async () => page.locator('#top-nav').evaluate(nav => getComputedStyle(nav).backgroundColor)).toContain('/')
  await expect(page.getByRole('progressbar', { name: 'Article reading progress' })).toBeHidden()
})

test('internal navigation preserves history and restores entry scroll positions', async ({ page }) => {
  const datastarRequests: string[] = []
  page.on('request', request => {
    if (request.headers()['datastar-request'] === 'true') datastarRequests.push(new URL(request.url()).pathname)
  })

  await page.goto('/', { waitUntil: 'domcontentloaded' })
  const documentMarker = await page.evaluate(() => {
    const marker = crypto.randomUUID()
    ;(window as Window & { documentMarker?: string }).documentMarker = marker
    return marker
  })

  await page.evaluate(() => {
    const style = document.createElement('style')
    style.textContent = '#page-content { min-height: 3000px !important; }'
    document.head.append(style)
    document.documentElement.style.scrollBehavior = 'auto'
    window.scrollTo(0, document.documentElement.scrollHeight)
  })
  const homeScrollY = await page.evaluate(() => window.scrollY)
  expect(homeScrollY).toBeGreaterThan(0)

  await expect(page.locator('#nav-articles')).toHaveAttribute('href', '/articles')
  await page.getByRole('link', { name: 'Articles', exact: true }).last().click()
  await expect(page).toHaveURL('/articles')
  await expect(page.getByRole('heading', { name: 'Articles', exact: true })).toBeVisible()
  await expect(page).toHaveTitle('Articles | Andy Meier')
  await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', 'https://andymeier.dev/articles')
  await expect(page.locator('#page-content')).toBeFocused()
  await expect(page.locator('#page-content')).toHaveCSS('outline-style', 'none')
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(0)
  expect(await page.evaluate(() => (window as Window & { documentMarker?: string }).documentMarker)).toBe(documentMarker)

  const currentRouteHistoryLength = await page.evaluate(() => history.length)
  await page.locator('#nav-articles').click()
  await expect(page).toHaveURL('/articles')
  await expect.poll(() => page.evaluate(() => history.length)).toBe(currentRouteHistoryLength)

  await page.getByRole('link', { name: 'Personal Infrastructure', exact: true }).click()
  await expect(page).toHaveURL('/articles/personal-infrastructure')
  await expect(page.getByRole('heading', { name: 'Personal Infrastructure', exact: true }).first()).toBeVisible()
  await expect(page).toHaveTitle('Personal Infrastructure | Andy Meier')
  await expect(page.locator('link[rel="canonical"]')).toHaveAttribute('href', 'https://andymeier.dev/articles/personal-infrastructure')

  await page.goBack()
  await expect(page).toHaveURL('/articles')
  await expect(page.getByRole('heading', { name: 'Articles', exact: true })).toBeVisible()
  await expect(page).toHaveTitle('Articles | Andy Meier')

  await page.goBack()
  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: 'Andy Meier', exact: true })).toBeVisible()
  await expect(page).toHaveTitle('Andy Meier')
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(homeScrollY)

  await page.goForward()
  await expect(page).toHaveURL('/articles')
  await expect(page.getByRole('heading', { name: 'Articles', exact: true })).toBeVisible()
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(0)

  await page.evaluate(() => window.scrollTo(0, 1200))
  const articlesScrollY = await page.evaluate(() => window.scrollY)
  expect(articlesScrollY).toBeGreaterThan(0)
  await expect.poll(() => page.evaluate(() => history.state?.meierMadeScrollY), { timeout: 2000 }).toBe(articlesScrollY)

  await page.goBack()
  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: 'Andy Meier', exact: true })).toBeVisible()
  await page.goForward()
  await expect(page).toHaveURL('/articles')
  await expect(page.getByRole('heading', { name: 'Articles', exact: true })).toBeVisible()
  await expect.poll(() => page.evaluate(() => window.scrollY)).toBe(articlesScrollY)

  expect(await page.evaluate(() => (window as Window & { documentMarker?: string }).documentMarker)).toBe(documentMarker)
  expect(datastarRequests).toEqual(['/articles', '/articles/personal-infrastructure', '/articles', '/', '/articles', '/', '/articles'])
})

test('modified internal-link clicks retain native new-tab behavior', async ({ page, context }) => {
  await page.goto('/', { waitUntil: 'domcontentloaded' })
  const existingPageCount = context.pages().length
  const modifier = process.platform === 'darwin' ? 'Meta' : 'Control'

  await page.locator('#nav-articles').click({ modifiers: [modifier] })

  await expect(page).toHaveURL('/')
  await expect.poll(() => context.pages().length).toBe(existingPageCount + 1)
  const openedPage = context.pages().at(-1)!
  await openedPage.waitForLoadState('domcontentloaded')
  await expect(openedPage).toHaveURL('/articles')
  await openedPage.close()
})

test('mobile navigation closes after enhanced navigation', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/', { waitUntil: 'domcontentloaded' })
  const menu = page.getByRole('group', { name: 'Navigation' })

  await page.getByRole('button', { name: 'Open navigation' }).click()
  await expect(menu).toBeVisible()
  await menu.getByRole('link', { name: 'Articles' }).click()

  await expect(page).toHaveURL('/articles')
  await expect(menu).toBeHidden()
  await expect(page.locator('#page-content')).toBeFocused()
})

test('internal links retain a full-document fallback without JavaScript', async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false })
  await context.addCookies([{
    name: 'analytics-consent',
    value: 'v1.declined.2026-08-16.0',
    url: siteBaseUrl,
    sameSite: 'Lax',
  }])
  const page = await context.newPage()

  await page.goto('/')
  await page.getByRole('link', { name: 'Articles', exact: true }).first().click()

  await expect(page).toHaveURL('/articles')
  await expect(page.getByRole('heading', { name: 'Articles', exact: true })).toBeVisible()
  await expect(page).toHaveTitle('Articles | Andy Meier')
  await context.close()
})

test('personal infrastructure diagrams render after client-side navigation', async ({ page }) => {
  await page.goto('/', { waitUntil: 'domcontentloaded' })
  await page.getByRole('link', { name: 'Personal Infrastructure', exact: true }).click()
  await expect(page).toHaveURL('/articles/personal-infrastructure')
  await expect(page.locator('.article-mermaid svg')).toHaveCount(3)
})

test('articles index renders and opens a source-controlled article', async ({ page }) => {
  const response = await page.goto('/articles', { waitUntil: 'domcontentloaded' })

  expect(response?.status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Articles', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'F# Semantic Kernel', exact: true })).toBeVisible()

  const articleResponse = await page.goto('/articles/fsharp-semantic-kernel', { waitUntil: 'domcontentloaded' })
  expect(articleResponse?.status()).toBe(200)
  await expect(page.locator('article')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'F# Semantic Kernel', exact: true })).toBeVisible()
  await expect(page.getByText('Microsoft’s Semantic Kernel SDK', { exact: false }).first()).toBeVisible()
  await expect(page.locator('code.language-fsharp .token.keyword').first()).toBeVisible()
  await expect(page.getByRole('button', { name: 'Copy' }).first()).toBeVisible()
})

test('development environment article presents the current setup', async ({ page }) => {
  const response = await page.goto('/articles/dev-env', { waitUntil: 'domcontentloaded' })

  expect(response?.status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Development Environment', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Pi coding agent', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'JetBrains IDEs', exact: true })).toBeVisible()
  await expect(page.getByText('Windows Subsystem for Linux', { exact: false })).toHaveCount(0)
  await expect(page.locator('article > div')).toHaveCSS('padding-bottom', '32px')
})

test('article pages keep the top navigation visible and track reading progress', async ({ page }) => {
  await page.goto('/articles', { waitUntil: 'domcontentloaded' })
  await page.getByRole('link', { name: 'Personal Infrastructure', exact: true }).click()
  await expect(page).toHaveURL('/articles/personal-infrastructure')

  const nav = page.locator('#top-nav')
  const progress = page.getByRole('progressbar', { name: 'Article reading progress' })

  await expect(nav).toHaveCSS('position', 'sticky')
  await expect(nav).toHaveCSS('top', '0px')
  await expect(nav).toHaveCSS('height', '56px')
  await expect(progress).toHaveCSS('display', 'block')
  await expect(progress).toHaveAttribute('aria-valuenow', '0')

  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight * 0.4))
  await expect.poll(async () => Number(await progress.getAttribute('aria-valuenow'))).toBeGreaterThan(0)
  await expect(progress).toBeVisible()
  await expect.poll(async () => nav.evaluate(element => Math.round(element.getBoundingClientRect().top))).toBe(0)

  await page.locator('article').evaluate(article => {
    const articleBottom = article.getBoundingClientRect().bottom + window.scrollY
    window.scrollTo(0, articleBottom - window.innerHeight)
  })
  await expect(progress).toHaveAttribute('aria-valuenow', '100')

  await page.locator('#nav-articles').click()
  await expect(page).toHaveURL('/articles')
  await expect(nav).toHaveCSS('position', 'sticky')
  await expect(nav).toHaveCSS('top', '0px')
  await expect(progress).toBeHidden()
})

test('article search and source-controlled detail content are deterministic', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('theme', 'light'))
  await page.goto('/articles?search=semantic', { waitUntil: 'domcontentloaded' })
  await expect(page.getByRole('heading', { name: 'F# Semantic Kernel', exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Clear search' })).toBeVisible()

  const response = await page.goto('/articles/personal-infrastructure', { waitUntil: 'domcontentloaded' })
  expect(response?.status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Personal Infrastructure', exact: true }).first()).toBeVisible()
  await expect(page.getByText('DevOps', { exact: true }).first()).toBeVisible()

  await expect(page.getByRole('heading', { name: 'Architecture at a glance', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'System context', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Runtime', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Deployment', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Why Google Cloud', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Kubernetes without platform engineering', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Personal applications and agents', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Cloudflare for networking and access', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'OpenTelemetry and ClickStack', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Pulumi and environments', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'GitHub for delivery', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'andymeier.dev', exact: true })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'How it is organized', exact: true })).toBeVisible()
  await expect(page.getByText('The site keeps article content in source control', { exact: false })).toHaveCount(0)
  await expect(page.getByText('Benji and Minnie are my two long-running AI agents', { exact: false })).toBeVisible()
  await expect(page.getByText('Named browser and business occurrences use OpenTelemetry EventRecords', { exact: false })).toBeVisible()
  await expect(page.getByText('small, coherent set of tools with strong APIs', { exact: false })).toBeVisible()
  await expect(page.getByText('narrowly scoped viewer roles', { exact: false })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Pi coding agent', exact: true })).toHaveAttribute('href', 'https://pi.dev/')
  await expect(page.getByRole('link', { name: 'source for andymeier.dev', exact: true })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'ClickHouse and ClickStack', exact: true })).toHaveAttribute('href', 'https://clickhouse.com/docs/use-cases/observability/clickstack/overview')
  await expect(page.locator('a[href="https://github.com/meiermade/agent"]')).toHaveCount(0)

  const diagrams = [
    { locator: page.locator('[data-system-context]'), title: 'Personal infrastructure system context' },
    { locator: page.locator('[data-container-view]'), title: 'Personal infrastructure runtime' },
    { locator: page.locator('[data-deployment-view]'), title: 'Personal infrastructure deployment' },
  ]

  for (const diagram of diagrams) {
    await expect(diagram.locator.locator('svg')).toBeVisible()
    await expect(diagram.locator.locator('svg title')).toHaveText(diagram.title)
    await expect(diagram.locator.locator('svg desc')).not.toBeEmpty()
    await expect(diagram.locator.locator('svg')).not.toHaveAttribute('aria-roledescription', 'error')
  }

  await expect(diagrams[0].locator.locator('svg desc')).toContainText('GitHub Actions, Pulumi Cloud, Google Cloud, Cloudflare, and Google Workspace')

  const [, , diagramWidth, diagramHeight] = (await diagrams[0].locator.locator('svg').getAttribute('viewBox'))!.split(' ').map(Number)
  expect(diagramHeight).toBeGreaterThan(diagramWidth)

  const lightDiagrams = await Promise.all(diagrams.map(diagram => diagram.locator.locator('svg').innerHTML()))
  await page.getByRole('button', { name: 'Choose theme' }).click()
  await page.getByRole('button', { name: 'Dark' }).click()
  await expect(page.locator('html')).toHaveClass(/dark/)
  await expect.poll(async () => {
    const darkDiagrams = await Promise.all(diagrams.map(diagram => diagram.locator.locator('svg').innerHTML()))
    const haveErrors = await Promise.all(diagrams.map(diagram => diagram.locator.locator('svg').getAttribute('aria-roledescription')))
    return haveErrors.every(role => role !== 'error') && darkDiagrams.every((svg, index) => svg !== lightDiagrams[index])
  }).toBe(true)

  for (const diagram of diagrams) {
    await expect(diagram.locator.locator('svg title')).toHaveText(diagram.title)
  }

  for (const excludedTopic of ['Meier Made Platform', 'PostgreSQL', 'Cloud SQL', 'Auth0', 'Dagster', 'Airbyte', 'Metabase', 'Raspberry Pi', 'Penpot', 'Redis', 'Memorystore']) {
    await expect(page.getByText(excludedTopic, { exact: false })).toHaveCount(0)
  }
})

test('article filter disclosures support native keyboard selection', async ({ page }) => {
  for (const filter of [
    { name: 'tag', ariaLabel: 'Tag filter', value: '.NET' },
    { name: 'year', ariaLabel: 'Published year filter', value: '2026' },
  ]) {
    await page.goto('/articles', { waitUntil: 'domcontentloaded' })
    await page.getByText('+ Add filter', { exact: true }).click()

    const disclosure = page.locator(`[data-filter-control="${filter.name}"]`)
    const button = disclosure.getByRole('button', { name: filter.ariaLabel })
    const panel = disclosure.locator('[data-disclosure-panel]')
    const option = disclosure.getByRole('link', { name: filter.value, exact: true })

    await button.focus()
    await button.press('Enter')
    await expect(panel).toBeVisible()
    await expect(button).toHaveAttribute('aria-expanded', 'true')
    await page.keyboard.press('Tab')
    if (await panel.evaluate(element => element === document.activeElement)) {
      await page.keyboard.press('Tab')
    }
    await expect(option).toBeFocused()

    await page.keyboard.press('Escape')
    await expect(panel).toBeHidden()
    await expect(button).toBeFocused()

    await button.press('Space')
    await expect(panel).toBeVisible()
    await page.keyboard.press('Tab')
    if (await panel.evaluate(element => element === document.activeElement)) {
      await page.keyboard.press('Tab')
    }
    await expect(option).toBeFocused()
    await page.keyboard.press('Enter')

    await expect.poll(() => new URL(page.url()).searchParams.get(filter.name)).toBe(filter.value)
    expect(new URL(page.url()).searchParams.has('datastar')).toBe(false)
  }
})

test('navigation disclosures use native keyboard behavior without Tailwind Elements', async ({ page }) => {
  await page.goto('/', { waitUntil: 'domcontentloaded' })

  await expect(page.locator('el-dropdown, el-menu, el-select, el-options, el-option')).toHaveCount(0)
  await expect(page.locator('[role="menu"], [role="menuitem"], [role="menuitemradio"]')).toHaveCount(0)

  const themeButton = page.getByRole('button', { name: 'Choose theme' })
  const themePanel = page.locator('#theme-panel')
  await themeButton.focus()
  await themeButton.press('Enter')
  await expect(themePanel).toBeVisible()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: 'Light' })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('button', { name: 'Dark' })).toBeFocused()
  await page.keyboard.press('Enter')
  await expect(themePanel).toBeHidden()
  await expect(themeButton).toBeFocused()
  await expect(page.locator('html')).toHaveClass(/dark/)

  await page.setViewportSize({ width: 390, height: 844 })
  const navigationButton = page.getByRole('button', { name: 'Open navigation' })
  const navigationPanel = page.locator('#navigation-panel')
  await navigationButton.focus()
  await navigationButton.press('Space')
  await expect(navigationPanel).toBeVisible()
  await page.keyboard.press('Tab')
  await expect(navigationPanel.getByRole('link', { name: 'Articles' })).toBeFocused()
  await page.keyboard.press('Escape')
  await expect(navigationPanel).toBeHidden()
  await expect(navigationButton).toBeFocused()
})

test('privacy page explains current regional analytics controls', async ({ page }) => {
  const response = await page.goto('/privacy', { waitUntil: 'domcontentloaded' })

  expect(response?.status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Privacy', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Browser analytics', exact: true })).toBeVisible()
  await expect(page.getByText('In locations where consent is required', { exact: false })).toBeVisible()
  await expect(page.getByText('Analytics settings control in the footer', { exact: false })).toBeVisible()
  await expect(page.getByText('Google Ads', { exact: false })).toHaveCount(0)
  await expect(page.getByText('LinkedIn Insight Tag', { exact: false })).toHaveCount(0)
})

test('policy check exposes only whether the trusted regional mode matches', async ({ request }) => {
  test.skip(isRemote, 'Simulated country headers are trusted only by the local origin.')
  const us = await request.get('/privacy/policy-check/default-on', { headers: { 'CF-IPCountry': 'US' } })
  const strict = await request.get('/privacy/policy-check/opt-in', { headers: { 'CF-IPCountry': 'DE' } })
  const mismatch = await request.get('/privacy/policy-check/opt-in', { headers: { 'CF-IPCountry': 'US' } })
  const unsupported = await request.get('/privacy/policy-check/unsupported', { headers: { 'CF-IPCountry': 'US' } })

  expect(us.status()).toBe(200)
  expect(strict.status()).toBe(200)
  expect(mismatch.status()).toBe(409)
  expect(unsupported.status()).toBe(400)
  expect(us.headers()['cache-control']).toBe('no-store')
})

test('starts limited analytics by default for US visitors without showing the banner', async ({ page }) => {
  test.skip(isRemote, 'Simulated country headers are trusted only by the local origin.')
  await page.context().clearCookies()
  await page.setExtraHTTPHeaders({ 'CF-IPCountry': 'US' })
  const otlpRequests: string[] = []
  await page.route(`${otelEndpoint}/**`, async route => {
    otlpRequests.push(route.request().url())
    await route.fulfill({ status: 200, contentType: 'application/x-protobuf', body: Buffer.alloc(0) })
  })

  await page.goto('/', { waitUntil: 'domcontentloaded' })

  await expect(page.getByRole('dialog', { name: 'Analytics settings' })).toBeHidden()
  await expect(page.locator('#privacy-controls')).toHaveAttribute('data-analytics-mode', 'default-on')
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).not.toBeNull()
  await expect.poll(() => otlpRequests.length).toBeGreaterThan(0)
  expect((await page.context().cookies()).find(cookie => cookie.name === 'analytics-consent')).toBeUndefined()
})

test('requires consent and defers telemetry for strict and unknown locations', async ({ page }) => {
  test.skip(isRemote, 'Simulated country headers are trusted only by the local origin.')
  for (const countryCode of ['DE', 'CA', 'XX', 'T1']) {
    await page.context().clearCookies()
    await page.setExtraHTTPHeaders({ 'CF-IPCountry': countryCode })
    const telemetryRequests: string[] = []
    page.on('request', request => {
      if (isTelemetryScriptRequest(request.url())) telemetryRequests.push(request.url())
    })

    await page.goto('/', { waitUntil: 'domcontentloaded' })

    await expect(page.getByRole('dialog', { name: 'Optional analytics' })).toBeVisible()
    await expect(page.locator('#privacy-controls')).toHaveAttribute('data-analytics-mode', 'opt-in')
    expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).toBeNull()
    expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-traffic-attribution'))).toBeNull()
    expect(telemetryRequests).toEqual([])
  }
})

test('preserves an explicit decline in a default-on US region', async ({ page }) => {
  test.skip(isRemote, 'Simulated country headers are trusted only by the local origin.')
  await page.setExtraHTTPHeaders({ 'CF-IPCountry': 'US' })
  const otlpRequests: string[] = []
  await page.route(`${otelEndpoint}/**`, async route => {
    otlpRequests.push(route.request().url())
    await route.fulfill({ status: 200, contentType: 'application/x-protobuf', body: Buffer.alloc(0) })
  })

  await page.goto('/', { waitUntil: 'domcontentloaded' })

  await expect(page.getByRole('dialog', { name: 'Analytics settings' })).toBeHidden()
  expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).toBeNull()
  expect(otlpRequests).toEqual([])
})

test('deployed site enforces its Cloudflare-resolved regional policy', async ({ page }) => {
  test.skip(!isRemote, 'Production policy verification runs only through public Cloudflare.')
  if (expectedAnalyticsMode !== 'default-on' && expectedAnalyticsMode !== 'opt-in') {
    throw new Error('E2E_EXPECTED_ANALYTICS_MODE must be default-on or opt-in for deployed policy verification.')
  }
  await page.context().clearCookies()
  const otlpRequests: string[] = []
  const telemetryScripts: string[] = []
  page.on('request', request => {
    if (isTelemetryScriptRequest(request.url())) telemetryScripts.push(request.url())
  })
  await page.route(`${otelEndpoint}/**`, async route => {
    otlpRequests.push(route.request().url())
    await route.fulfill({ status: 200, contentType: 'application/x-protobuf', body: Buffer.alloc(0) })
  })

  await page.goto('/', { waitUntil: 'domcontentloaded' })

  const mode = await page.locator('#privacy-controls').getAttribute('data-analytics-mode')
  expect(mode).toBe(expectedAnalyticsMode)
  if (expectedAnalyticsMode === 'default-on') {
    await expect(page.getByRole('dialog', { name: 'Analytics settings' })).toBeHidden()
    await expect.poll(() => telemetryScripts.length).toBeGreaterThan(0)
    await expect.poll(() => page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).not.toBeNull()
    await expect.poll(() => otlpRequests.length).toBeGreaterThan(0)
  } else {
    await expect(page.getByRole('dialog', { name: 'Optional analytics' })).toBeVisible()
    expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).toBeNull()
    expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-traffic-attribution'))).toBeNull()
    expect(telemetryScripts).toEqual([])
    expect(otlpRequests).toEqual([])
  }
})

test('consent endpoint honors the trusted forwarded scheme', async ({ request }) => {
  const origin = new URL(siteBaseUrl)
  origin.protocol = 'https:'
  const response = await request.post('/privacy/consent', {
    headers: {
      'Content-Type': 'application/json',
      'Origin': origin.origin,
      'Sec-Fetch-Site': 'same-origin',
      'X-Forwarded-Proto': 'https',
    },
    data: { analytics: 'declined' },
  })

  expect(response.status()).toBe(204)
})

test('ignores a legacy local analytics choice and asks again', async ({ page }) => {
  test.skip(isRemote, 'The fallback policy requires a controlled local country header.')
  await page.context().clearCookies()
  await page.setExtraHTTPHeaders({ 'CF-IPCountry': 'DE' })
  await page.addInitScript(() => localStorage.setItem('analytics-consent', 'accepted'))
  const consentRequests: string[] = []
  const otlpRequests: string[] = []
  page.on('request', request => {
    if (request.url().endsWith('/privacy/consent')) consentRequests.push(request.url())
  })
  await page.route(`${otelEndpoint}/**`, async route => {
    otlpRequests.push(route.request().url())
    await route.fulfill({ status: 200, contentType: 'application/x-protobuf', body: Buffer.alloc(0) })
  })

  await page.goto('/articles/personal-infrastructure?utm_source=x&utm_medium=organic-social', { waitUntil: 'domcontentloaded' })
  await expect(page.getByRole('dialog', { name: 'Optional analytics' })).toBeVisible()
  await page.waitForTimeout(500)

  expect(consentRequests).toEqual([])
  expect(otlpRequests).toEqual([])
  expect((await page.context().cookies()).find(cookie => cookie.name === 'analytics-consent')).toBeUndefined()
})

test('invalid consent cookies fail closed and keep the controls usable', async ({ page }) => {
  test.skip(isRemote, 'The fallback policy requires a controlled local country header.')
  await page.setExtraHTTPHeaders({ 'CF-IPCountry': 'DE' })
  for (const value of ['%', 'v1.accepted.2025-01-01.0']) {
    await page.context().clearCookies()
    await page.context().addCookies([{ name: 'analytics-consent', value, url: siteBaseUrl, sameSite: 'Lax' }])
    await page.goto('/', { waitUntil: 'domcontentloaded' })

    await expect(page.getByRole('dialog', { name: 'Optional analytics' })).toBeVisible()
    const consentResponse = page.waitForResponse(response =>
      response.url().endsWith('/privacy/consent') && response.request().method() === 'POST',
    )
    await page.getByRole('button', { name: 'Accept analytics' }).click()
    expect((await consentResponse).status()).toBe(204)
  }
})

test('analytics choices keep mobile visual and keyboard order aligned', async ({ page }) => {
  test.skip(isRemote, 'The fallback policy requires a controlled local country header.')
  await page.context().clearCookies()
  await page.setExtraHTTPHeaders({ 'CF-IPCountry': 'DE' })
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/', { waitUntil: 'domcontentloaded' })

  const decline = page.getByRole('button', { name: 'Decline analytics' })
  const accept = page.getByRole('button', { name: 'Accept analytics' })
  const declineBox = await decline.boundingBox()
  const acceptBox = await accept.boundingBox()
  expect(declineBox?.y).toBeLessThan(acceptBox?.y ?? 0)
  expect(await decline.getAttribute('class')).toBe(await accept.getAttribute('class'))

  const settings = page.getByRole('button', { name: 'Analytics settings' })
  await settings.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('heading', { name: 'Optional analytics' })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(page.getByRole('link', { name: 'privacy page' })).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(decline).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(accept).toBeFocused()
})

test('browser telemetry starts only after analytics consent', async ({ page }) => {
  const advertisingRequests: string[] = []
  const telemetryScripts: string[] = []
  const otlpRequests: string[] = []
  const otlpBodies: Buffer[] = []
  page.on('request', request => {
    const hostname = new URL(request.url()).hostname
    if (isTelemetryScriptRequest(request.url())) telemetryScripts.push(request.url())
    if (
      hostname === 'www.googletagmanager.com'
      || hostname === 'google-analytics.com'
      || hostname.endsWith('.google-analytics.com')
      || hostname === 'snap.licdn.com'
    ) advertisingRequests.push(request.url())
  })
  await page.route(`${otelEndpoint}/**`, async route => {
    otlpRequests.push(route.request().url())
    const body = route.request().postDataBuffer()
    if (body) otlpBodies.push(body)
    await route.fulfill({ status: 200, contentType: 'application/x-protobuf', body: Buffer.alloc(0) })
  })
  await page.goto('/?utm_source=linkedin&utm_medium=organic-social&utm_campaign=personal-infrastructure&utm_content=post-01&email=private%40example.com', { waitUntil: 'domcontentloaded' })
  expect(otlpRequests).toEqual([])
  expect(telemetryScripts).toEqual([])
  expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).toBeNull()
  expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-traffic-attribution'))).toBeNull()

  await page.getByRole('link', { name: 'Personal Infrastructure', exact: true }).click()
  await expect(page).toHaveURL('/articles/personal-infrastructure')
  expect(otlpRequests).toEqual([])

  await page.getByRole('button', { name: 'Analytics settings' }).click()
  const consentResponse = page.waitForResponse(response =>
    response.url().endsWith('/privacy/consent') && response.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Accept analytics' }).click()
  expect((await consentResponse).status()).toBe(204)
  expect(await page.evaluate(() =>
    !('setAnalyticsConsent' in window) && !('loadOpenTelemetry' in window) && !('disableOpenTelemetry' in window),
  )).toBe(true)
  await expect.poll(async () =>
    (await page.context().cookies()).find(cookie => cookie.name === 'analytics-consent')?.value,
  ).toMatch(/^v1[.]accepted[.]2026-08-16[.]\d+$/)
  expect(await page.evaluate(() => localStorage.getItem('analytics-consent'))).toBeNull()

  await expect.poll(() => telemetryScripts.length).toBeGreaterThan(0)
  await expect.poll(() => otlpRequests.length).toBeGreaterThan(0)
  await expect.poll(() =>
    otlpBodies.some(body => body.includes(Buffer.from('com.meiermade.content.article_opened'))),
  ).toBe(true)
  expect(otlpBodies.some(body => body.includes(Buffer.from('com.meiermade.traffic.source')))).toBe(true)
  expect(otlpBodies.some(body => body.includes(Buffer.from('linkedin')))).toBe(true)
  expect(otlpBodies.some(body => body.includes(Buffer.from('personal-infrastructure')))).toBe(true)
  expect(otlpBodies.some(body => body.includes(Buffer.from('private@example.com')))).toBe(false)
  await page.waitForTimeout(1500)
  expect(otlpBodies.some(body => body.includes(Buffer.from('browser.web_vital')))).toBe(false)

  await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight))
  await expect.poll(() =>
    otlpBodies.some(body => body.includes(Buffer.from('com.meiermade.content.article_completed'))),
  ).toBe(true)
  expect(otlpRequests.every(url => url === `${otelEndpoint}/v1/logs` || url === `${otelEndpoint}/v1/traces`)).toBe(true)
  expect(advertisingRequests).toEqual([])
  const firstSessionId = await page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))
  expect(firstSessionId).not.toBeNull()

  otlpBodies.length = 0
  await page.goto('/articles/dev-env', { waitUntil: 'domcontentloaded' })
  await expect.poll(() =>
    otlpBodies.some(body => body.includes(Buffer.from('com.meiermade.content.article_opened'))),
  ).toBe(true)
  expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).toBe(firstSessionId)
  expect(otlpBodies.some(body => body.includes(Buffer.from('linkedin')))).toBe(true)
  expect(otlpBodies.some(body => body.includes(Buffer.from('personal-infrastructure')))).toBe(true)
  expect(otlpBodies.some(body => body.includes(Buffer.from('direct')))).toBe(false)

  otlpBodies.length = 0
  await page.reload({ waitUntil: 'domcontentloaded' })
  await expect.poll(() =>
    otlpBodies.some(body => body.includes(Buffer.from('browser.web_vital'))),
  ).toBe(true)

  await page.getByRole('button', { name: 'Analytics settings' }).click()
  const declineResponse = page.waitForResponse(response =>
    response.url().endsWith('/privacy/consent') && response.request().method() === 'POST',
  )
  await page.getByRole('button', { name: 'Decline analytics' }).click()
  expect((await declineResponse).status()).toBe(204)
  await expect.poll(async () =>
    (await page.context().cookies()).find(cookie => cookie.name === 'analytics-consent')?.value,
  ).toMatch(/^v1[.]declined[.]2026-08-16[.]\d+$/)
  await expect.poll(() => page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).toBeNull()

  otlpRequests.length = 0
  otlpBodies.length = 0
  await page.getByRole('link', { name: 'Articles', exact: true }).last().click()
  await expect(page).toHaveURL('/articles')
  await page.reload({ waitUntil: 'domcontentloaded' })
  await page.waitForTimeout(1500)

  expect(otlpRequests).toEqual([])
  expect(otlpBodies).toEqual([])
  expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).toBeNull()
  expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-traffic-attribution'))).toBeNull()
})

test('legacy company paths permanently redirect to Meier Made', async ({ request }) => {
  for (const path of ['/services', '/projects']) {
    const response = await request.get(path, { maxRedirects: 0 })
    expect([301, 308], `${path} permanent redirect status`).toContain(response.status())
    expect(response.headers().location).toBe(`https://meiermade.com${path}`)
  }
})

test('articles remain usable at a mobile viewport', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/articles', { waitUntil: 'domcontentloaded' })

  await expect(page.getByRole('heading', { name: 'Articles', exact: true })).toBeVisible()
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)

  for (const article of [
    { path: '/articles/personal-infrastructure', title: 'Personal Infrastructure' },
    { path: '/articles/dev-env', title: 'Development Environment' },
  ]) {
    await page.goto(article.path, { waitUntil: 'domcontentloaded' })
    await expect(page.getByRole('heading', { name: article.title, exact: true })).toBeVisible()
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)

    if (article.path === '/articles/personal-infrastructure') {
      for (const selector of ['[data-system-context]', '[data-container-view]', '[data-deployment-view]']) {
        const diagram = page.locator(selector)
        await expect(diagram.locator('svg')).toBeVisible()
        await expect(diagram.getByText('Scroll horizontally to see the complete diagram.')).toBeVisible()
        expect(await diagram.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true)
        expect(await diagram.evaluate(element => element.scrollLeft > 0)).toBe(true)
      }
    }
  }
})

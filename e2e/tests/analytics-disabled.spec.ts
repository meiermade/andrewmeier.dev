import { test, expect } from '@playwright/test'

for (const country of ['DE', 'US', 'XX']) {
  test(`disabled browser analytics stays off after acceptance in ${country}`, async ({ page }) => {
    await page.setExtraHTTPHeaders({ 'CF-IPCountry': country })
    const telemetryRequests: string[] = []
    page.on('request', request => {
      const url = new URL(request.url())
      if (/^\/scripts\/telemetry(?:\.[a-f0-9]+)?\.js$/.test(url.pathname)
        || /^\/v1\/(logs|traces|metrics)$/.test(url.pathname)) {
        telemetryRequests.push(request.url())
      }
    })
    // Any accidental export is observed but never sent to a real collector.
    await page.route('https://otel.test/**', route => route.fulfill({ status: 200, body: '' }))
    await page.goto('/', { waitUntil: 'networkidle' })
    const privacy = page.locator('#privacy-controls')
    await expect(privacy).toHaveAttribute('data-analytics-mode', country === 'US' ? 'default-on' : 'opt-in')
    expect(await privacy.getAttribute('data-otel-endpoint')).toBeNull()
    expect(await privacy.getAttribute('data-telemetry-src')).toBeNull()

    await page.getByRole('button', { name: 'Analytics settings' }).click()
    const accepted = page.waitForResponse(response => response.url().endsWith('/privacy/consent') && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Accept analytics' }).click()
    expect((await accepted).status()).toBe(204)
    await expect(page.locator('#cookie-consent-banner')).toBeHidden()
    expect((await page.context().cookies()).find(cookie => cookie.name === 'analytics-consent')?.value).toMatch(/^v1\.accepted\./)

    await page.getByRole('link', { name: 'Personal Infrastructure', exact: true }).click()
    await expect(page).toHaveURL('/articles/personal-infrastructure')
    await expect(page.getByRole('heading', { name: 'Personal Infrastructure', exact: true })).toBeVisible()
    await page.reload({ waitUntil: 'networkidle' })
    await expect(privacy).toBeAttached()
    expect(await privacy.getAttribute('data-otel-endpoint')).toBeNull()
    expect(await privacy.getAttribute('data-telemetry-src')).toBeNull()
    expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-session-id'))).toBeNull()
    expect(await page.evaluate(() => sessionStorage.getItem('opentelemetry-traffic-attribution'))).toBeNull()
    expect(telemetryRequests).toEqual([])
    const screenshot = test.info().outputPath(`disabled-after-acceptance-${country}.png`)
    await page.screenshot({ path: screenshot, fullPage: true })
    await test.info().attach('Disabled after acceptance', { path: screenshot, contentType: 'image/png' })

    await page.getByRole('button', { name: 'Analytics settings' }).click()
    const declined = page.waitForResponse(response => response.url().endsWith('/privacy/consent') && response.request().method() === 'POST')
    await page.getByRole('button', { name: 'Decline analytics' }).click()
    expect((await declined).status()).toBe(204)
    await expect(page.locator('#cookie-consent-banner')).toBeHidden()
    expect((await page.context().cookies()).find(cookie => cookie.name === 'analytics-consent')?.value).toMatch(/^v1\.declined\./)
    expect(telemetryRequests).toEqual([])
  })
}

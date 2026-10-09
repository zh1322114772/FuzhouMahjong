import { test, expect, devices } from '@playwright/test';

const profiles = [
  { label: 'iphone-13-landscape', device: devices['iPhone 13 landscape'] },
  { label: 'ipad-7-landscape', device: devices['iPad (gen 7) landscape'] }
];

for (const profile of profiles) {
  test(`${profile.label} keeps the hand and center discard control visible`, async ({ browser }, testInfo) => {
    const context = await browser.newContext({ ...profile.device, locale: 'zh-CN' });
    const page = await context.newPage();
    const browserErrors = [];
    page.on('pageerror', error => browserErrors.push(error.message));

    try {
      await page.goto('/');
      await page.locator('#display-name').fill(profile.label);
      await page.getByRole('button', { name: '创建房间' }).click();
      await expect(page).toHaveURL(/\/room\/[A-Z0-9]{6}$/);
      await expect(page.getByText('网络已连接')).toBeVisible();
      await page.getByRole('button', { name: '我准备好了' }).click();
      await expect(page.getByRole('button', { name: /开始/ })).toBeEnabled();
      await page.getByRole('button', { name: /开始/ }).click();

      await expect(page.locator('.table-grid')).toBeVisible();
      await expect(page.locator('.player-panel')).toHaveCount(4);
      await expect(page.locator('.position-bottom .hand')).toBeVisible();

      const layout = await page.evaluate(() => {
        const viewport = { width: innerWidth, height: innerHeight };
        const headerItems = ['.room-identity', '.connection-banner', '.header-actions']
          .map(selector => document.querySelector(selector).getBoundingClientRect());
        const hand = document.querySelector('.position-bottom .hand');
        const handRect = hand.getBoundingClientRect();
        const tileRects = [...hand.querySelectorAll('.mahjong-tile')].map(tile => tile.getBoundingClientRect());
        return {
          viewport,
          pageOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
          pageVerticalOverflow: document.documentElement.scrollHeight - document.documentElement.clientHeight,
          scrollTop: scrollY,
          headerSharesRow: Math.max(...headerItems.map(rect => rect.top)) < Math.min(...headerItems.map(rect => rect.bottom)),
          handOverflow: hand.scrollWidth - hand.clientWidth,
          handWithinViewport: handRect.left >= 0 && handRect.right <= viewport.width,
          tilesWithinHand: tileRects.every(rect => rect.left >= handRect.left - 1 && rect.right <= handRect.right + 1),
          smallestTileWidth: Math.min(...tileRects.map(rect => rect.width)),
          clippedPanels: [...document.querySelectorAll('.player-panel')]
            .filter(panel => panel.scrollHeight - panel.clientHeight > 1)
            .map(panel => ({ className: panel.className, clippedBy: panel.scrollHeight - panel.clientHeight }))
        };
      });

      expect(layout.pageOverflow, `${profile.label} must not overflow horizontally`).toBeLessThanOrEqual(1);
      expect(layout.pageVerticalOverflow, `${profile.label} landscape table should fit one screen`).toBeLessThanOrEqual(1);
      expect(layout.scrollTop, `${profile.label} should return to the top after starting`).toBeLessThanOrEqual(1);
      expect(layout.headerSharesRow, `${profile.label} header controls should remain on one compact row`).toBeTruthy();
      expect(layout.handOverflow, `${profile.label} should show the complete hand without swiping`).toBeLessThanOrEqual(1);
      expect(layout.handWithinViewport, `${profile.label} hand should stay inside the viewport`).toBeTruthy();
      expect(layout.tilesWithinHand, `${profile.label} every concealed tile should be visible`).toBeTruthy();
      expect(layout.smallestTileWidth, `${profile.label} tiles should remain large enough to identify`).toBeGreaterThanOrEqual(16);
      expect(layout.clippedPanels, `${profile.label} player panels must not clip tiles`).toEqual([]);

      const goldMarker = await page.locator('.gold-status .gold-tile').evaluate(tile =>
        getComputedStyle(tile, '::before').content);
      expect(goldMarker, 'gold tiles should use the outline without a covering badge').toBe('none');

      await page.screenshot({ path: testInfo.outputPath(`${profile.label}-initial.png`) });

      let playableTile = page.locator('.position-bottom .hand > .mahjong-tile:not(.gold-tile)').first();
      if (await playableTile.count() === 0)
        playableTile = page.locator('.position-bottom .hand .mahjong-tile:not(.gold-tile)').first();
      await playableTile.click();

      const discardButton = page.getByRole('button', { name: '打出', exact: true });
      await expect(discardButton).toBeEnabled();
      const controlLayout = await page.evaluate(() => {
        const table = document.querySelector('.table-grid').getBoundingClientRect();
        const ownPanel = document.querySelector('.position-bottom').getBoundingClientRect();
        const actionBar = document.querySelector('.action-bar').getBoundingClientRect();
        const button = document.querySelector('.action-bar .danger').getBoundingClientRect();
        return {
          buttonFullyVisible: button.top >= 0 && button.right <= innerWidth && button.bottom <= innerHeight && button.left >= 0,
          actionInsideTable: actionBar.left >= table.left && actionBar.right <= table.right && actionBar.top >= table.top && actionBar.bottom <= table.bottom,
          actionAboveHand: actionBar.bottom < ownPanel.top,
          relativeCenterY: ((actionBar.top + actionBar.bottom) / 2 - table.top) / table.height
        };
      });
      expect(controlLayout.buttonFullyVisible, `${profile.label} center discard button should be fully visible`).toBeTruthy();
      expect(controlLayout.actionInsideTable, `${profile.label} actions should stay inside the table`).toBeTruthy();
      expect(controlLayout.actionAboveHand, `${profile.label} actions must not cover the hand`).toBeTruthy();
      expect(controlLayout.relativeCenterY, `${profile.label} actions should remain near the table center`).toBeGreaterThan(0.35);
      expect(controlLayout.relativeCenterY, `${profile.label} actions should remain near the table center`).toBeLessThan(0.72);

      await page.screenshot({ path: testInfo.outputPath(`${profile.label}-selected.png`) });
      await discardButton.click();
      await expect(page.locator('.table-center.has-discard .mahjong-tile')).toBeVisible();
      expect(browserErrors).toEqual([]);
    } finally {
      await context.close();
    }
  });
}

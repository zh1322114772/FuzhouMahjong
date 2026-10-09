import { test, expect } from '@playwright/test';

const players = [
  { name: '东家', label: '1080p-125', viewport: { width: 1536, height: 864 }, scale: 1.25 },
  { name: '南家', label: '1080p-150', viewport: { width: 1280, height: 720 }, scale: 1.5 },
  { name: '西家', label: 'ultrawide-125', viewport: { width: 2752, height: 1152 }, scale: 1.25 },
  { name: '北家', label: 'ultrawide-150', viewport: { width: 2293, height: 960 }, scale: 1.5 }
];

test('four players can see and use the same responsive table', async ({ browser }, testInfo) => {
  const sessions = [];
  const browserErrors = [];

  for (const player of players) {
    const context = await browser.newContext({
      viewport: player.viewport,
      screen: player.viewport,
      deviceScaleFactor: player.scale
    });
    const page = await context.newPage();
    page.on('pageerror', error => browserErrors.push(`${player.name}: ${error.message}`));
    sessions.push({ ...player, context, page });
  }

  try {
    const host = sessions[0].page;
    await host.goto('/');
    await host.locator('#display-name').fill(players[0].name);
    await host.getByRole('button', { name: '创建房间' }).click();
    await expect(host).toHaveURL(/\/room\/[A-Z0-9]{6}$/);
    const roomCode = host.url().split('/').pop();

    for (const session of sessions.slice(1)) {
      await session.page.goto(`/join/${roomCode}`);
      await session.page.locator('#display-name').fill(session.name);
      await session.page.getByRole('button', { name: '加入这个房间' }).click();
      await expect(session.page).toHaveURL(new RegExp(`/room/${roomCode}$`));
    }

    for (const session of sessions) {
      await expect(session.page.getByText('网络已连接')).toBeVisible();
      await session.page.getByRole('button', { name: '我准备好了' }).click();
    }

    await expect(host.getByRole('button', { name: /开始/ })).toBeEnabled();
    await host.getByRole('button', { name: /开始/ }).click();

    for (const session of sessions) {
      await expect(session.page.locator('.table-grid')).toBeVisible();
      await expect(session.page.locator('.position-bottom .hand')).toBeVisible();
      await expect(session.page.locator('.gold-status')).toBeInViewport();
      await expect(session.page.locator('.player-panel')).toHaveCount(4);

      const toolbarAlignment = await session.page.evaluate(() => {
        const roomCode = document.querySelector('.room-identity').getBoundingClientRect();
        const connection = document.querySelector('.connection-banner').getBoundingClientRect();
        const actions = document.querySelector('.header-actions').getBoundingClientRect();
        return Math.max(roomCode.top, connection.top, actions.top) < Math.min(roomCode.bottom, connection.bottom, actions.bottom);
      });
      expect(toolbarAlignment, `${session.label} room details, connection, and actions should share one row`).toBeTruthy();

      const overflow = await session.page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow, `${session.name} must not have horizontal page overflow`).toBeLessThanOrEqual(1);
      const handOverflow = await session.page.locator('.position-bottom .hand').evaluate(element =>
        element.scrollWidth - element.clientWidth);
      expect(handOverflow, `${session.label} should show the complete hand without horizontal clipping`).toBeLessThanOrEqual(1);
    }

    await assertFourDirections(host);
    await expect(host.locator('.position-bottom .hand')).toBeInViewport();
    const ownPanelClipping = await host.locator('.position-bottom').evaluate(element =>
      element.scrollHeight - element.clientHeight);
    expect(ownPanelClipping, 'the local player panel must not clip its hand or public tiles').toBeLessThanOrEqual(1);

    await host.screenshot({
      path: testInfo.outputPath('desktop-initial.png'),
      fullPage: true
    });
    await sessions[1].page.screenshot({
      path: testInfo.outputPath('1080p-150-initial.png'),
      fullPage: true
    });
    await sessions[2].page.screenshot({
      path: testInfo.outputPath('ultrawide-125-initial.png'),
      fullPage: true
    });

    let sawClaimTimer = false;
    let sawDrawnTile = false;
    let usedDoubleClickDiscard = false;
    const capturedDrawnProfiles = new Set();
    let discards = 0;
    let idleChecks = 0;
    for (let step = 0; step < 240; step++) {
      if (await host.locator('.result-overlay').isVisible().catch(() => false)) break;

      const active = await findPageWithEnabledDiscard(sessions);
      if (!active) {
        const passers = [];
        for (const session of sessions) {
          const passButton = session.page.getByRole('button', { name: '过', exact: true });
          if (await passButton.count() > 0 && await passButton.isEnabled().catch(() => false)) {
            passers.push(session);
          }
        }

        if (passers.length === 0) {
          const handEnded = await sessions[0].page.locator('.result-overlay').isVisible().catch(() => false);
          if (handEnded) break;
          if (++idleChecks <= 20) {
            await sessions[0].page.waitForTimeout(150);
            step--;
            continue;
          }
          break;
        }
        idleChecks = 0;
        sawClaimTimer = true;
        for (const session of sessions) {
          await expect(session.page.locator('.claim-timer')).toBeVisible();
        }
        for (const passer of passers) {
          await passer.page.getByRole('button', { name: '过', exact: true }).click();
        }
        continue;
      }

      idleChecks = 0;
      const selfDrawnHu = active.page.getByRole('button', { name: '和牌', exact: true });
      if (discards >= 36 && await selfDrawnHu.count() > 0 && await selfDrawnHu.isEnabled().catch(() => false)) {
        await selfDrawnHu.click();
        break;
      }

      const drawnTile = active.page.locator('.position-bottom .drawn-tile');
      if (await drawnTile.count() > 0) {
        sawDrawnTile = true;
        await expect(drawnTile).toBeVisible();
        const isLast = await drawnTile.evaluate(element => element === element.parentElement?.lastElementChild);
        expect(isLast, 'the newly drawn tile must be the rightmost hand item').toBeTruthy();
        const gap = await active.page.locator('.position-bottom .hand').evaluate(hand => {
          const drawn = hand.querySelector('.drawn-tile');
          const previous = drawn?.previousElementSibling;
          if (!drawn || !previous) return 0;
          return drawn.getBoundingClientRect().left - previous.getBoundingClientRect().right;
        });
        expect(gap, 'the newly drawn tile needs a visible separation gap').toBeGreaterThan(6);
        const handOverflow = await active.page.locator('.position-bottom .hand').evaluate(element =>
          element.scrollWidth - element.clientWidth);
        expect(handOverflow, `${active.label} drawn hand should remain fully visible`).toBeLessThanOrEqual(1);
        if (!capturedDrawnProfiles.has(active.label)) {
          capturedDrawnProfiles.add(active.label);
          await active.page.screenshot({
            path: testInfo.outputPath(`${active.label}-drawn-tile.png`),
            fullPage: true
          });
        }
      }

      let playableTile = active.page.locator('.position-bottom .hand > .mahjong-tile:not(.gold-tile)').first();
      if (await playableTile.count() === 0)
        playableTile = active.page.locator('.position-bottom .hand .mahjong-tile:not(.gold-tile)').last();
      if (!usedDoubleClickDiscard) {
        await playableTile.dblclick();
        usedDoubleClickDiscard = true;
      } else {
        await playableTile.click();
        const discardButton = active.page.getByRole('button', { name: '打出', exact: true });
        await expect(discardButton).toBeEnabled();
        const discardButtonBounds = await discardButton.boundingBox();
        expect(discardButtonBounds, `${active.label} discard button should have a layout box`).toBeTruthy();
        expect(discardButtonBounds.y, `${active.label} discard button top should be visible`).toBeGreaterThanOrEqual(0);
        expect(discardButtonBounds.y + discardButtonBounds.height,
          `${active.label} discard button should be fully visible`).toBeLessThanOrEqual(active.viewport.height);
        await discardButton.click();
      }
      discards++;
      await expect(active.page.locator('.position-bottom .drawn-tile')).toHaveCount(0);

      for (const session of sessions) {
        await expect(session.page.locator('.table-center.has-discard .mahjong-tile')).toBeVisible();
      }

      const visibleTitles = await Promise.all(sessions.map(session =>
        session.page.locator('.table-center.has-discard .mahjong-tile').getAttribute('title')));
      expect(new Set(visibleTitles).size, 'all players should see the same last discard').toBe(1);
    }

    expect(discards, 'the browser test should play deeply enough to fill discard rivers').toBeGreaterThanOrEqual(36);
    expect(usedDoubleClickDiscard, 'double-clicking a tile should discard it directly').toBeTruthy();
    expect(sawDrawnTile, 'the browser test should observe the separate newly drawn tile').toBeTruthy();
    expect(capturedDrawnProfiles.size, 'every DPI profile should capture a newly drawn tile').toBe(players.length);

    for (const session of sessions) {
      await expect(session.page.locator('.result-overlay')).toBeVisible();
      const centerDelta = await session.page.evaluate(() => {
        const table = document.querySelector('.table-grid').getBoundingClientRect();
        const dialog = document.querySelector('.result-card').getBoundingClientRect();
        return {
          x: Math.abs((table.left + table.width / 2) - (dialog.left + dialog.width / 2)),
          y: Math.abs((table.top + table.height / 2) - (dialog.top + dialog.height / 2))
        };
      });
      expect(centerDelta.x, `${session.label} result dialog should be horizontally centered`).toBeLessThanOrEqual(2);
      expect(centerDelta.y, `${session.label} result dialog should be vertically centered`).toBeLessThanOrEqual(2);
      const headerActionsAlignment = await session.page.evaluate(() => {
        const header = document.querySelector('.room-header').getBoundingClientRect();
        const actions = document.querySelector('.header-actions').getBoundingClientRect();
        return (actions.left - header.left) / header.width;
      });
      expect(headerActionsAlignment, `${session.label} room actions should stay on the right`).toBeGreaterThan(0.7);
    }

    for (const session of sessions) {
      const clippedPanels = await session.page.locator('.player-panel').evaluateAll(panels => panels
        .filter(panel => panel.scrollHeight - panel.clientHeight > 1)
        .map(panel => ({ className: panel.className, clippedBy: panel.scrollHeight - panel.clientHeight })));
      expect(clippedPanels, `${session.label} player panels must not hide tiles`).toEqual([]);
    }

    for (const session of sessions) {
      await session.page.screenshot({ path: testInfo.outputPath(`${session.label}-after-play.png`), fullPage: true });
    }
    expect(browserErrors).toEqual([]);

    // Claims depend on the shuffled hand. When one appears, its timer is verified for every player.
    testInfo.annotations.push({ type: 'claim-timer-observed', description: String(sawClaimTimer) });
  } finally {
    await Promise.all(sessions.map(session => session.context.close()));
  }
});

async function findPageWithEnabledDiscard(sessions) {
  for (const session of sessions) {
    const button = session.page.getByRole('button', { name: '打出', exact: true });
    if (await button.count() > 0) return session;
  }
  return null;
}

async function assertFourDirections(page) {
  const table = await page.locator('.table-grid').boundingBox();
  const top = await page.locator('.position-top').boundingBox();
  const right = await page.locator('.position-right').boundingBox();
  const bottom = await page.locator('.position-bottom').boundingBox();
  const left = await page.locator('.position-left').boundingBox();
  expect(table && top && right && bottom && left).toBeTruthy();

  expect(top.y).toBeLessThan(left.y);
  expect(bottom.y).toBeGreaterThan(left.y);
  expect(left.x).toBeLessThan(top.x);
  expect(right.x).toBeGreaterThan(top.x);
}

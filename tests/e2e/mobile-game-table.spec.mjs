import { test, expect, devices } from '@playwright/test';

test('iPhone landscape completes five hands with all table tiles visible', async ({ browser }, testInfo) => {
  test.setTimeout(300_000);
  const context = await browser.newContext({ ...devices['iPhone 13 landscape'], locale: 'zh-CN' });
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', error => errors.push(error.message));

  try {
    await startSinglePlayerGame(page, '手机玩家');
    await assertMobileTable(page, 'iPhone initial');
    await page.screenshot({ path: testInfo.outputPath('iphone-hand-1-initial.png') });

    let handsCompleted = 0;
    while (handsCompleted < 5) {
      const handNumber = handsCompleted + 1;
      console.log(`starting simulated hand ${handNumber}`);
      const actionCount = await playUntilResult(page, handNumber, testInfo);
      handsCompleted++;
      console.log(`completed simulated hand ${handsCompleted} after ${actionCount} human actions`);
      await expect(page.locator('.result-overlay')).toBeVisible();
      await assertMobileTable(page, `iPhone result ${handsCompleted}`);

      if (handsCompleted === 1 || handsCompleted === 5) {
        await page.screenshot({ path: testInfo.outputPath(`iphone-hand-${handsCompleted}-result.png`) });
      }
      testInfo.annotations.push({ type: `hand-${handsCompleted}-actions`, description: String(actionCount) });

      if (handsCompleted < 5) {
        const continueButton = page.getByRole('button', { name: /开始下一局|再来一场/ });
        await expect(continueButton).toBeEnabled();
        await continueButton.click();
        await expect(page.locator('.result-overlay')).toHaveCount(0);
      }
    }

    expect(errors).toEqual([]);
  } finally {
    await context.close();
  }
});

test('iPad landscape keeps enlarged hand and lower action controls visible', async ({ browser }, testInfo) => {
  const context = await browser.newContext({ ...devices['iPad (gen 7) landscape'], locale: 'zh-CN' });
  const page = await context.newPage();
  try {
    await startSinglePlayerGame(page, '平板玩家');
    await assertMobileTable(page, 'iPad initial');

    const tile = page.locator('.position-bottom .hand .mahjong-tile:not(.gold-tile)').first();
    await tile.click();
    const discard = page.getByRole('button', { name: '打出', exact: true });
    await expect(discard).toBeEnabled();
    const positions = await page.evaluate(() => {
      const table = document.querySelector('.table-grid').getBoundingClientRect();
      const hand = document.querySelector('.position-bottom').getBoundingClientRect();
      const actions = document.querySelector('.action-bar').getBoundingClientRect();
      return {
        insideTable: actions.left >= table.left && actions.right <= table.right && actions.top >= table.top && actions.bottom <= table.bottom,
        aboveHand: actions.bottom <= hand.top + 2,
        visible: actions.top >= 0 && actions.bottom <= innerHeight
      };
    });
    expect(positions).toEqual({ insideTable: true, aboveHand: true, visible: true });
    await page.screenshot({ path: testInfo.outputPath('ipad-selected-action.png') });
  } finally {
    await context.close();
  }
});

async function startSinglePlayerGame(page, name) {
  await page.goto('/');
  await page.locator('#display-name').fill(name);
  await page.getByRole('button', { name: '创建房间' }).click();
  await expect(page).toHaveURL(/\/room\/\d{6}$/);
  await expect(page).toHaveTitle(/^\d{6}｜福州麻将$/);
  const ready = page.getByRole('button', { name: '我准备好了' });
  await expect(ready).toBeVisible();
  await ready.click();
  const start = page.getByRole('button', { name: /开始/ });
  await expect(start).toBeEnabled();
  await start.click();
  await expect(page.locator('.table-grid')).toBeVisible();
}

async function playUntilResult(page, handNumber, testInfo) {
  let actions = 0;
  let capturedMidHand = false;
  for (let step = 0; step < 400; step++) {
    if (await page.locator('.result-overlay').isVisible().catch(() => false)) return actions;
    if (step > 0 && step % 50 === 0) console.log(`hand ${handNumber}: step ${step}, ${actions} human actions`);
    if (!capturedMidHand && actions >= 5 && [1, 3, 5].includes(handNumber)) {
      await page.waitForTimeout(100);
      await page.screenshot({ path: testInfo.outputPath(`iphone-hand-${handNumber}-play-mid.png`) });
      capturedMidHand = true;
    }

    const hu = page.getByRole('button', { name: '和牌', exact: true });
    if (await hu.count() && await hu.isEnabled().catch(() => false)) {
      await clickReliably(hu);
      actions++;
      continue;
    }

    const pass = page.getByRole('button', { name: '过', exact: true });
    if (await pass.count() && await pass.isEnabled().catch(() => false)) {
      await clickReliably(pass);
      actions++;
      continue;
    }

    const tile = page.locator('.position-bottom .hand .mahjong-tile:not(.gold-tile):not([disabled])').first();
    if (await tile.count()) {
      await clickReliably(tile);
      const discard = page.getByRole('button', { name: '打出', exact: true });
      if (await discard.count() === 0) await clickReliably(tile);
      await expect(discard).toBeEnabled();
      await clickReliably(discard);
      actions++;
      if (actions === 10) await assertMobileTable(page, 'iPhone late hand');
      continue;
    }

    await page.waitForTimeout(75);
  }
  throw new Error('A hand did not finish within 400 browser actions.');
}

async function clickReliably(locator) {
  await locator.click({ timeout: 2_000 }).catch(() => locator.dispatchEvent('click'));
}

async function assertMobileTable(page, label) {
  await expect(page.locator('.player-panel')).toHaveCount(4);
  await expect(page.locator('.position-bottom .hand')).toBeVisible();
  const layout = await page.evaluate(() => {
    const table = document.querySelector('.table-grid').getBoundingClientRect();
    const hand = document.querySelector('.position-bottom .hand');
    const handRect = hand.getBoundingClientRect();
    const handTiles = [...hand.querySelectorAll('.mahjong-tile')].map(tile => tile.getBoundingClientRect());
    const publicTiles = [...document.querySelectorAll('.public-tiles .mahjong-tile')];
    const hiddenPublicTiles = publicTiles.map(tile => {
      const rect = tile.getBoundingClientRect();
      return { title: tile.title, panel: tile.closest('.player-panel').className, left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom };
    }).filter(tile => tile.left < table.left - 1 || tile.right > table.right + 1 || tile.top < table.top - 1 || tile.bottom > table.bottom + 1);
    const misplacedCounts = [...document.querySelectorAll('.tile-count')].filter(count => {
      const countRect = count.getBoundingClientRect();
      const tileRect = count.closest('.tile-group').querySelector('.mahjong-tile').getBoundingClientRect();
      const overlap = tileRect.bottom - countRect.top;
      return overlap <= 0
        || overlap > countRect.height * .75
        || countRect.width > 18
        || countRect.height > 18;
    }).length;
    const bubbleStretchedGroups = [...document.querySelectorAll('.public-group')].filter(group => {
      if (!group.querySelector('.tile-count')) return false;
      const groupRect = group.getBoundingClientRect();
      const tiles = [...group.querySelectorAll('.mahjong-tile')].map(tile => tile.getBoundingClientRect());
      return groupRect.bottom - Math.max(...tiles.map(tile => tile.bottom)) > 1;
    }).length;
    const hiddenCountBubbles = [...document.querySelectorAll('.tile-count')].filter(count => {
      const rect = count.getBoundingClientRect();
      return rect.left < table.left - 1 || rect.right > table.right + 1 || rect.top < table.top - 1 || rect.bottom > table.bottom + 1;
    }).length;
    return {
      horizontalOverflow: document.documentElement.scrollWidth - innerWidth,
      verticalOverflow: document.documentElement.scrollHeight - innerHeight,
      tableVisible: table.top >= 0 && table.left >= 0 && table.right <= innerWidth && table.bottom <= innerHeight + 1,
      handOverflow: hand.scrollWidth - hand.clientWidth,
      handTilesVisible: handTiles.every(rect => rect.left >= handRect.left - 1 && rect.right <= handRect.right + 1 && rect.bottom <= innerHeight + 1),
      tileWidth: Math.min(...handTiles.map(rect => rect.width)),
      hiddenPublicTiles,
      misplacedCounts,
      bubbleStretchedGroups,
      hiddenCountBubbles
    };
  });
  expect(layout.horizontalOverflow, `${label}: no horizontal page overflow`).toBeLessThanOrEqual(1);
  expect(layout.verticalOverflow, `${label}: landscape view fits one screen`).toBeLessThanOrEqual(1);
  expect(layout.tableVisible, `${label}: whole table is visible`).toBeTruthy();
  expect(layout.handOverflow, `${label}: complete hand is visible`).toBeLessThanOrEqual(1);
  expect(layout.handTilesVisible, `${label}: every concealed tile is visible`).toBeTruthy();
  expect(layout.tileWidth, `${label}: concealed tiles remain readable`).toBeGreaterThanOrEqual(30);
  expect(layout.hiddenPublicTiles, `${label}: public tiles are not clipped`).toEqual([]);
  expect(layout.misplacedCounts, `${label}: count bubbles stay small and overlap only the tile edge`).toBe(0);
  expect(layout.bubbleStretchedGroups, `${label}: count bubbles do not lengthen group separators`).toBe(0);
  expect(layout.hiddenCountBubbles, `${label}: count bubbles remain fully visible`).toBe(0);
}

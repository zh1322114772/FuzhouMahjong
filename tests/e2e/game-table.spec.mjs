import { test, expect } from '@playwright/test';

test('four desktop players share a readable responsive table', async ({ browser }, testInfo) => {
  const profiles = [
    { name: '东家', viewport: { width: 1536, height: 864 } },
    { name: '南家', viewport: { width: 1280, height: 720 } },
    { name: '西家', viewport: { width: 2752, height: 1152 } },
    { name: '北家', viewport: { width: 2293, height: 960 } }
  ];
  const sessions = [];
  const errors = [];

  try {
    for (const profile of profiles) {
      const context = await browser.newContext({ viewport: profile.viewport });
      const page = await context.newPage();
      page.on('pageerror', error => errors.push(`${profile.name}: ${error.message}`));
      sessions.push({ ...profile, context, page });
    }

    const host = sessions[0].page;
    await host.goto('/');
    await host.locator('#display-name').fill('东家');
    await host.getByRole('button', { name: '创建房间' }).click();
    await expect(host).toHaveURL(/\/room\/\d{6}$/);
    const roomCode = host.url().split('/').pop();

    for (const session of sessions.slice(1)) {
      await session.page.goto(`/join/${roomCode}`);
      await session.page.locator('#display-name').fill(session.name);
      await session.page.getByRole('button', { name: '加入这个房间' }).click();
      await expect(session.page).toHaveURL(new RegExp(`/room/${roomCode}$`));
    }

    for (const session of sessions) {
      const ready = session.page.getByRole('button', { name: '我准备好了' });
      await expect(ready).toBeVisible();
      await ready.click();
    }
    await expect(host.getByRole('button', { name: /开始/ })).toBeEnabled();
    await host.getByRole('button', { name: /开始/ }).click();

    for (const session of sessions) {
      await expect(session.page.locator('.table-grid')).toBeVisible();
      await expect(session.page.locator('.player-panel')).toHaveCount(4);
      await expect(session.page.locator('.position-bottom .hand')).toBeVisible();
      await expect(session.page.locator('.gold-status')).toBeVisible();
      await expect(session.page.locator('.action-timer')).toHaveText(/\d+/);
      expect(await session.page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(1);
      expect(await session.page.locator('.position-bottom .hand').evaluate(hand => hand.scrollWidth - hand.clientWidth)).toBeLessThanOrEqual(1);
      const topCenterDelta = await session.page.evaluate(() => {
        const table = document.querySelector('.table-grid').getBoundingClientRect();
        const top = document.querySelector('.position-top').getBoundingClientRect();
        return Math.abs((table.left + table.width / 2) - (top.left + top.width / 2));
      });
      expect(topCenterDelta, `${session.name} top player should align with table center`).toBeLessThanOrEqual(3);
    }

    await host.getByRole('button', { name: '分享房间' }).click();
    await expect(host.getByRole('dialog', { name: '分享房间' })).toBeVisible();
    await expect(host.locator('.share-dialog .room-code')).toHaveText(/^\d{6}$/);
    await host.getByRole('button', { name: '关闭' }).click();

    const tile = host.locator('.position-bottom .hand .mahjong-tile:not(.gold-tile)').first();
    await tile.dblclick();
    await expect(host.locator('.table-center.has-discard .mahjong-tile')).toBeVisible();
    await host.screenshot({ path: testInfo.outputPath('desktop-table.png'), fullPage: true });
    expect(errors).toEqual([]);
  } finally {
    await Promise.all(sessions.map(session => session.context.close()));
  }
});

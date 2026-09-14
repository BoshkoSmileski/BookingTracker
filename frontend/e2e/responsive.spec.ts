import type { Page } from '@playwright/test';
import { bookViaApi } from './support/api';
import { expect, signIn, test } from './support/fixtures';
import { fillGuestDetails, pickFirstSlot } from './support/wizard';

/**
 * A handful of viewport checks, deliberately not a visual regression suite.
 *
 * No screenshot baselines: they would need maintaining on every design change
 * and would fail for reasons that are not defects. What is asserted instead is
 * the thing that is actually broken when a layout breaks - the document is wider
 * than the window, or a control the screen exists for cannot be reached.
 *
 * jsdom applies no CSS at all, so the frontend suite cannot answer either
 * question. This is the one place in the project where geometry is measurable.
 */

const PHONE = { width: 375, height: 812 };
const DESKTOP = { width: 1440, height: 900 };

/**
 * The two widths the layout is actually held to: a small iPhone and the
 * modern-default one. 390 is not a rounding of 375 - the extra 15px is enough
 * for a flex row to stop wrapping, which changes which rule is under test.
 */
const PHONE_WIDTHS = [375, 390];

/**
 * The one geometric claim worth making everywhere: nothing scrolls sideways. A
 * couple of pixels of slack for sub-pixel layout rounding, which is not overflow
 * anybody can see or scroll to.
 *
 * **`<main>` has to be measured as well as the document, and that is the whole
 * point of this helper.** The organizer shell's content pane is `overflow-y-auto`
 * so that only it scrolls and the rail stays put - and CSS computes
 * `overflow-x: visible` to `auto` whenever the other axis is not visible. The
 * pane is therefore itself a horizontal scroll container, and it *absorbs* any
 * overflow inside it: `document.documentElement.scrollWidth` never grows, no
 * matter how far a dashboard screen bleeds.
 *
 * Measured rather than reasoned about: with the document check alone, an
 * analytics screen scrolling 86px sideways at 375px reported zero overflow.
 */
/** The same claim at both phone widths, for whatever is currently on screen. */
async function expectFitsOnAPhone(page: Page) {
  for (const width of PHONE_WIDTHS) {
    await page.setViewportSize({ width, height: PHONE.height });
    await expectNoHorizontalOverflow(page);
  }
  await page.setViewportSize(PHONE);
}

async function expectNoHorizontalOverflow(page: Page) {
  const overflow = await page.evaluate(() => {
    const doc = document.documentElement;
    const main = document.querySelector('main');
    return Math.max(
      doc.scrollWidth - doc.clientWidth,
      main ? main.scrollWidth - main.clientWidth : 0,
    );
  });
  expect(overflow).toBeLessThanOrEqual(1);
}

test.describe('at 375px', () => {
  test.use({ viewport: PHONE });

  test('the booking wizard is usable end to end', async ({ page, bookingPage }) => {
    await page.goto(`/book/${bookingPage.slug}`);
    await expect(page.getByRole('heading', { level: 1, name: bookingPage.title })).toBeVisible();
    await expectNoHorizontalOverflow(page);

    // The calendar and the time list stack rather than being cut off, and both
    // are genuinely operable at this width.
    await pickFirstSlot(page);
    await expectNoHorizontalOverflow(page);

    await fillGuestDetails(page, { name: 'Mobile Guest', email: 'mobile@guest.test' });
    await page.getByRole('button', { name: 'Continue' }).click();
    await expect(page.getByRole('heading', { name: 'Check and confirm' })).toBeVisible();
    await expectNoHorizontalOverflow(page);

    await page.getByRole('button', { name: 'Confirm booking' }).click();
    await expect(page.getByRole('heading', { name: 'Booking confirmed' })).toBeVisible();
    await expectNoHorizontalOverflow(page);
  });

  test('sign-in fits, and the dashboard navigation is behind a drawer', async ({
    page,
    organizer,
    bookingPage,
  }) => {
    await page.goto('/login');
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
    await expectNoHorizontalOverflow(page);

    await signIn(page, organizer);
    await page.goto('/dashboard');
    // The booking page card, not merely the route: this assertion measures the
    // shell's scroll pane too, and measuring it mid-load is measuring a
    // skeleton. Rule #69 - wait for the content that could actually be wide.
    //
    // The card is also *why* this needed fixing. Its `<li>` had no `min-w-0`, so
    // the single-column grid track was sized by the `truncate`d page title's
    // min-content and the workspace scrolled sideways by however far that title
    // overran - a real defect that read as an intermittent one, because the
    // suite's titles are generated and vary in length run to run.
    await expect(page.getByRole('main').getByRole('link', { name: bookingPage.title })).toBeVisible();
    await expectFitsOnAPhone(page);

    // Below `lg` the rail is `hidden lg:flex` - genuinely absent, not merely
    // moved off-screen, so it is out of the tab order too.
    await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden();

    await page.getByRole('button', { name: 'Open navigation' }).click();
    const rail = page.getByRole('navigation', { name: 'Main' });
    await expect(rail).toBeVisible();
    // Scoped: the workspace grid behind the drawer lists the same page by name.
    await expect(rail.getByRole('link', { name: bookingPage.title })).toBeVisible();

    // Escape is the expected way out of an overlay.
    await page.keyboard.press('Escape');
    await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden();
  });

  /**
   * The four organizer screens with the most horizontal content, in one pass.
   *
   * One test rather than four because the claim is identical on each and the
   * setup is what costs - and because the failure this guards is not a property
   * of any one screen but of how three separate CSS rules interact with a
   * `min-width: auto` default. Each of these bled at 375px, each for its own
   * reason:
   *
   *  - **Working hours** - two ~125px time inputs plus a remove button in a row
   *    285px wide, so the button sat on the card's border.
   *  - **Analytics** - a single-column `grid` track is sized `auto`, whose
   *    minimum is its item's content-based minimum, so one long activity
   *    description took the whole screen 86px wider than the phone.
   *  - **Session detail** - a tracked `Message` holding a URL has no break
   *    opportunity, so the timeline's flex item could not shrink below it: 124px.
   *  - **Date exceptions** - the widest form on the workspace, and the control
   *    of last resort if any of the above regresses through shared styling.
   */
  test('no organizer screen scrolls sideways', async ({
    page,
    request,
    organizer,
    bookingPage,
  }) => {
    await bookViaApi(request, bookingPage.slug, {
      name: 'Bartholomew Featherstonehaugh-Wentworth',
      email: 'bartholomew.featherstonehaugh@a-very-long-corporate-domain.example.com',
      // No spaces to break on, which is the entire point.
      message: 'See https://intranet.example.com/very/long/path/to/a/document?with=query&parameters=that&do=not#break',
    });
    await signIn(page, organizer);

    await page.goto(`/dashboard/${bookingPage.id}/settings/hours`);
    // The heading renders over the skeleton, so it is not the loaded state.
    // Save schedule only exists once the week itself is on screen.
    await expect(page.getByRole('button', { name: 'Save schedule' })).toBeVisible();
    await expectFitsOnAPhone(page);

    // Working hours needs its own assertion as well as the page-level one: the
    // interval row overflowed its *card*, not the viewport, so `<main>` reported
    // nothing while the remove button sat on the border with no padding left.
    // The card's own padding is read from the DOM rather than hardcoded, so this
    // stays true if that padding is ever retuned.
    const roomLeftInTheCard = await page.evaluate(() => {
      const row = document.querySelector('main ul > li') as HTMLElement;
      const button = row.querySelector('button[aria-label^="Remove"]') as HTMLElement;
      const contentRight =
        row.getBoundingClientRect().right - parseFloat(getComputedStyle(row).paddingRight);
      return contentRight - button.getBoundingClientRect().right;
    });
    expect(roomLeftInTheCard).toBeGreaterThanOrEqual(0);

    await page.goto('/dashboard/analytics');
    await expect(page.getByRole('heading', { level: 1, name: 'Analytics' })).toBeVisible();
    // Wait for the *activity feed*, not merely for the page: it is the last
    // panel to resolve and the one whose long descriptions used to widen the
    // grid track. Measuring on the heading alone reported zero overflow while
    // the screen was still 84px too wide a moment later - which is exactly how
    // the first version of this test passed against the unfixed code.
    // Scoped to a list item on purpose: the page title is also an `<option>` in
    // the filter select above, which is never visible.
    await expect(
      page.getByRole('main').getByRole('listitem').filter({ hasText: bookingPage.title }).first(),
    ).toBeVisible();
    await expectFitsOnAPhone(page);

    await page.goto(`/dashboard/${bookingPage.id}/settings/date-exceptions`);
    await expect(page.getByRole('heading', { level: 1, name: 'Date exceptions' })).toBeVisible();
    await expectFitsOnAPhone(page);

    await page.goto(`/dashboard/${bookingPage.id}?status=Submitted`);
    await page.getByRole('main').getByRole('link', { name: /Bartholomew/ }).click();
    // Same again: the URL has to be *rendered* before its width can be wrong.
    await expect(page.getByRole('main').getByText(/intranet\.example\.com/)).toBeVisible();
    await expectFitsOnAPhone(page);
  });

  /**
   * The drawer and the unsaved-changes guard, which used to disagree.
   *
   * `useUnsavedChanges` intercepts link clicks in the capture phase at the
   * document. It also called `stopPropagation`, which swallowed the click
   * entirely - including the `onClick` the shell puts on every rail link to
   * close this drawer. So below `lg` a held navigation left the drawer open
   * over the very panel asking the question, and the tap looked like a link
   * that had done nothing.
   *
   * Only measurable here: the drawer exists only below `lg`, which needs CSS,
   * and the dirty state needs a page that has really loaded its own data.
   */
  test('a drawer link is held by unsaved changes, and closes the drawer to ask', async ({
    page,
    organizer,
    bookingPage,
  }) => {
    await signIn(page, organizer);
    const hours = `/dashboard/${bookingPage.id}/settings/hours`;
    await page.goto(hours);
    await expect(page.getByRole('heading', { level: 1, name: 'Working hours' })).toBeVisible();

    const openDrawer = async () => {
      await page.getByRole('button', { name: 'Open navigation' }).click();
      await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();
    };
    const drawerLink = (name: string) =>
      page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name });

    // Clean: the link navigates and the drawer closes behind it.
    await openDrawer();
    await drawerLink('Notifications').click();
    await expect(page).toHaveURL(/settings\/notifications$/);
    await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden();

    // Dirty: the same link is held, and the drawer gets out of the way so the
    // question is actually on screen.
    await page.goto(hours);
    await expect(page.getByRole('heading', { level: 1, name: 'Working hours' })).toBeVisible();
    await page.getByRole('textbox', { name: 'Time zone' }).fill('Europe/Berlin');

    await openDrawer();
    await drawerLink('Notifications').click();
    await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden();
    await expect(page.getByRole('button', { name: 'Stay on this page' })).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`${bookingPage.id}/settings/hours$`));

    // Staying keeps both the page and the edit.
    await page.getByRole('button', { name: 'Stay on this page' }).click();
    await expect(page).toHaveURL(new RegExp(`${bookingPage.id}/settings/hours$`));
    await expect(page.getByRole('textbox', { name: 'Time zone' })).toHaveValue('Europe/Berlin');

    // Leaving discards it and goes where the link pointed.
    await openDrawer();
    await drawerLink('Notifications').click();
    await page.getByRole('button', { name: 'Leave without saving' }).click();
    await expect(page).toHaveURL(/settings\/notifications$/);
  });

  test('a session row still says what was booked and what state it is in', async ({
    page,
    request,
    organizer,
    bookingPage,
  }) => {
    await bookViaApi(request, bookingPage.slug, {
      name: 'Mobile Booker',
      email: 'booker@guest.test',
    });

    await signIn(page, organizer);
    await page.goto(`/dashboard/${bookingPage.id}?status=Submitted`);

    const row = page.getByRole('main').getByRole('link', { name: /Mobile Booker/ });
    await expect(row).toBeVisible();
    // The row reflows rather than dropping cells: the appointment and the status
    // used to be hidden below `sm`, leaving a phone showing a name and an email
    // and nothing about the booking.
    await expect(row).toContainText('Submitted');
    await expectNoHorizontalOverflow(page);
  });
});

test.describe('at 1440px', () => {
  test.use({ viewport: DESKTOP });

  test('the organizer shell shows the permanent rail beside the content', async ({
    page,
    organizer,
    bookingPage,
  }) => {
    await signIn(page, organizer);
    await page.goto('/dashboard');

    await expect(page.getByRole('navigation', { name: 'Main' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Open navigation' })).toBeHidden();
    await expect(page.getByRole('main').getByRole('link', { name: bookingPage.title })).toBeVisible();
    await expectNoHorizontalOverflow(page);

    // Only the content pane scrolls - the rail does not slide away as you read,
    // which is what distinguishes an application window from a long web page.
    const bodyScrolls = await page.evaluate(
      () => document.documentElement.scrollHeight > document.documentElement.clientHeight,
    );
    expect(bodyScrolls).toBe(false);
  });
});

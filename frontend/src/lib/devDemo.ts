/**
 * The seeded demo account, and the rule for keeping it out of production.
 *
 * `DevelopmentSeeder` creates this organizer whenever the API starts in
 * Development, so filling it in is a genuine convenience for whoever is running
 * the app locally. It is also a working credential, and it was previously
 * printed as plain text on `/login` and on the landing page in **every** build.
 *
 * **Guard the call site with a bare `import.meta.env.DEV`, never with a
 * helper.** Vite replaces that expression textually at build time and Rollup
 * then eliminates the branch, taking these constants with it - which is the
 * difference between not *showing* a credential and not *shipping* one. An
 * `isDevBuild()` wrapper looks equivalent and is not: the call is opaque to the
 * bundler, the branch survives, and the password stays in `dist`. That was
 * tried here first and caught by `grep Passw0rd dist/assets/*.js`, which is
 * also the check to run if this is ever touched again.
 */

/** Mirrors `DevelopmentSeeder.DemoOrganizerEmail`/`DemoOrganizerPassword`. */
export const DEMO_ORGANIZER = {
  email: 'organizer@example.com',
  password: 'Passw0rd!',
} as const;

/** Mirrors `DevelopmentSeeder.DemoBookingPageSlug`. Public, and safe in any build. */
export const DEMO_BOOKING_PAGE_SLUG = 'demo-30-min-meeting';

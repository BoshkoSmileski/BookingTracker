import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { AuthShell } from '../components/AuthShell';
import { BUTTON_PRIMARY, Field, FOCUS_RING, InlineNotice, INPUT } from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { DEFAULT_SIGNED_IN_PATH, safeReturnTo } from '../lib/returnTo';
import { DEMO_ORGANIZER } from '../lib/devDemo';
import { useDocumentTitle } from '../lib/pageTitle';

export function LoginPage() {
  useDocumentTitle('Sign in');
  const { login, authError } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  // Where ProtectedRoute was headed before it turned the organizer away, if it
  // passed anything usable. `safeReturnTo` is what stops this being an open
  // redirect; an ordinary sign-in supplies no state and gets the default.
  const returnTo = safeReturnTo((location.state as { from?: unknown } | null)?.from);

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      await login(email, password);
      // `replace` so Back does not land on the login form the organizer has
      // just completed - which would bounce them straight forward again.
      navigate(returnTo ?? DEFAULT_SIGNED_IN_PATH, { replace: true });
    } catch {
      // authError from context is already surfaced below
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <AuthShell
      title="Sign in"
      description="Manage your booking pages, availability and bookings."
      footer={
        <>
          No account?{' '}
          <Link to="/register" className={`rounded font-medium text-accent-700 hover:underline ${FOCUS_RING}`}>
            Create one
          </Link>
        </>
      }
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        <Field label="Email">
          {(control) => (
            <input
              {...control}
              type="email"
              autoComplete="username"
              className={`${INPUT} w-full`}
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
            />
          )}
        </Field>
        <Field label="Password">
          {(control) => (
            <input
              {...control}
              type="password"
              autoComplete="current-password"
              className={`${INPUT} w-full`}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          )}
        </Field>

        {authError && <InlineNotice tone="error">{authError}</InlineNotice>}

        <button type="submit" disabled={submitting} className={`${BUTTON_PRIMARY} w-full`}>
          {submitting ? 'Signing in…' : 'Sign in'}
        </button>
      </form>

      {/*
        The seeded demo account, in development only.

        It used to be printed on this page as plain text - "demo login:
        organizer@example.com / Passw0rd!" - in every build, which published a
        working credential for anyone who opened the deployed app. The
        convenience is real for whoever is running this locally, so it survives
        as a button rather than as text: `import.meta.env.DEV` is statically
        replaced at build time, so this whole block is removed from a
        production bundle rather than merely hidden by it. A helper function here
        would NOT work - see lib/devDemo.ts.
      */}
      {import.meta.env.DEV && (
        <div className="mt-6 border-t border-gray-200 pt-4">
          <button
            type="button"
            onClick={() => {
              setEmail(DEMO_ORGANIZER.email);
              setPassword(DEMO_ORGANIZER.password);
            }}
            className={`rounded text-[13px] font-medium text-accent-700 hover:underline ${FOCUS_RING}`}
          >
            Fill demo credentials
          </button>
          <p className="mt-1 text-[13px] text-gray-500">
            Development build only — the account <code>DevelopmentSeeder</code> creates.
          </p>
        </div>
      )}
    </AuthShell>
  );
}

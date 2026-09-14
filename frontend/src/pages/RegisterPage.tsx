import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AuthShell } from '../components/AuthShell';
import { BUTTON_PRIMARY, Field, FOCUS_RING, InlineNotice, INPUT } from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { DEFAULT_SIGNED_IN_PATH } from '../lib/returnTo';
import { useDocumentTitle } from '../lib/pageTitle';

export function RegisterPage() {
  useDocumentTitle('Create account');
  const { register, authError } = useAuth();
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    try {
      await register(name, email, password);
      // Navigation is part of completing this action, not a substitute for
      // confirming it: registering has no "stay here" state to return to.
      navigate(DEFAULT_SIGNED_IN_PATH, { replace: true });
    } catch {
      // authError from context is already surfaced below
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <AuthShell
      title="Create an account"
      description="Set up your first booking page in a couple of minutes."
      footer={
        <>
          Already have an account?{' '}
          <Link to="/login" className={`rounded font-medium text-accent-700 hover:underline ${FOCUS_RING}`}>
            Sign in
          </Link>
        </>
      }
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        <Field label="Name">
          {(control) => (
            <input
              {...control}
              autoComplete="name"
              className={`${INPUT} w-full`}
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
            />
          )}
        </Field>
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
        {/* The hint was hand-wired and sat on `mt-1`, one of the three sites
            `HELP_GAP` was extracted to settle. */}
        <Field label="Password" hint="At least 8 characters.">
          {(control) => (
            <input
              {...control}
              type="password"
              autoComplete="new-password"
              className={`${INPUT} w-full`}
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              minLength={8}
              required
            />
          )}
        </Field>

        {authError && <InlineNotice tone="error">{authError}</InlineNotice>}

        <button type="submit" disabled={submitting} className={`${BUTTON_PRIMARY} w-full`}>
          {submitting ? 'Creating account…' : 'Create account'}
        </button>
      </form>
    </AuthShell>
  );
}

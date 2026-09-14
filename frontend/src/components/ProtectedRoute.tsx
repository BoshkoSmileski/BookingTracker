import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../contexts/AuthContext';
import { SkeletonLines } from './ui';

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated, isInitializing } = useAuth();
  const location = useLocation();

  if (isInitializing) {
    return (
      <div className="mx-auto w-full max-w-[1440px] px-6 py-8 lg:px-10 lg:py-10">
        <SkeletonLines lines={4} />
      </div>
    );
  }

  // The address that was refused travels with the redirect, so LoginPage can
  // return the organizer to it. Router state rather than a query parameter:
  // the destination never appears in the URL bar, so it cannot be handed to
  // someone else as a crafted link - and `location` already carries the search
  // string and hash, which a hand-built `?returnTo=` would have to re-encode.
  if (!isAuthenticated) return <Navigate to="/login" state={{ from: location }} replace />;

  return <>{children}</>;
}

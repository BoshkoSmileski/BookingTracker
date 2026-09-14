import { Navigate, Route, Routes } from 'react-router-dom';
import { DashboardLayout } from './components/DashboardLayout';
import { ProtectedRoute } from './components/ProtectedRoute';
import { AnalyticsPage } from './pages/AnalyticsPage';
import { BookingInstructionsPage } from './pages/BookingInstructionsPage';
import { BookingLimitsPage } from './pages/BookingLimitsPage';
import { MeetingSettingsPage } from './pages/MeetingSettingsPage';
import { BookingFormPage } from './pages/BookingFormPage';
import { BookingPage } from './pages/BookingPage';
import { BookingPageDetailsPage } from './pages/BookingPageDetailsPage';
import { CalendarIntegrationPage } from './pages/CalendarIntegrationPage';
import { CancelBookingPage } from './pages/CancelBookingPage';
import { CreateBookingPagePage } from './pages/CreateBookingPagePage';
import { DashboardHomePage } from './pages/DashboardHomePage';
import { DateExceptionsPage } from './pages/DateExceptionsPage';
import { HomePage } from './pages/HomePage';
import { LoginPage } from './pages/LoginPage';
import { ManageBookingPage } from './pages/ManageBookingPage';
import { DashboardNotFoundPage, NotFoundPage } from './pages/NotFoundPage';
import { NotificationSettingsPage } from './pages/NotificationSettingsPage';
import { OrganizerDashboardPage } from './pages/OrganizerDashboardPage';
import { RegisterPage } from './pages/RegisterPage';
import { RescheduleBookingPage } from './pages/RescheduleBookingPage';
import { SchedulingSettingsPage } from './pages/SchedulingSettingsPage';
import { SessionDetailPage } from './pages/SessionDetailPage';
import { WorkingHoursPage } from './pages/WorkingHoursPage';

function App() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="/book/:slug" element={<BookingPage />} />
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />

      {/* Public - PublicToken in the URL is the sole credential, no auth. */}
      <Route path="/manage/:token" element={<ManageBookingPage />} />
      <Route path="/manage/:token/cancel" element={<CancelBookingPage />} />
      <Route path="/manage/:token/reschedule" element={<RescheduleBookingPage />} />

      {/* The screen was called "questions" until it was renamed to match what it
          actually does; keep any bookmark working rather than 404ing it. Outside
          the layout below so an unauthenticated visitor lands on the renamed
          route (and then /login) rather than on /login with the redirect lost.

          `relative="path"` on all three is load-bearing, not decoration. These
          are flat routes with no parent, so React Router's default resolves
          `..` against the route HIERARCHY rather than the URL - which put every
          one of them on `/date-exceptions` and `/instructions`, paths that do
          not exist. The redirects had never actually worked; with no catch-all
          they simply rendered a blank page, which is why it went unnoticed. */}
      <Route
        path="/dashboard/:pageId/settings/questions"
        element={<Navigate to="../instructions" replace relative="path" />}
      />

      {/* "Blocked dates" and "Date overrides" were two screens for one question -
          what is different about this date - and are now one. Both old routes are
          real bookmarks an organizer may hold, and both are outside the layout
          below for the same reason `questions` is. */}
      <Route
        path="/dashboard/:pageId/settings/blocked-dates"
        element={<Navigate to="../date-exceptions" replace relative="path" />}
      />
      <Route
        path="/dashboard/:pageId/settings/date-overrides"
        element={<Navigate to="../date-exceptions" replace relative="path" />}
      />

      {/*
        One pathless layout route wraps every organizer screen. This is what
        gives them a shared top bar, breadcrumbs and booking page tabs without
        each page rendering its own chrome, and it means ProtectedRoute is
        declared once rather than repeated fourteen times.
      */}
      <Route
        element={
          <ProtectedRoute>
            <DashboardLayout />
          </ProtectedRoute>
        }
      >
        <Route path="/dashboard" element={<DashboardHomePage />} />
        {/* Organizer-wide, not per-page: analytics span every booking page the organizer owns. */}
        <Route path="/dashboard/analytics" element={<AnalyticsPage />} />
        <Route path="/dashboard/new" element={<CreateBookingPagePage />} />
        <Route path="/dashboard/:pageId" element={<OrganizerDashboardPage />} />
        <Route path="/dashboard/:pageId/sessions/:sessionId" element={<SessionDetailPage />} />
        <Route path="/dashboard/:pageId/settings/hours" element={<WorkingHoursPage />} />
        <Route path="/dashboard/:pageId/settings/date-exceptions" element={<DateExceptionsPage />} />
        <Route path="/dashboard/:pageId/settings/scheduling" element={<SchedulingSettingsPage />} />
        <Route path="/dashboard/:pageId/settings/details" element={<BookingPageDetailsPage />} />
        <Route path="/dashboard/:pageId/settings/meeting" element={<MeetingSettingsPage />} />
        <Route path="/dashboard/:pageId/settings/limits" element={<BookingLimitsPage />} />
        <Route path="/dashboard/:pageId/settings/instructions" element={<BookingInstructionsPage />} />
        <Route path="/dashboard/:pageId/settings/form" element={<BookingFormPage />} />
        <Route path="/dashboard/:pageId/settings/calendar" element={<CalendarIntegrationPage />} />
        <Route path="/dashboard/:pageId/settings/notifications" element={<NotificationSettingsPage />} />

        {/* Scoped to /dashboard rather than declared as a bare `*` inside this
            group: a pathless layout route matches at any depth, so a bare splat
            here would tie with the public catch-all below and could send a
            guest who mistyped a booking link to /login. `/dashboard/*` ranks
            below every route above it and above the public splat, so a real
            screen always wins and only genuinely unknown dashboard paths land
            here - still inside the shell, still signed-in. */}
        <Route path="/dashboard/*" element={<DashboardNotFoundPage />} />
      </Route>

      {/* Everything else, including a mistyped /book/... link. Last, and
          unauthenticated: a wrong address is not a reason to ask for a login. */}
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  );
}

export default App;

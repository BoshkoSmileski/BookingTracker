import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, NetworkError, api } from '../api';

/**
 * The analytics exports are the only endpoints that return bytes rather than
 * JSON, so they exercise a path nothing else in the client does: read the blob,
 * and take the file name from Content-Disposition instead of inventing one.
 *
 * fetch is stubbed here rather than the api object, because the code under test
 * *is* the fetch handling - error mapping included, which must stay identical to
 * every JSON call's.
 */
describe('api.analytics export', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  function stubFetch(response: Partial<Response> & { headers?: Headers }) {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers(),
      blob: async () => new Blob(['x']),
      ...response,
    });
    vi.stubGlobal('fetch', fetchMock);
    return fetchMock;
  }

  const filter = { from: '2026-07-06', to: '2026-08-04', bookingPageId: null, status: null };

  it('requests the CSV endpoint with the filter and a bearer token', async () => {
    const fetchMock = stubFetch({});

    await api.analytics.exportCsv('token-123', filter);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain('/api/organizer/analytics/export/csv');
    expect(url).toContain('from=2026-07-06');
    expect(url).toContain('to=2026-08-04');
    expect(init.headers.Authorization).toBe('Bearer token-123');
  });

  it('requests the PDF endpoint from the same filter', async () => {
    const fetchMock = stubFetch({});

    await api.analytics.exportPdf('token-123', { ...filter, bookingPageId: 'page-1', status: 'Submitted' });

    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('/api/organizer/analytics/export/pdf');
    expect(url).toContain('bookingPageId=page-1');
    expect(url).toContain('status=Submitted');
  });

  it('takes the file name from Content-Disposition', async () => {
    stubFetch({
      headers: new Headers({
        'Content-Disposition': 'attachment; filename=bookingtracker-analytics-20260706-to-20260804.csv',
      }),
    });

    const file = await api.analytics.exportCsv('t', filter);

    // The server encodes the filter in the name, so inventing one on the client
    // would lose that.
    expect(file.fileName).toBe('bookingtracker-analytics-20260706-to-20260804.csv');
  });

  it('prefers the RFC 5987 filename* form when both are present', async () => {
    stubFetch({
      headers: new Headers({
        'Content-Disposition': "attachment; filename=fallback.pdf; filename*=UTF-8''bookingtracker%2Danalytics.pdf",
      }),
    });

    const file = await api.analytics.exportPdf('t', filter);

    expect(file.fileName).toBe('bookingtracker-analytics.pdf');
  });

  it('falls back to a sensible name when the header is missing', async () => {
    // Content-Disposition is not CORS-safelisted; if the API ever stops exposing
    // it, a download must still be usable rather than saved as "undefined".
    stubFetch({ headers: new Headers() });

    expect((await api.analytics.exportCsv('t', filter)).fileName).toBe('analytics.csv');
    expect((await api.analytics.exportPdf('t', filter)).fileName).toBe('analytics.pdf');
  });

  it('surfaces a rejected request as an ApiError carrying the server’s message', async () => {
    stubFetch({
      ok: false,
      status: 404,
      json: async () => ({ title: 'BookingPage was not found.' }),
    });

    // Export endpoints answer errors with the same `{ title }` JSON shape as
    // every other endpoint, even though success is a file.
    const error = await api.analytics.exportCsv('t', filter).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(404);
    expect((error as ApiError).problem.title).toBe('BookingPage was not found.');
  });

  it('surfaces an unreachable server as a NetworkError, not an ApiError', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));

    const error = await api.analytics.exportPdf('t', filter).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(NetworkError);
    expect(error).not.toBeInstanceOf(ApiError);
  });
});

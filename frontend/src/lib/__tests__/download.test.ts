import { afterEach, describe, expect, it, vi } from 'vitest';
import { saveFile } from '../download';

/**
 * saveFile exists because an analytics export cannot be a plain link: the
 * endpoint is behind [Authorize] and a browser navigation carries no bearer
 * token. These pin the three things that make it work - the server's file name
 * reaches the anchor, the anchor is in the document when it is clicked, and the
 * object URL is released afterwards.
 */
describe('saveFile', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  /** Hands saveFile a real anchor whose click is inert, so nothing navigates under jsdom. */
  function interceptAnchor(onClick?: (anchor: HTMLAnchorElement) => void) {
    const anchor = document.createElement('a');
    const click = vi.spyOn(anchor, 'click').mockImplementation(() => onClick?.(anchor));
    vi.spyOn(document, 'createElement').mockReturnValue(anchor);
    return { anchor, click };
  }

  it('downloads the blob under the name the server chose', () => {
    const { anchor, click } = interceptAnchor();

    saveFile({
      blob: new Blob(['a,b\r\n1,2\r\n']),
      fileName: 'bookingtracker-analytics-20260706-to-20260804.csv',
    });

    expect(anchor.download).toBe('bookingtracker-analytics-20260706-to-20260804.csv');
    expect(anchor.href).toMatch(/^blob:/);
    expect(click).toHaveBeenCalledOnce();
  });

  it('releases the object URL rather than pinning the bytes for the life of the tab', () => {
    // Compared against the URL that was actually minted rather than a fixed
    // string: jsdom generates a real one, and revoking some *other* URL would
    // leave this blob pinned for the life of the tab.
    const create = vi.spyOn(URL, 'createObjectURL');
    const revoke = vi.spyOn(URL, 'revokeObjectURL');
    interceptAnchor();

    saveFile({ blob: new Blob(['%PDF-1.4']), fileName: 'report.pdf' });

    expect(revoke).toHaveBeenCalledWith(create.mock.results[0].value);
  });

  it('attaches the anchor before clicking it and detaches it after', () => {
    let connectedAtClick = false;
    // Firefox only honours a click on an element that is in the document.
    const { anchor } = interceptAnchor((a) => { connectedAtClick = a.isConnected; });

    saveFile({ blob: new Blob(['x']), fileName: 'report.pdf' });

    expect(connectedAtClick).toBe(true);
    expect(anchor.isConnected).toBe(false);
  });
});

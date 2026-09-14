import type { DownloadedFile } from './types';

/**
 * Saves a file the API returned.
 *
 * A plain link to the endpoint would not work: every analytics export is behind
 * `[Authorize]`, and a browser navigation cannot carry the bearer token the API
 * requires (and the access token deliberately never lives anywhere a URL could
 * pick it up - see AuthContext). So the file is fetched as a blob through the
 * normal authenticated client and handed to the browser here.
 *
 * The object URL is revoked immediately afterwards. Without that every export
 * pins its own bytes in memory for the lifetime of the tab, which for a
 * multi-megabyte PDF an organizer exports repeatedly is a real leak.
 */
export function saveFile({ blob, fileName }: DownloadedFile): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;

  // Firefox only honours a click on an element that is in the document.
  document.body.appendChild(link);
  link.click();
  link.remove();

  URL.revokeObjectURL(url);
}

import * as signalR from '@microsoft/signalr';
import { HUB_URL } from './config';

/**
 * The hub requires the same JWT the REST API uses ([Authorize] on
 * OrganizerDashboardHub). Browsers can't set an Authorization header on a
 * WebSocket/SSE handshake, so accessTokenFactory sends it as a query string
 * param instead - the backend's JwtBearerEvents.OnMessageReceived reads it
 * back out, scoped strictly to the hub path.
 */
export function createDashboardConnection(getAccessToken: () => string | null): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(HUB_URL, { accessTokenFactory: () => getAccessToken() ?? '' })
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();
}

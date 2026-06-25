import { InjectionToken } from '@angular/core';

export type EventSourceFactory = (url: string) => EventSource;

/**
 * Seam for opening Server-Sent-Event connections. Production binds the real
 * `EventSource`; tests provide a fake through the same DI door, so SSE logic is
 * verified without monkeypatching the global.
 */
export const EVENT_SOURCE_FACTORY = new InjectionToken<EventSourceFactory>('EVENT_SOURCE_FACTORY', {
  providedIn: 'root',
  factory: () => (url: string) => new EventSource(url),
});

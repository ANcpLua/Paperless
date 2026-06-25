import { Injectable, signal } from '@angular/core';

export type NotificationType = 'info' | 'success' | 'warning' | 'danger';

export interface AppNotification {
  readonly id: number;
  readonly message: string;
  readonly type: NotificationType;
}

/**
 * Mirrors the wwwroot SPA showNotification(): toast that auto-dismisses after 2s.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private static readonly AUTO_DISMISS_MS = 2000;
  private nextId = 0;

  readonly notifications = signal<readonly AppNotification[]>([]);

  show(message: string, type: NotificationType = 'info'): void {
    const id = this.nextId++;
    this.notifications.update((list) => [...list, { id, message, type }]);
    setTimeout(() => this.dismiss(id), NotificationService.AUTO_DISMISS_MS);
  }

  dismiss(id: number): void {
    this.notifications.update((list) => list.filter((n) => n.id !== id));
  }
}
